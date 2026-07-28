using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Services;

public sealed record MoenvFactorSynchronizationResult(
    int CreatedCount,
    int UnchangedCount,
    int SkippedCount);

public sealed record MoenvDeploymentSynchronizationResult(
    int OrganizationCount,
    int CreatedCount,
    int UnchangedCount,
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
            return new MoenvDeploymentSynchronizationResult(0, 0, 0, 0);
        }

        var download = await _factorSource.DownloadAsync(cancellationToken);
        var createdCount = 0;
        var unchangedCount = 0;
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
        }

        return new MoenvDeploymentSynchronizationResult(
            organizationIds.Length,
            createdCount,
            unchangedCount,
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
                && string.Equals(current.SourceDatasetVersion, datasetVersion, StringComparison.Ordinal))
            {
                unchangedCount++;
                continue;
            }

            var factorVersionId = Guid.NewGuid();
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
                PublicationStatus = FactorPublicationStatus.Draft.ToString(),
                SourceDatasetVersion = datasetVersion,
                LicenseCode = "政府資料開放授權條款第1版",
                SourceType = "government-database",
                SourceName = sourceName,
                SourceReference = MoenvFactorClient.DatasetReference,
                DatasetName = "環境部碳足跡排放係數",
                OriginalDocumentName = $"CFP_P_02-record-{source.SourceRecordSha256[..12]}.json",
                OriginalDocumentSha256 = source.SourceRecordSha256,
                Applicability = "來源資料的宣告單位已對應受控單位；發布前仍須確認盤查邊界與適用性。",
                ReviewStatus = FactorReviewStatus.Pending.ToString(),
                SupersedesVersionId = current?.Id
            };
            dbContext.EmissionFactorVersions.Add(factor);
            dbContext.AuditEvents.Add(new AuditEventRecord
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ActorId = actorId,
                OrganizationId = organizationId,
                Action = "factor.version.synced",
                ResourceType = "EmissionFactorVersion",
                ResourceId = factorVersionId,
                BeforeHash = null,
                AfterHash = null,
                CorrelationId = correlationId,
                MetadataJson = "{}"
            });
            groupedFactors[key] = factor;
            createdCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new MoenvFactorSynchronizationResult(
            createdCount,
            unchangedCount,
            download.SkippedCount);
    }

    private static string BuildExternalFactorKey(string name, string unitCode, string sourceName) =>
        $"{name.Trim()}\u001f{unitCode.Trim()}\u001f{sourceName.Trim()}";

    private sealed record ExplicitOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }
}
