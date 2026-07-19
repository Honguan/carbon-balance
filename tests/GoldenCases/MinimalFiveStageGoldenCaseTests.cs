using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.GoldenCases.Tests;

public sealed class MinimalFiveStageGoldenCaseTests
{
    [Fact]
    public void Calculate_HandCalculatedFiveStageCase_MatchesEveryLineAndTotal()
    {
        var snapshot = CreateSnapshot();
        var engine = new CalculationEngine();

        var run = engine.Calculate(
            Guid.Parse("90000000-0000-0000-0000-000000000001"),
            snapshot,
            "engine-golden-1");

        Assert.Collection(
            run.LineItems,
            line => Assert.Equal(2m, line.Emissions),
            line => Assert.Equal(1.5m, line.Emissions),
            line => Assert.Equal(1m, line.Emissions),
            line => Assert.Equal(2m, line.Emissions),
            line => Assert.Equal(0.5m, line.Emissions));
        Assert.Equal(7m, run.ProductTotal);
        Assert.Equal(run.ProductTotal, run.LineItems.Sum(line => line.Emissions));
        Assert.Equal(run.ProductTotal, run.StageSummaries.Sum(stage => stage.Emissions));
        Assert.Equal(
            [
                "material-input-times-factor-times-allocation-v1",
                "manufacturing-consumption-times-factor-times-allocation-v1",
                "distribution-transport-work-times-factor-times-allocation-v1",
                "use-energy-consumption-times-factor-times-allocation-v1",
                "end-of-life-treatment-times-factor-times-allocation-v1"
            ],
            run.LineItems.Select(line => line.FormulaId));
        Assert.Equal(
            [
                ActivityAmountFormula.DirectFormulaId,
                ActivityAmountFormula.DirectFormulaId,
                ActivityAmountFormula.TransportFormulaId,
                ActivityAmountFormula.UseScenarioFormulaId,
                ActivityAmountFormula.DirectFormulaId
            ],
            run.LineItems.Select(line => line.ActivityAmountFormulaId));
        Assert.Empty(run.Warnings);
    }

    [Fact]
    public void Calculate_SameCanonicalInput_ProducesSameHashAndValues()
    {
        var snapshot = CreateSnapshot();
        var engine = new CalculationEngine();

        var first = engine.Calculate(Guid.NewGuid(), snapshot, "engine-golden-1");
        var second = engine.Calculate(Guid.NewGuid(), snapshot, "engine-golden-1");

        Assert.Equal(first.InputSha256, second.InputSha256);
        Assert.Equal(first.ProductTotal, second.ProductTotal);
        Assert.Equal(64, first.InputSha256.Length);
    }

    [Fact]
    public void Calculate_AllocationFactor_IsAppliedAndFrozenInManifest()
    {
        var baseline = CreateSnapshot();
        var activities = baseline.Activities
            .Select((activity, index) => index == 0 ? activity with { AllocationFactor = 0.5m } : activity)
            .ToArray();
        var allocated = baseline with { Activities = activities };
        var engine = new CalculationEngine();

        var run = engine.Calculate(Guid.NewGuid(), allocated, "engine-golden-1");

        Assert.Equal(1m, run.LineItems[0].Emissions);
        Assert.Equal(6m, run.ProductTotal);
        Assert.Contains("\"allocationFactor\":0.5", run.CanonicalInputManifest, StringComparison.Ordinal);
        Assert.NotEqual(engine.Calculate(Guid.NewGuid(), baseline, "engine-golden-1").InputSha256, run.InputSha256);
    }

