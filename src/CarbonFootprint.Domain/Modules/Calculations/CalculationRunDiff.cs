using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Domain.Modules.Calculations;

public sealed record CalculationRunTotals(
    Guid RunId,
    decimal ProductTotal,
    IReadOnlyDictionary<LifecycleStage, decimal> StageTotals);

public sealed record CalculationStageDifference(
    LifecycleStage Stage,
    decimal Baseline,
    decimal Candidate,
    decimal Delta);

public sealed record CalculationRunDifference(
    Guid BaselineRunId,
    Guid CandidateRunId,
    decimal ProductDelta,
    IReadOnlyList<CalculationStageDifference> Stages);

public static class CalculationRunDiff
{
    public static CalculationRunDifference Compare(CalculationRunTotals baseline, CalculationRunTotals candidate)
    {
        var stages = Enum.GetValues<LifecycleStage>()
            .Select(stage =>
            {
                var baselineTotal = baseline.StageTotals.GetValueOrDefault(stage);
                var candidateTotal = candidate.StageTotals.GetValueOrDefault(stage);
                return new CalculationStageDifference(
                    stage,
                    baselineTotal,
                    candidateTotal,
                    candidateTotal - baselineTotal);
            })
            .ToArray();

        return new CalculationRunDifference(
            baseline.RunId,
            candidate.RunId,
            candidate.ProductTotal - baseline.ProductTotal,
            stages);
    }
}
