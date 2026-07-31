using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarbonFootprint.Domain.Modules.DataQuality;

public enum DataQualityDimension
{
    TechnologicalRepresentativeness = 1,
    GeographicalRepresentativeness = 2,
    TemporalRepresentativeness = 3,
    Completeness = 4,
    Reliability = 5
}

public enum DataSourceCategory
{
    PrimaryMeasured = 1,
    PrimaryCalculated = 2,
    SupplierSpecific = 3,
    Secondary = 4,
    Estimated = 5,
    Proxy = 6
}

public enum UncertaintyDistribution
{
    None = 0,
    Uniform = 1,
    Triangular = 2,
    Normal = 3
}

public sealed record DataQualityCriterion(
    DataQualityDimension Dimension,
    int Score,
    decimal Weight,
    string Code,
    string Description)
{
    public void Validate()
    {
        if (Score is < 1 or > 5)
        {
            throw new InvalidOperationException("Data quality score must be between 1 and 5.");
        }

        if (Weight <= 0m)
        {
            throw new InvalidOperationException("Data quality criterion weight must be positive.");
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            throw new InvalidOperationException("Data quality criterion code is required.");
        }
    }
}

public sealed record DataQualityRuleSetVersion(
    Guid Id,
    string Version,
    DateTimeOffset PublishedAt,
    IReadOnlyList<DataQualityCriterion> Criteria,
    bool IsPublished,
    DateTimeOffset? WithdrawnAt = null)
{
    public bool IsAvailable => IsPublished && WithdrawnAt is null;

    public IReadOnlyDictionary<DataQualityDimension, decimal> Weights()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Data quality rule set is not available.");
        }

        foreach (var criterion in Criteria)
        {
            criterion.Validate();
        }

        var groups = Criteria
            .GroupBy(item => item.Dimension)
            .ToDictionary(group => group.Key, group => group.First().Weight);

        foreach (var dimension in Enum.GetValues<DataQualityDimension>())
        {
            if (!groups.ContainsKey(dimension))
            {
                throw new InvalidOperationException($"Missing data quality dimension: {dimension}.");
            }
        }

        return groups;
    }
}

public sealed record DataQualityDimensionScore(
    DataQualityDimension Dimension,
    int Score,
    string CriterionCode,
    string Explanation,
    IReadOnlyList<Guid> EvidenceDocumentVersionIds);

