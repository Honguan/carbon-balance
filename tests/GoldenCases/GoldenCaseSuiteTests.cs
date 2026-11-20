using CarbonFootprint.Domain.Modules.Calculations;

namespace CarbonFootprint.GoldenCases.Tests;

public sealed class GoldenCaseSuiteTests
{
    /// <summary>Categories required by the golden-case acceptance criteria (issue #57); uncertainty is pending #23.</summary>
    private static readonly string[] RequiredCoverage =
    [
        "single-factor", "multi-stage", "not-applicable-stage", "unit-conversion", "boundary-date", "allocation",
        "multi-leg-transport", "use-phase", "end-of-life", "cutoff", "rounding-precision", "supersession", "readiness",
        "data-quality", "negative-value", "zero-value", "large-decimal", "invalid-unit", "invalid-dependency"
    ];

    public static TheoryData<string> CaseIds => new(GoldenCaseSuite.Cases.Select(item => item.Id));

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void ReferenceCase_MatchesIndependentExpectation(string caseId)
    {
        var diffs = GoldenCaseSuite.Evaluate(GoldenCaseSuite.Find(caseId));

        Assert.True(diffs.Count == 0, $"{caseId} 與參考值不符：{Environment.NewLine}{string.Join(Environment.NewLine, diffs)}");
    }

    [Fact]
    public void Suite_IsVersionedTraceableAndCoversEveryRequiredCategory()
    {
        var cases = GoldenCaseSuite.Cases;

        Assert.Equal(cases.Count, cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(cases, item =>
        {
            Assert.Equal("golden-suite-v1", item.SuiteVersion);
            Assert.NotNull(item.Versions);
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.NotEmpty(item.Covers);
            Assert.All(item.Factors, factor => Assert.False(string.IsNullOrWhiteSpace(factor.DatasetVersion)));
            // Successful cases must show the hand calculation the expected values came from.
            if (item.Expected.Error is null)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Derivation), $"{item.Id} 缺少手算推導");
                Assert.NotNull(item.Expected.ProductTotal);
                Assert.NotNull(item.Expected.Lines);
            }
        });
        Assert.Empty(RequiredCoverage.Except(cases.SelectMany(item => item.Covers), StringComparer.Ordinal));
    }

    [Fact]
    public void FactorSupersession_DoesNotChangeTheEarlierRun()
    {
        var engine = new CalculationEngine();
        var before = GoldenCaseSuite.BuildSnapshot(GoldenCaseSuite.Find("GC-010A"));
        var after = GoldenCaseSuite.BuildSnapshot(GoldenCaseSuite.Find("GC-010B"));

        var original = engine.Calculate(Guid.NewGuid(), before, GoldenCaseSuite.BuildProvenance);
        var superseding = engine.Calculate(Guid.NewGuid(), after, GoldenCaseSuite.BuildProvenance, original.Id);
        var replay = engine.Calculate(Guid.NewGuid(), before, GoldenCaseSuite.BuildProvenance);

        Assert.NotEqual(original.InputSha256, superseding.InputSha256);
        Assert.Equal(original.Id, superseding.SupersedesRunId);
        Assert.Equal(original.InputSha256, replay.InputSha256);
        Assert.Equal(original.CanonicalInputManifest, replay.CanonicalInputManifest);
        Assert.Equal(original.ProductTotal, replay.ProductTotal);
        Assert.Equal(
            original.LineItems.Select(item => (item.FactorVersionId, item.FactorValue, item.Emissions)),
            replay.LineItems.Select(item => (item.FactorVersionId, item.FactorValue, item.Emissions)));
    }
}
