using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
        if (organizationIds.Length == 0)
        {
            return new MoenvDeploymentSynchronizationResult(0, 0, 0, 0, 0);
        }

        var download = await _factorSource.DownloadAsync(cancellationToken);
        var createdCount = 0;
        var unchangedCount = 0;
        var publishedExistingCount = 0;
        foreach (var organizationId in organizationIds)
        {
            var result = await SynchronizeOrganizationAsync(
                organizationId,
                actorId: null,
                correlationId,
                download,
                cancellationToken);
            createdCount += result.CreatedCount;
            unchangedCount += result.UnchangedCount;
            publishedExistingCount += result.PublishedExistingCount;
        }

        return new MoenvDeploymentSynchronizationResult(
            organizationIds.Length,
            createdCount,
            unchangedCount,
            publishedExistingCount,
            download.SkippedCount);
    }

    public async Task<MoenvFactorSynchronizationResult> SynchronizeOrganizationAsync(
        Guid organizationId,
        Guid? actorId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var download = await _factorSource.DownloadAsync(cancellationToken);
        return await SynchronizeOrganizationAsync(
            organizationId,
            actorId,
            correlationId,
            download,
            cancellationToken);
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
        var existingFactors = await dbContext.EmissionFactorVersions
            .Where(item => item.SourceReference == MoenvFactorClient.DatasetReference)
            .ToArrayAsync(cancellationToken);
        var groupedFactors = existingFactors
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
            if (current is not null
                && current.Value == source.Value
                && string.Equals(current.SourceDatasetVersion, datasetVersion, StringComparison.Ordinal)
                && string.Equals(current.OriginalDocumentSha256, source.SourceRecordSha256, StringComparison.Ordinal))
            {
                if (current.PublicationStatus == FactorPublicationStatus.Published.ToString()
                    && current.ReviewStatus == FactorReviewStatus.NotRequired.ToString())
                {
                    unchangedCount++;
                }
                else
                {
                    var existingPublishedAt = DateTimeOffset.UtcNow;
                    WithdrawPublishedVersions(
                        dbContext,
                        existingFactors,
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

                continue;
            }

            var factorVersionId = Guid.NewGuid();
            var publishedAt = DateTimeOffset.UtcNow;
            if (current is not null)
            {
                WithdrawPublishedVersions(
                    dbContext,
                    existingFactors,
                    current.FactorId,
                    excludedVersionId: null,
                    actorId,
                    correlationId,
                    publishedAt);
            }

            var factor = new EmissionFactorVersionRecord
            {
                Id = factorVersionId,
                OrganizationId = organizationId,
                FactorId = current?.FactorId ?? Guid.NewGuid(),
                VersionNumber = (current?.VersionNumber ?? 0) + 1,
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
                SupersedesVersionId = current?.Id
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

    private sealed record ExplicitOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }
}
