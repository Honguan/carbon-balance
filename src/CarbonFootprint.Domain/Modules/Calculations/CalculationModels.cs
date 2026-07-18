using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Domain.Modules.Calculations;

public sealed record CalculationLineItem(
    Guid ActivityId,
    LifecycleStage Stage,
    string FormulaId,
    decimal CanonicalActivityValue,
    string ActivityUnitCode,
    Guid FactorVersionId,
    decimal FactorValue,
    string FactorUnit,
    decimal Emissions,
    string EmissionsUnitCode);

public sealed record CalculationStageSummary(LifecycleStage Stage, decimal Emissions);

public sealed record CalculationWarning(string Code, string Message);

public sealed class CalculationRun
{
    public CalculationRun(
        Guid id,
        Guid organizationId,
        Guid projectVersionId,
        Guid? supersedesRunId,
        string canonicalInputManifest,
        string inputSha256,
        string engineBuild,
        string ruleSetVersion,
        string unitCatalogueVersion,
        string gwpVersion,
        string pcrVersion,
        IReadOnlyList<CalculationLineItem> lineItems,
        IReadOnlyList<CalculationStageSummary> stageSummaries,
        IReadOnlyList<CalculationWarning> warnings)
    {
        Id = id;
        OrganizationId = organizationId;
        ProjectVersionId = projectVersionId;
        SupersedesRunId = supersedesRunId;
        CanonicalInputManifest = canonicalInputManifest;
        InputSha256 = inputSha256;
        EngineBuild = engineBuild;
        RuleSetVersion = ruleSetVersion;
        UnitCatalogueVersion = unitCatalogueVersion;
        GwpVersion = gwpVersion;
        PcrVersion = pcrVersion;
        LineItems = lineItems.ToArray();
        StageSummaries = stageSummaries.ToArray();
        Warnings = warnings.ToArray();
        ProductTotal = StageSummaries.Sum(summary => summary.Emissions);
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid ProjectVersionId { get; }

    public Guid? SupersedesRunId { get; }

    public string CanonicalInputManifest { get; }

    public string InputSha256 { get; }

    public string EngineBuild { get; }

    public string RuleSetVersion { get; }

    public string UnitCatalogueVersion { get; }

    public string GwpVersion { get; }

    public string PcrVersion { get; }

    public IReadOnlyList<CalculationLineItem> LineItems { get; }

    public IReadOnlyList<CalculationStageSummary> StageSummaries { get; }

    public IReadOnlyList<CalculationWarning> Warnings { get; }

    public decimal ProductTotal { get; }
}

