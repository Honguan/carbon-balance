using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Unit.Tests;

public sealed class CalculationEngineValidationTests
{
    [Fact]
    public void Calculate_NegativeActivity_IsRejected()
    {
        var snapshot = CreateSnapshot(rawValue: -1m, canonicalValue: -1m);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CalculationEngine().Calculate(Guid.NewGuid(), snapshot, "engine-test"));

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
            () => new CalculationEngine().Calculate(Guid.NewGuid(), snapshot, "engine-test"));

        Assert.Contains("未發布、已撤回或不在有效期", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalManifest_MatchesOnlyCurrentSnapshot()
    {
        var snapshot = CreateSnapshot(rawValue: 1m, canonicalValue: 1m);
        var inputSha256 = CanonicalManifest.Create(snapshot, "engine-test").Sha256;

        Assert.True(CanonicalManifest.Matches(snapshot, "engine-test", inputSha256));
        Assert.False(CanonicalManifest.Matches(snapshot with { FunctionalUnit = "2 units" }, "engine-test", inputSha256));
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
            "rules-1",
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
