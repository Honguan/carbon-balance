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
    string? EvidenceSha256);

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
    IReadOnlyList<ActivityDataSnapshot> Activities);

