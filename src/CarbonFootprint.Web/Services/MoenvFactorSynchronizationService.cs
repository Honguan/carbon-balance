using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace CarbonFootprint.Web.Services;

public sealed record MoenvFactorSynchronizationResult(
    int CreatedCount,
    int UnchangedCount,
    int PublishedExistingCount,
    int SkippedCount);

public sealed record MoenvDeploymentSynchronizationResult(
    int OrganizationCount,
    int CreatedCount,
    int UnchangedCount,
    int PublishedExistingCount,
    int SkippedCount);

public sealed class MoenvFactorSynchronizationService
{
    private readonly DbContextOptions<CarbonFootprintDbContext> _dbContextOptions;
    private readonly IMoenvFactorSource _factorSource;

    public MoenvFactorSynchronizationService(
        DbContextOptions<CarbonFootprintDbContext> dbContextOptions,
        IMoenvFactorSource factorSource)
    {
        _dbContextOptions = dbContextOptions;
        _factorSource = factorSource;
    }

    public async Task<MoenvDeploymentSynchronizationResult> SynchronizeExistingOrganizationsAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var lookupContext = new CarbonFootprintDbContext(
            _dbContextOptions,
            new UnscopedOrganizationScope());
        var organizationIds = await lookupContext.Organizations
            .IgnoreQueryFilters()
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        return await SynchronizeAsync(organizationIds, actorId: null, correlationId, cancellationToken);
    }

    public async Task<MoenvFactorSynchronizationResult> SynchronizeOrganizationAsync(
        Guid organizationId,
        Guid? actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = await SynchronizeAsync([organizationId], actorId, correlationId, cancellationToken);
        return new MoenvFactorSynchronizationResult(result.CreatedCount, result.UnchangedCount,
            result.PublishedExistingCount, result.SkippedCount);
    }

    private async Task<MoenvDeploymentSynchronizationResult> SynchronizeAsync(
        Guid[] organizationIds, Guid? actorId, string correlationId, CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        for (var attempt = 1; ; attempt++)
        {
            MoenvFactorDownload? download = null;
            string? inputSha256 = null;
            var sourceVersions = Array.Empty<string>();
            var phase = "download";
            var completedOrganizations = 0;
            var created = 0;
            var unchanged = 0;
            var published = 0;

            async Task RecordOutcomeAsync(string outcome, Exception? error = null, int retryAfterSeconds = 0)
            {
                await using var auditContext = new CarbonFootprintDbContext(_dbContextOptions, new UnscopedOrganizationScope());
                auditContext.SystemAuditEvents.Add(new SystemAuditEventRecord
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ActorId = actorId,
                    Action = $"factor.synchronization.{outcome}",
                    ResourceType = "FactorSynchronization",
                    ResourceId = batchId,
                    Source = MoenvFactorClient.DatasetReference,
                    CorrelationId = correlationId,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        BatchId = batchId,
                        Attempt = attempt,
                        Outcome = outcome,
                        Phase = phase,
                        OrganizationIds = organizationIds,
                        CompletedOrganizations = completedOrganizations,
                        InputSha256 = inputSha256,
                        InputHashKind = "ordered-source-record-sha256-list-v1",
                        SourceVersions = sourceVersions,
                        CreatedCount = created,
                        UnchangedCount = unchanged,
                        PublishedExistingCount = published,
                        SkippedCount = download?.SkippedCount,
                        ErrorCode = error?.Data["MoenvErrorCode"] as string ?? error?.GetType().Name,
                        HttpStatusCode = (error as HttpRequestException)?.StatusCode,
                        RetryAfterSeconds = retryAfterSeconds
                    })
                });
                // Persist cancellation/failure separately from catalogue writes; never log request URLs or keys.
                await auditContext.SaveChangesAsync(CancellationToken.None);
            }

            await RecordOutcomeAsync("started");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (organizationIds.Length == 0)
                {
                    await RecordOutcomeAsync("skipped-no-organizations");
                    return new MoenvDeploymentSynchronizationResult(0, 0, 0, 0, 0);
                }
                download = await _factorSource.DownloadAsync(cancellationToken);
                inputSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                    string.Join('\n', download.Records.Select(record => record.SourceRecordSha256).Order(StringComparer.Ordinal)))));
                sourceVersions = download.Records.Select(record => $"CFP_P_02-{record.AnnouncementYear?.ToString() ?? "未標示年份"}")
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                phase = "apply";
                foreach (var organizationId in organizationIds)
                {
                    var result = await SynchronizeOrganizationAsync(organizationId, actorId, correlationId, download, cancellationToken);
                    created += result.CreatedCount;
                    unchanged += result.UnchangedCount;
                    published += result.PublishedExistingCount;
                    completedOrganizations++;
                }
                await RecordOutcomeAsync("completed");
                return new MoenvDeploymentSynchronizationResult(organizationIds.Length, created, unchanged, published, download.SkippedCount);
            }
            catch (Exception exception)
            {
                var transient = exception is HttpRequestException { StatusCode: null or System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests }
                    || exception is HttpRequestException { StatusCode: >= System.Net.HttpStatusCode.InternalServerError }
                    || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
                var retryAfterSeconds = phase == "download" && transient && attempt < 3 && !cancellationToken.IsCancellationRequested
                    ? 1 << (attempt - 1) : 0;
                await RecordOutcomeAsync(cancellationToken.IsCancellationRequested ? "cancelled" : "failed", exception, retryAfterSeconds);
                if (retryAfterSeconds == 0)
                {
                    throw;
                }
                await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds), cancellationToken);
            }
        }
    }

    private async Task<MoenvFactorSynchronizationResult> SynchronizeOrganizationAsync(
        Guid organizationId,
        Guid? actorId,
        string correlationId,
        MoenvFactorDownload download,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new CarbonFootprintDbContext(
            _dbContextOptions,
            new ExplicitOrganizationScope(organizationId));
        var allFactors = await dbContext.EmissionFactorVersions.ToArrayAsync(cancellationToken);
        var synchronizedFactors = allFactors
            .Where(IsSynchronizedFactorVersion)
            .ToArray();
        var groupedFactors = synchronizedFactors
            .GroupBy(
                item => BuildExternalFactorKey(item.Name, item.DenominatorUnitCode, item.SourceName),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.VersionNumber).First(),
                StringComparer.Ordinal);
        var createdCount = 0;
        var unchangedCount = 0;
        var publishedExistingCount = 0;
        foreach (var source in download.Records)
        {
            var sourceName = string.IsNullOrWhiteSpace(source.DepartmentName)
                ? "環境部氣候變遷署"
                : source.DepartmentName.Trim();
            var key = BuildExternalFactorKey(source.Name, source.DenominatorUnitCode, sourceName);
            groupedFactors.TryGetValue(key, out var current);
            var datasetVersion = $"CFP_P_02-{source.AnnouncementYear?.ToString() ?? "未標示年份"}";
            var matchesCurrentSource = current is not null
                && current.Value == source.Value
                && string.Equals(current.SourceDatasetVersion, datasetVersion, StringComparison.Ordinal)
                && string.Equals(current.OriginalDocumentSha256, source.SourceRecordSha256, StringComparison.Ordinal);
            if (matchesCurrentSource)
            {
                if (current!.PublicationStatus == FactorPublicationStatus.Draft.ToString())
                {
                    var existingPublishedAt = DateTimeOffset.UtcNow;
                    WithdrawPublishedVersions(
                        dbContext,
                        allFactors,
                        current.FactorId,
                        current.Id,
                        actorId,
                        correlationId,
                        existingPublishedAt);
                    current.PublicationStatus = FactorPublicationStatus.Published.ToString();
                    current.ReviewStatus = FactorReviewStatus.NotRequired.ToString();
                    current.ReviewedBy = null;
                    current.ReviewedAt = null;
                    current.PublishedAt = existingPublishedAt;
                    dbContext.AuditEvents.Add(CreateAudit(
                        organizationId,
                        actorId,
                        correlationId,
                        "factor.version.auto-published",
                        current.Id,
                        existingPublishedAt));
                    publishedExistingCount++;
                }
                else
                {
                    unchangedCount++;
                }

                continue;
            }

            var factorVersionId = Guid.NewGuid();
            var publishedAt = DateTimeOffset.UtcNow;
            var factorId = current?.FactorId ?? Guid.NewGuid();
            var latestVersion = current is null
                ? null
                : allFactors
                    .Where(item => item.FactorId == factorId)
                    .OrderByDescending(item => item.VersionNumber)
                    .First();
            if (current is not null)
            {
                WithdrawPublishedVersions(
                    dbContext,
                    allFactors,
                    factorId,
                    excludedVersionId: null,
                    actorId,
                    correlationId,
                    publishedAt);
            }

            var factor = new EmissionFactorVersionRecord
            {
                Id = factorVersionId,
                OrganizationId = organizationId,
                FactorId = factorId,
                VersionNumber = (latestVersion?.VersionNumber ?? 0) + 1,
                Name = source.Name,
                Value = source.Value,
                NumeratorUnitCode = "kgCO2e",
                DenominatorUnitCode = source.DenominatorUnitCode,
                Geography = "TW",
                ValidFrom = source.AnnouncementYear.HasValue
                    ? new DateOnly(source.AnnouncementYear.Value, 1, 1)
                    : null,
                ValidTo = null,
                PublicationStatus = FactorPublicationStatus.Published.ToString(),
                SourceDatasetVersion = datasetVersion,
                LicenseCode = "政府資料開放授權條款第1版",
                SourceType = "government-database",
                SourceName = sourceName,
                SourceReference = MoenvFactorClient.DatasetReference,
                DatasetName = "環境部碳足跡排放係數",
                OriginalDocumentName = $"CFP_P_02-record-{source.SourceRecordSha256[..12]}.json",
                OriginalDocumentSha256 = source.SourceRecordSha256,
                Applicability = "環境部公開資料的宣告單位已對應受控單位；選用時仍須確認盤查邊界與適用性。",
                ReviewStatus = FactorReviewStatus.NotRequired.ToString(),
                ReviewedBy = null,
                ReviewedAt = null,
                PublishedAt = publishedAt,
                SupersedesVersionId = latestVersion?.Id
            };
            dbContext.EmissionFactorVersions.Add(factor);
            dbContext.AuditEvents.Add(CreateAudit(
                organizationId,
                actorId,
                correlationId,
                "factor.version.synced",
                factorVersionId,
                publishedAt));
            groupedFactors[key] = factor;
            createdCount++;
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actorId,
            OrganizationId = organizationId,
            Action = "factor.synchronization.completed",
            ResourceType = "Organization",
            ResourceId = organizationId,
            BeforeHash = null,
            AfterHash = null,
            CorrelationId = correlationId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                SourceReference = MoenvFactorClient.DatasetReference,
                CreatedCount = createdCount,
                UnchangedCount = unchangedCount,
                PublishedExistingCount = publishedExistingCount,
                SkippedCount = download.SkippedCount
            })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MoenvFactorSynchronizationResult(
            createdCount,
            unchangedCount,
            publishedExistingCount,
            download.SkippedCount);
    }

    private static void WithdrawPublishedVersions(
        CarbonFootprintDbContext dbContext,
        IReadOnlyList<EmissionFactorVersionRecord> existingFactors,
        Guid factorId,
        Guid? excludedVersionId,
        Guid? actorId,
        string correlationId,
        DateTimeOffset withdrawnAt)
    {
        foreach (var predecessor in existingFactors.Where(item =>
                     item.FactorId == factorId
                     && item.Id != excludedVersionId
                     && item.PublicationStatus == FactorPublicationStatus.Published.ToString()))
        {
            predecessor.PublicationStatus = FactorPublicationStatus.Withdrawn.ToString();
            predecessor.WithdrawnAt = withdrawnAt;
            dbContext.AuditEvents.Add(CreateAudit(
                predecessor.OrganizationId,
                actorId,
                correlationId,
                "factor.version.auto-withdrawn",
                predecessor.Id,
                withdrawnAt));
        }
    }

    private static AuditEventRecord CreateAudit(
        Guid organizationId,
        Guid? actorId,
        string correlationId,
        string action,
        Guid resourceId,
        DateTimeOffset timestamp) =>
        new()
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            ActorId = actorId,
            OrganizationId = organizationId,
            Action = action,
            ResourceType = "EmissionFactorVersion",
            ResourceId = resourceId,
            BeforeHash = null,
            AfterHash = null,
            CorrelationId = correlationId,
            MetadataJson = "{}"
        };

    private static string BuildExternalFactorKey(string name, string unitCode, string sourceName) =>
        $"{name.Trim()}\u001f{unitCode.Trim()}\u001f{sourceName.Trim()}";

    private static bool IsSynchronizedFactorVersion(EmissionFactorVersionRecord factor)
    {
        if (!string.Equals(
                factor.SourceReference,
                MoenvFactorClient.DatasetReference,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (factor.ReviewStatus == FactorReviewStatus.NotRequired.ToString())
        {
            return true;
        }

        return factor.ReviewStatus is nameof(FactorReviewStatus.Pending) or nameof(FactorReviewStatus.Approved)
            && factor.SourceType == "government-database"
            && factor.DatasetName == "環境部碳足跡排放係數"
            && factor.OriginalDocumentSha256.Length == 64
            && factor.OriginalDocumentName == $"CFP_P_02-record-{factor.OriginalDocumentSha256[..12]}.json";
    }

    private sealed record ExplicitOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }
}
