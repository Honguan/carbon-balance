using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Units;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Persistence;

public sealed class InventorySnapshotReader : IInventorySnapshotReader
{
    private readonly CarbonFootprintDbContext _dbContext;

    public InventorySnapshotReader(CarbonFootprintDbContext dbContext) => _dbContext = dbContext;

    public async Task<InventoryProjectSnapshot> ReadAsync(
        Guid projectVersionId,
        CancellationToken cancellationToken)
    {
        var project = await _dbContext.InventoryProjectVersions.SingleAsync(
            item => item.Id == projectVersionId, cancellationToken);
        var activities = await _dbContext.ActivityData
            .Where(item => item.InventoryProjectVersionId == project.Id && item.RetiredAt == null)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var stageDeclarations = await _dbContext.LifecycleStageDeclarations
            .Where(item => item.InventoryProjectVersionId == project.Id)
            .OrderBy(item => item.LifecycleStage)
            .ToArrayAsync(cancellationToken);
        var factorIds = activities.Select(item => item.FactorVersionId).Distinct().ToArray();
        var factorRecords = await _dbContext.EmissionFactorVersions
            .Where(item => factorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var pcr = await _dbContext.PcrVersions.AsNoTracking().SingleAsync(
            item => item.Id == project.PcrVersionId,
            cancellationToken);

        return new InventoryProjectSnapshot(
            project.OrganizationId,
            project.Id,
            project.ProductVersionId,
            project.PeriodStart,
            project.PeriodEnd,
            project.FunctionalUnit,
            project.PcrVersion,
            pcr.FormulaRuleSetVersion,
            "gwp-fixture-p0-v1",
            UnitCatalogue.ResolveVersion(activities.Select(item => item.ConversionRuleVersion)),
            stageDeclarations.Select(item => new StageDeclaration(
                (LifecycleStage)item.LifecycleStage,
                item.IsApplicable,
                string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason)).ToArray(),
            activities.Select(activity =>
            {
                var factor = factorRecords[activity.FactorVersionId];
                return new ActivityDataSnapshot(
                    activity.Id,
                    activity.OrganizationId,
                    (LifecycleStage)activity.LifecycleStage,
                    activity.Name,
                    activity.RawValue,
                    activity.RawUnitCode,
                    activity.CanonicalValue,
                    activity.CanonicalUnitCode,
                    activity.ConversionRuleVersion,
                    activity.PeriodStart,
                    activity.PeriodEnd,
                    new EmissionFactorVersion(
                        factor.Id,
                        factor.FactorId,
                        factor.VersionNumber,
                        factor.Name,
                        factor.Value,
                        factor.NumeratorUnitCode,
                        factor.DenominatorUnitCode,
                        factor.Geography,
                        factor.ValidFrom,
                        factor.ValidTo,
                        Enum.Parse<FactorPublicationStatus>(factor.PublicationStatus),
                        factor.SourceDatasetVersion,
                        factor.LicenseCode,
                        Enum.Parse<FactorReviewStatus>(factor.ReviewStatus),
                        factor.Applicability),
                    activity.EvidenceSha256,
                    Enum.Parse<ActivityDataKind>(activity.ActivityKind),
                    string.IsNullOrWhiteSpace(activity.SupplierOrScenario) ? null : activity.SupplierOrScenario,
                    activity.AllocationFactor,
                    activity.IsEstimated,
                    string.IsNullOrWhiteSpace(activity.EstimationReason) ? null : activity.EstimationReason,
                    activity.DataQuality,
                    activity.AmountFormulaId,
                    activity.FormulaInputsJson,
                    activity.EquipmentCategory,
                    activity.DataSourceType,
                    activity.DataProvider,
                    activity.CollectionMethod,
                    activity.SourceReference);
            }).ToArray(),
            project.DeclaredUnit,
            project.SystemBoundary,
            project.AllocationMethod,
            project.AllocationReason,
            project.Exclusions,
            project.Assumptions,
            project.EstimationReason,
            pcr.CutoffThresholdPercent,
            pcr.RoundingDecimalPlaces,
            pcr.ReportingRequirements);
    }
}