public sealed record DataQualityAssessmentVersion(
    Guid Id,
    Guid ActivityId,
    Guid RuleSetVersionId,
    DataSourceCategory SourceCategory,
    string CollectionMethod,
    string AssessorId,
    DateTimeOffset AssessedAt,
    IReadOnlyList<DataQualityDimensionScore> DimensionScores,
    string Explanation)
{
    public decimal CalculateOverallScore(DataQualityRuleSetVersion rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Id != RuleSetVersionId)
        {
            throw new InvalidOperationException("Assessment and rule-set versions do not match.");
        }

        var weights = rules.Weights();
        var scores = DimensionScores
            .GroupBy(item => item.Dimension)
            .ToDictionary(group => group.Key, group => group.Single());

        foreach (var dimension in Enum.GetValues<DataQualityDimension>())
        {
            if (!scores.TryGetValue(dimension, out var score))
            {
                throw new InvalidOperationException($"Missing assessment score: {dimension}.");
            }

            if (score.Score is < 1 or > 5)
            {
                throw new InvalidOperationException("Assessment score must be between 1 and 5.");
            }

            if (string.IsNullOrWhiteSpace(score.Explanation))
            {
                throw new InvalidOperationException($"Assessment explanation is required for {dimension}.");
            }
        }

        var totalWeight = weights.Values.Sum();
        if (totalWeight <= 0m)
        {
            throw new InvalidOperationException("Total data quality weight must be positive.");
        }

        return scores.Sum(item => item.Value.Score * weights[item.Key]) / totalWeight;
    }

    public string CreateCanonicalHash(decimal overallScore)
    {
        var payload = new
        {
            id = Id,
            activityId = ActivityId,
            ruleSetVersionId = RuleSetVersionId,
            sourceCategory = SourceCategory.ToString(),
            collectionMethod = CollectionMethod,
            assessorId = AssessorId,
            assessedAt = AssessedAt.ToUniversalTime(),
            explanation = Explanation,
            overallScore,
            dimensions = DimensionScores
                .OrderBy(item => item.Dimension)
                .Select(item => new
                {
                    dimension = item.Dimension.ToString(),
                    item.Score,
                    item.CriterionCode,
                    item.Explanation,
                    evidence = item.EvidenceDocumentVersionIds.OrderBy(value => value)
                })
        };

        var json = JsonSerializer.Serialize(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

public sealed record UncertaintyInput(
    Guid InputId,
    string Name,
    decimal BaseValue,
    UncertaintyDistribution Distribution,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal? StandardDeviation,
    decimal ContributionToResult,
    DataSourceCategory SourceCategory)
{
    public void Validate()
    {
        if (BaseValue < 0m)
        {
            throw new InvalidOperationException("Uncertainty base value cannot be negative.");
        }

        if (ContributionToResult < 0m)
        {
            throw new InvalidOperationException("Contribution to result cannot be negative.");
        }

        if (LowerBound is not null && LowerBound < 0m)
        {
            throw new InvalidOperationException("Uncertainty lower bound cannot be negative.");
        }

        if (LowerBound is not null && UpperBound is not null && LowerBound > UpperBound)
        {
            throw new InvalidOperationException("Uncertainty lower bound cannot exceed upper bound.");
        }

        if (StandardDeviation is < 0m)
        {
            throw new InvalidOperationException("Standard deviation cannot be negative.");
        }
    }
}

public sealed record SensitivityResult(
    Guid InputId,
    string Name,
    decimal BaseContribution,
    decimal LowerContribution,
    decimal UpperContribution,
    decimal AbsoluteRange,
    decimal RelativeRange,
    DataSourceCategory SourceCategory);

public sealed record UncertaintyAnalysisResult(
    decimal BaseResult,
    decimal LowerResult,
    decimal UpperResult,
    decimal ConfidenceLevel,
    IReadOnlyList<SensitivityResult> Sensitivities,
    IReadOnlyDictionary<DataSourceCategory, decimal> EmissionShares,
    int SimulationIterations,
    int Seed);

public static class UncertaintyAnalysisService
{
    public static UncertaintyAnalysisResult Analyze(
        IReadOnlyList<UncertaintyInput> inputs,
        decimal confidenceLevel = 0.95m,
        int simulationIterations = 0,
        int seed = 20260731)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (confidenceLevel <= 0m || confidenceLevel >= 1m)
        {
            throw new InvalidOperationException("Confidence level must be between zero and one.");
        }

        if (simulationIterations < 0)
        {
            throw new InvalidOperationException("Simulation iteration count cannot be negative.");
        }

        foreach (var input in inputs)
        {
            input.Validate();
        }

        var sensitivities = inputs
            .Select(CreateSensitivity)
            .OrderByDescending(item => item.AbsoluteRange)
            .ThenBy(item => item.InputId)
            .ToArray();

        var baseResult = inputs.Sum(item => item.ContributionToResult);
        decimal lowerResult;
        decimal upperResult;

        if (simulationIterations > 0 && inputs.Count > 0)
        {
            (lowerResult, upperResult) = MonteCarloInterval(inputs, confidenceLevel, simulationIterations, seed);
        }
        else
        {
            lowerResult = sensitivities.Sum(item => item.LowerContribution);
            upperResult = sensitivities.Sum(item => item.UpperContribution);
        }

        var sourceTotals = inputs
            .GroupBy(item => item.SourceCategory)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.ContributionToResult));
        var emissionShares = Enum.GetValues<DataSourceCategory>()
            .ToDictionary(
                category => category,
                category => baseResult == 0m || !sourceTotals.TryGetValue(category, out var total)
                    ? 0m
                    : total / baseResult);

        return new(
            baseResult,
            lowerResult,
            upperResult,
            confidenceLevel,
            sensitivities,
            emissionShares,
            simulationIterations,
            seed);
    }

    private static SensitivityResult CreateSensitivity(UncertaintyInput input)
    {
        var baseValue = input.BaseValue;
        var lowerValue = input.LowerBound ?? InferLower(input);
        var upperValue = input.UpperBound ?? InferUpper(input);
        var ratio = baseValue == 0m ? 0m : input.ContributionToResult / baseValue;
        var lowerContribution = lowerValue * ratio;
        var upperContribution = upperValue * ratio;
        var range = upperContribution - lowerContribution;
        var relative = input.ContributionToResult == 0m ? 0m : range / input.ContributionToResult;

        return new(
            input.InputId,
            input.Name,
            input.ContributionToResult,
            lowerContribution,
            upperContribution,
            Math.Abs(range),
            Math.Abs(relative),
            input.SourceCategory);
    }

    private static decimal InferLower(UncertaintyInput input)
    {
        if (input.StandardDeviation is null)
        {
            return input.BaseValue;
        }

        return Math.Max(0m, input.BaseValue - 1.96m * input.StandardDeviation.Value);
    }

    private static decimal InferUpper(UncertaintyInput input)
    {
        if (input.StandardDeviation is null)
        {
            return input.BaseValue;
        }

        return input.BaseValue + 1.96m * input.StandardDeviation.Value;
    }

    private static (decimal Lower, decimal Upper) MonteCarloInterval(
        IReadOnlyList<UncertaintyInput> inputs,
        decimal confidenceLevel,
        int iterations,
        int seed)
    {
        var random = new Random(seed);
        var samples = new decimal[iterations];
        for (var index = 0; index < iterations; index++)
        {
            decimal result = 0m;
            foreach (var input in inputs)
            {
                var sampledValue = Sample(input, random);
                var ratio = input.BaseValue == 0m ? 0m : input.ContributionToResult / input.BaseValue;
                result += sampledValue * ratio;
            }

            samples[index] = result;
        }

        Array.Sort(samples);
        var tail = (1m - confidenceLevel) / 2m;
        var lowerIndex = Math.Clamp((int)Math.Floor(tail * iterations), 0, iterations - 1);
        var upperIndex = Math.Clamp((int)Math.Ceiling((1m - tail) * iterations) - 1, 0, iterations - 1);
        return (samples[lowerIndex], samples[upperIndex]);
    }

    private static decimal Sample(UncertaintyInput input, Random random)
    {
        var lower = input.LowerBound ?? InferLower(input);
        var upper = input.UpperBound ?? InferUpper(input);
        if (upper <= lower || input.Distribution == UncertaintyDistribution.None)
        {
            return input.BaseValue;
        }

        var value = random.NextDouble();
        return input.Distribution switch
        {
            UncertaintyDistribution.Uniform => lower + (upper - lower) * (decimal)value,
            UncertaintyDistribution.Triangular => SampleTriangular(lower, input.BaseValue, upper, value),
            UncertaintyDistribution.Normal => SampleNormal(input, lower, upper, random),
            _ => input.BaseValue
        };
    }

    private static decimal SampleTriangular(decimal lower, decimal mode, decimal upper, double value)
    {
        mode = Math.Clamp(mode, lower, upper);
        var range = upper - lower;
        if (range == 0m)
        {
            return mode;
        }

        var threshold = (double)((mode - lower) / range);
        if (value <= threshold)
        {
            return lower + (decimal)Math.Sqrt(value * threshold) * range;
        }

        return upper - (decimal)Math.Sqrt((1d - value) * (1d - threshold)) * range;
    }

    private static decimal SampleNormal(
        UncertaintyInput input,
        decimal lower,
        decimal upper,
        Random random)
    {
        var standardDeviation = input.StandardDeviation
            ?? Math.Max(0m, (upper - lower) / 3.92m);
        if (standardDeviation == 0m)
        {
            return input.BaseValue;
        }

        var u1 = Math.Max(double.Epsilon, random.NextDouble());
        var u2 = random.NextDouble();
        var normal = Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2);
        var sampled = input.BaseValue + standardDeviation * (decimal)normal;
        return Math.Clamp(sampled, lower, upper);
    }
}
