using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Unit.Tests;

public sealed class CalculationEngineValidationTests
{
    private static readonly CalculationBuildProvenance TestBuildProvenance =
        CalculationBuildProvenance.Create("1.0.0-test", new string('a', 40));

    [Fact]
    public void BuildProvenance_RequiresImmutableRevisionUnlessDevelopmentIsExplicit()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CalculationBuildProvenance.Create("1.0.0", null));
        Assert.Throws<InvalidOperationException>(() =>
            CalculationBuildProvenance.Create("1.0.0", "dev"));
        Assert.Throws<InvalidOperationException>(() =>
            CalculationBuildProvenance.Create("dev", new string('a', 40)));

        var development = CalculationBuildProvenance.Create("1.0.0-dev", "dev", allowDevelopment: true);

        Assert.Equal("dev", development.EngineBuild);
        Assert.False(development.IsVerifiable);
        Assert.True(TestBuildProvenance.IsVerifiable);
    }

    [Fact]
    public void Calculate_NegativeActivity_IsRejected()
    {
        var snapshot = CreateSnapshot(rawValue: -1m, canonicalValue: -1m);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CalculationEngine().Calculate(Guid.NewGuid(), snapshot, TestBuildProvenance));

        Assert.Contains("不得為負值", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_WithdrawnFactor_IsRejected()
    {
        var snapshot = CreateSnapshot(
            rawValue: 1m,
            canonicalValue: 1m,
            status: FactorPublicationStatus.Withdrawn);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CalculationEngine().Calculate(Guid.NewGuid(), snapshot, TestBuildProvenance));

        Assert.Contains("未發布、已撤回或不在有效期", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalManifest_MatchesOnlyCurrentSnapshot()
    {
        var snapshot = CreateSnapshot(rawValue: 1m, canonicalValue: 1m);
        var manifest = CanonicalManifest.Create(snapshot, TestBuildProvenance);

        Assert.True(CanonicalManifest.Matches(snapshot, manifest.Json, manifest.Sha256));
        Assert.False(CanonicalManifest.Matches(snapshot with { FunctionalUnit = "2 units" }, manifest.Json, manifest.Sha256));
        Assert.False(CanonicalManifest.Matches(snapshot with { CutoffThresholdPercent = 1m }, manifest.Json, manifest.Sha256));
        Assert.False(CanonicalManifest.Matches(snapshot with { RoundingDecimalPlaces = 6 }, manifest.Json, manifest.Sha256));
        Assert.False(CanonicalManifest.Matches(snapshot with { ReportingRequirements = "PCR report v2" }, manifest.Json, manifest.Sha256));
        Assert.NotEqual(
            manifest.Sha256,
            CanonicalManifest.Create(
                snapshot,
                CalculationBuildProvenance.Create("1.0.0-test", new string('c', 40))).Sha256);
    }

    [Fact]
    public void Calculate_UnsupportedPcrFormulaRuleSet_IsRejected()
    {
        var snapshot = CreateSnapshot(rawValue: 1m, canonicalValue: 1m) with
        {
            RuleSetVersion = "unknown-formulas-v1"
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CalculationEngine().Calculate(Guid.NewGuid(), snapshot, TestBuildProvenance));

        Assert.Contains("Unsupported PCR formula rule set", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ActivityDataKind.Material, "material-input-times-factor-times-allocation-v1")]
    [InlineData(ActivityDataKind.DistributionTransport, "distribution-transport-work-times-factor-times-allocation-v1")]
    [InlineData(ActivityDataKind.UseEnergy, "use-energy-consumption-times-factor-times-allocation-v1")]
    [InlineData(ActivityDataKind.EndOfLifeTreatment, "end-of-life-treatment-times-factor-times-allocation-v1")]
    public void ActivityEmissionFormula_UsesAuditableFormulaPerConsumption(
        ActivityDataKind kind,
        string expectedFormulaId)
    {
        Assert.Equal(expectedFormulaId, ActivityEmissionFormula.Resolve(kind).Id);
        Assert.Equal(3m, ActivityEmissionFormula.Calculate(2m, 3m, 0.5m));
    }

    [Fact]
    public void ActivityAmountFormula_TransportDerivesTonneKilometres()
    {
        var amount = ActivityAmountFormula.Derive(
            ActivityDataKind.DistributionTransport,
            null,
            string.Empty,
            120m,
            500m,
            null,
            null,
            null);

        Assert.Equal(60m, amount.Value);
        Assert.Equal("tonne-km", amount.UnitCode);
        Assert.Equal("transport-distance-times-weight-divided-by-1000-v1", amount.FormulaId);
        Assert.Equal(120m, amount.Inputs["distanceKm"]);
        Assert.Equal(500m, amount.Inputs["weightKg"]);
        Assert.Contains("÷ 1000", amount.FormulaTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityAmountFormula_UseScenarioMultipliesLifetimeFrequencyAndConsumption()
    {
        var amount = ActivityAmountFormula.Derive(
            ActivityDataKind.UseEnergy,
            null,
            "kWh",
            null,
            null,
            5m,
            100m,
            0.2m);

        Assert.Equal(100m, amount.Value);
        Assert.Equal("kWh", amount.UnitCode);
        Assert.Equal("use-lifetime-times-frequency-times-consumption-v1", amount.FormulaId);
        Assert.Equal(5m, amount.Inputs["lifetime"]);
        Assert.Equal(100m, amount.Inputs["frequency"]);
        Assert.Equal(0.2m, amount.Inputs["consumptionPerUse"]);
    }

    [Fact]
    public void Calculate_InconsistentStructuredFormulaInputs_IsRejected()
    {
        var snapshot = CreateSnapshot(rawValue: 60m, canonicalValue: 60m);
        var activity = snapshot.Activities.Single() with
        {
            Stage = LifecycleStage.Distribution,
            Kind = ActivityDataKind.DistributionTransport,
            AmountFormulaId = ActivityAmountFormula.TransportFormulaId,
            FormulaInputsJson = "{\"distanceKm\":120,\"weightKg\":400}"
        };
        var stages = snapshot.Stages.Select(stage => stage.Stage switch
        {
            LifecycleStage.RawMaterial => stage with { IsApplicable = false, Reason = "測試不適用" },
            LifecycleStage.Distribution => stage with { IsApplicable = true, Reason = null },
            _ => stage
        }).ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CalculationEngine().Calculate(
                Guid.NewGuid(),
                snapshot with { Activities = [activity], Stages = stages },
                TestBuildProvenance));

        Assert.Contains("推導結果不一致", exception.Message, StringComparison.Ordinal);
    }

    private static InventoryProjectSnapshot CreateSnapshot(
        decimal rawValue,
        decimal canonicalValue,
        FactorPublicationStatus status = FactorPublicationStatus.Published)
    {
        var organizationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var factor = new EmissionFactorVersion(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            1,
            "測試係數",
            2m,
            "kgCO2e",
            "kg",
            "TW",
            new DateOnly(2025, 1, 1),
            new DateOnly(2027, 12, 31),
            status,
            "dataset-1",
            "test-only");

        var activity = new ActivityDataSnapshot(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            organizationId,
            LifecycleStage.RawMaterial,
            "測試活動",
            rawValue,
            "kg",
            canonicalValue,
            "kg",
            "units-1",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            factor,
            null);

        return new InventoryProjectSnapshot(
            organizationId,
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            "1 unit",
            "pcr-test-1",
            ActivityEmissionFormula.PcrFormulaRuleSetV1,
            "gwp-1",
            "units-1",
            [
                new StageDeclaration(LifecycleStage.RawMaterial, true, null),
                new StageDeclaration(LifecycleStage.Manufacturing, false, "測試不適用"),
                new StageDeclaration(LifecycleStage.Distribution, false, "測試不適用"),
                new StageDeclaration(LifecycleStage.Use, false, "測試不適用"),
                new StageDeclaration(LifecycleStage.EndOfLife, false, "測試不適用")
            ],
            [activity]);
    }
}