    [Fact]
    public void Calculate_ZeroAllocationFactor_IsRejected()
    {
        var baseline = CreateSnapshot();
        var activities = baseline.Activities
            .Select((activity, index) => index == 0 ? activity with { AllocationFactor = 0m } : activity)
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            new CalculationEngine().Calculate(Guid.NewGuid(), baseline with { Activities = activities }, "engine-golden-1"));
    }

    private static InventoryProjectSnapshot CreateSnapshot()
    {
        var organizationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var periodStart = new DateOnly(2026, 1, 1);
        var periodEnd = new DateOnly(2026, 12, 31);
        var factorIndex = 0;
        var activityIndex = 0;

        EmissionFactorVersion Factor(string name, decimal value, string denominator)
        {
            factorIndex++;
            return new EmissionFactorVersion(
                Guid.Parse($"20000000-0000-0000-0000-{factorIndex:D12}"),
                Guid.Parse($"21000000-0000-0000-0000-{factorIndex:D12}"),
                1,
                name,
                value,
                "kgCO2e",
                denominator,
                "TW",
                periodStart,
                periodEnd,
                FactorPublicationStatus.Published,
                "golden-dataset-1",
                "test-fixture");
        }

        ActivityDataSnapshot Activity(
            LifecycleStage stage,
            string name,
            decimal rawValue,
            string rawUnit,
            decimal canonicalValue,
            string canonicalUnit,
            EmissionFactorVersion factor)
        {
            activityIndex++;
            return new ActivityDataSnapshot(
                Guid.Parse($"30000000-0000-0000-0000-{activityIndex:D12}"),
                organizationId,
                stage,
                name,
                rawValue,
                rawUnit,
                canonicalValue,
                canonicalUnit,
                "units-golden-1",
                periodStart,
                periodEnd,
                factor,
                null,
                stage switch
                {
                    LifecycleStage.RawMaterial => ActivityDataKind.Material,
                    LifecycleStage.Manufacturing => ActivityDataKind.Energy,
                    LifecycleStage.Distribution => ActivityDataKind.DistributionTransport,
                    LifecycleStage.Use => ActivityDataKind.UseEnergy,
                    LifecycleStage.EndOfLife => ActivityDataKind.EndOfLifeTreatment,
                    _ => throw new ArgumentOutOfRangeException(nameof(stage))
                },
                null,
                1m,
                false,
                null,
                "primary",
                stage switch
                {
                    LifecycleStage.Distribution => ActivityAmountFormula.TransportFormulaId,
                    LifecycleStage.Use => ActivityAmountFormula.UseScenarioFormulaId,
                    _ => ActivityAmountFormula.DirectFormulaId
                },
                stage switch
                {
                    LifecycleStage.Distribution => "{\"distanceKm\":10,\"weightKg\":1000}",
                    LifecycleStage.Use => "{\"lifetime\":1,\"frequency\":1,\"consumptionPerUse\":4}",
                    _ => "{}"
                });
        }

        var activities = new[]
        {
            Activity(LifecycleStage.RawMaterial, "原料", 1000m, "g", 1m, "kg", Factor("原料係數", 2m, "kg")),
            Activity(LifecycleStage.Manufacturing, "製造用電", 3m, "kWh", 3m, "kWh", Factor("電力係數", 0.5m, "kWh")),
            Activity(LifecycleStage.Distribution, "配送", 10m, "tonne-km", 10m, "tonne-km", Factor("運輸係數", 0.1m, "tonne-km")),
            Activity(LifecycleStage.Use, "使用用電", 4m, "kWh", 4m, "kWh", Factor("使用電力係數", 0.5m, "kWh")),
            Activity(LifecycleStage.EndOfLife, "廢棄處理", 2m, "kg", 2m, "kg", Factor("廢棄係數", 0.25m, "kg"))
        };

        return new InventoryProjectSnapshot(
            organizationId,
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            periodStart,
            periodEnd,
            "1 個產品",
            "pcr-golden-unapproved-1",
            "rules-golden-1",
            "gwp-ar6-100y-fixture",
            "units-golden-1",
            Enum.GetValues<LifecycleStage>()
                .Select(stage => new StageDeclaration(stage, true, null))
                .ToArray(),
            activities);
    }
}
