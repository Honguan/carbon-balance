using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Unit.Tests;

public sealed class CalculationRunDiffTests
{
    [Fact]
    public void Compare_ReturnsProductAndEveryStageDelta()
    {
        var baseline = new CalculationRunTotals(
            Guid.NewGuid(),
            7m,
            new Dictionary<LifecycleStage, decimal>
            {
                [LifecycleStage.RawMaterial] = 2m,
                [LifecycleStage.Manufacturing] = 1.5m,
                [LifecycleStage.Distribution] = 1m,
                [LifecycleStage.Use] = 2m,
                [LifecycleStage.EndOfLife] = 0.5m
            });
        var candidate = new CalculationRunTotals(
            Guid.NewGuid(),
            8m,
            new Dictionary<LifecycleStage, decimal>
            {
                [LifecycleStage.RawMaterial] = 3m,
                [LifecycleStage.Manufacturing] = 1.5m,
                [LifecycleStage.Distribution] = 1m,
                [LifecycleStage.Use] = 2m,
                [LifecycleStage.EndOfLife] = 0.5m
            });

        var difference = CalculationRunDiff.Compare(baseline, candidate);

        Assert.Equal(1m, difference.ProductDelta);
        Assert.Equal(5, difference.Stages.Count);
        Assert.Equal(1m, difference.Stages.Single(item => item.Stage == LifecycleStage.RawMaterial).Delta);
        Assert.Equal(0m, difference.Stages.Single(item => item.Stage == LifecycleStage.Use).Delta);
    }

    [Fact]
    public void CanonicalManifest_HashVerificationDetectsTampering()
    {
        const string manifest = "{\"value\":7}";
        var sha256 = CanonicalManifest.ComputeSha256(manifest);

        Assert.True(CanonicalManifest.HasValidSha256(manifest, sha256));
        Assert.False(CanonicalManifest.HasValidSha256("{\"value\":8}", sha256));
    }
}
