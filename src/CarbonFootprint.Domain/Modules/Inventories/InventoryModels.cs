using CarbonFootprint.Domain.Modules.Factors;

namespace CarbonFootprint.Domain.Modules.Inventories;

public enum LifecycleStage
{
    RawMaterial = 1,
    Manufacturing = 2,
    Distribution = 3,
    Use = 4,
    EndOfLife = 5
}

public enum ActivityDataKind
{
    Material = 1,
    MaterialTransport = 2,
    Energy = 3,
    ManufacturingWaste = 4,
    OutsourcedTreatmentTransport = 5,
    DistributionTransport = 6,
    UseEnergy = 7,
    UseConsumable = 8,
    EndOfLifeTreatment = 9,
    EndOfLifeTransport = 10
}

public sealed record StageDeclaration(LifecycleStage Stage, bool IsApplicable, string? Reason);

public sealed record ActivityDataSnapshot(
    Guid Id,
    Guid OrganizationId,
    LifecycleStage Stage,
    string Name,
    decimal RawValue,
    string RawUnitCode,
    decimal CanonicalValue,
    string CanonicalUnitCode,
    string ConversionRuleVersion,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    EmissionFactorVersion FactorVersion,
    string? EvidenceSha256,
    ActivityDataKind Kind = ActivityDataKind.Material,
    string? SupplierOrScenario = null,
    decimal AllocationFactor = 1m,
    bool IsEstimated = false,
    string? EstimationReason = null,
    string DataQuality = "primary");

public sealed record InventoryProjectSnapshot(
    Guid OrganizationId,
    Guid ProjectVersionId,
    Guid ProductVersionId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string FunctionalUnit,
    string PcrVersion,
    string RuleSetVersion,
    string GwpVersion,
    string UnitCatalogueVersion,
    IReadOnlyList<StageDeclaration> Stages,
    IReadOnlyList<ActivityDataSnapshot> Activities,
    string DeclaredUnit = "",
    string SystemBoundary = "",
    string AllocationMethod = "",
    string AllocationReason = "",
    string Exclusions = "",
    string Assumptions = "",
    string EstimationReason = "");

public static class ActivityKindRules
{
    public static bool IsAllowed(LifecycleStage stage, ActivityDataKind kind) => stage switch
    {
        LifecycleStage.RawMaterial => kind is ActivityDataKind.Material or ActivityDataKind.MaterialTransport,
        LifecycleStage.Manufacturing => kind is ActivityDataKind.Energy or ActivityDataKind.ManufacturingWaste or ActivityDataKind.OutsourcedTreatmentTransport,
        LifecycleStage.Distribution => kind is ActivityDataKind.DistributionTransport,
        LifecycleStage.Use => kind is ActivityDataKind.UseEnergy or ActivityDataKind.UseConsumable,
        LifecycleStage.EndOfLife => kind is ActivityDataKind.EndOfLifeTreatment or ActivityDataKind.EndOfLifeTransport,
        _ => false
    };
}
