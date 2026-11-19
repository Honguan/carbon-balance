using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Units;

namespace CarbonFootprint.GoldenCases.Tests;

/// <summary>
/// Loads the versioned reference cases under Cases/ and runs them through the production unit catalogue,
/// activity-amount formulas, calculation engine and readiness validator. Expected values live only in the JSON files.
/// </summary>
public static class GoldenCaseSuite
{
    public static readonly CalculationBuildProvenance BuildProvenance =
        CalculationBuildProvenance.Create("1.0.0-golden", new string('b', 40));

    private static readonly Guid OrganizationId = Guid.Parse("10000000-0000-0000-0000-000000000057");
    private static readonly DateOnly DefaultPeriodStart = new(2026, 1, 1);
    private static readonly DateOnly DefaultPeriodEnd = new(2026, 12, 31);
    private static readonly DateOnly ReadinessAsOf = new(2027, 1, 15);
    private static readonly string CleanEvidenceSha256 = new('a', 64);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Lazy<IReadOnlyList<GoldenCase>> LoadedCases = new(LoadCases);
    private static readonly Lazy<UnitCatalogue> Units = new(LoadUnits);

    public static IReadOnlyList<GoldenCase> Cases => LoadedCases.Value;

    public static GoldenCase Find(string id) => Cases.Single(item => item.Id == id);

    public static InventoryProjectSnapshot BuildSnapshot(GoldenCase goldenCase)
    {
        var periodStart = goldenCase.Project?.PeriodStart ?? DefaultPeriodStart;
        var periodEnd = goldenCase.Project?.PeriodEnd ?? DefaultPeriodEnd;
        var factors = goldenCase.Factors.ToDictionary(
            item => item.Key,
            item => new EmissionFactorVersion(
                KeyGuid("20", goldenCase.Id, $"{item.Key}-v{item.Version}"),
                KeyGuid("21", goldenCase.Id, item.Key),
                item.Version,
                item.Key,
                item.Value,
                "kgCO2e",
                item.Denominator,
                "TW",
                item.ValidFrom ?? periodStart,
                item.ValidTo ?? periodEnd,
                item.Status,
                item.DatasetVersion,
                "test-fixture",
                item.ReviewStatus),
            StringComparer.Ordinal);

        var activities = goldenCase.Activities.Select(item =>
        {
            decimal rawValue, canonicalValue;
            string rawUnit, formulaId, inputsJson;
            if (ActivityAmountFormula.IsTransport(item.Kind) || ActivityAmountFormula.IsUseScenario(item.Kind))
            {
                var derived = ActivityAmountFormula.Derive(item.Kind, null, item.CanonicalUnit, item.DistanceKm, item.WeightKg,
                    item.Lifetime, item.Frequency, item.ConsumptionPerUse);
                (rawValue, canonicalValue, rawUnit) = (derived.Value, derived.Value, derived.UnitCode);
                formulaId = derived.FormulaId;
                inputsJson = JsonSerializer.Serialize(derived.Inputs);
            }
            else
            {
                rawValue = item.RawValue ?? throw new InvalidDataException($"{goldenCase.Id}/{item.Key} 缺少 rawValue。");
                rawUnit = item.RawUnit ?? throw new InvalidDataException($"{goldenCase.Id}/{item.Key} 缺少 rawUnit。");
                canonicalValue = Units.Value.Convert(rawValue, rawUnit, item.CanonicalUnit);
                formulaId = ActivityAmountFormula.DirectFormulaId;
                inputsJson = "{}";
            }

            return new ActivityDataSnapshot(
                ActivityId(goldenCase, item.Key),
                OrganizationId,
                item.Stage,
                item.Key,
                rawValue,
                rawUnit,
                canonicalValue,
                item.CanonicalUnit,
                goldenCase.RecordedVersions.UnitCatalogue,
                item.PeriodStart ?? periodStart,
                item.PeriodEnd ?? periodEnd,
                factors[item.Factor],
                item.Evidence == "missing" ? null : CleanEvidenceSha256,
                item.Kind,
                null,
                item.Allocation,
                item.IsEstimated,
                item.EstimationReason,
                item.DataQuality,
                formulaId,
                inputsJson,
                DataProvider: "golden-fixture");
        }).ToArray();

        var stages = Enum.GetValues<LifecycleStage>()
            .Select(stage => goldenCase.ApplicableStages.Contains(stage)
                ? new StageDeclaration(stage, true, null)
                : new StageDeclaration(stage, false, goldenCase.NotApplicableReason))
            .ToArray();

        return new InventoryProjectSnapshot(
            OrganizationId,
            KeyGuid("40", goldenCase.Id, "project"),
            KeyGuid("50", goldenCase.Id, "product"),
            periodStart,
            periodEnd,
            "1 piece",
            goldenCase.RecordedVersions.Pcr,
            goldenCase.RecordedVersions.RuleSet,
            goldenCase.RecordedVersions.Gwp,
            goldenCase.RecordedVersions.UnitCatalogue,
            stages,
            activities,
            Exclusions: goldenCase.Project?.Exclusions ?? string.Empty,
            CutoffThresholdPercent: goldenCase.Project?.CutoffThresholdPercent ?? 0m,
            RoundingDecimalPlaces: goldenCase.Project?.RoundingDecimalPlaces ?? 3);
    }

    /// <summary>Runs one case and returns every difference from its expected outcome; empty means the case passes.</summary>
    public static IReadOnlyList<string> Evaluate(GoldenCase goldenCase)
    {
        var expected = goldenCase.Expected;
        var diffs = new List<string>();
        InventoryProjectSnapshot snapshot;
        CalculationRun run;
        try
        {
            snapshot = BuildSnapshot(goldenCase);
            run = new CalculationEngine().Calculate(KeyGuid("90", goldenCase.Id, "run"), snapshot, BuildProvenance);
        }
        catch (InvalidOperationException exception)
        {
            if (expected.Error is null) return [$"非預期拒絕：{exception.Message}"];
            return exception.Message.Contains(expected.Error, StringComparison.Ordinal)
                ? []
                : [$"拒絕訊息不符：預期包含「{expected.Error}」，實際「{exception.Message}」"];
        }

        if (expected.Error is not null) return [$"預期拒絕（{expected.Error}），但計算成功，總計 {Format(run.ProductTotal)}"];

        foreach (var line in expected.Lines ?? [])
        {
            var actual = run.LineItems.SingleOrDefault(item => item.ActivityId == ActivityId(goldenCase, line.Activity));
            if (actual is null)
            {
                diffs.Add($"活動 {line.Activity}：缺少計算明細");
                continue;
            }
            Compare(diffs, $"活動 {line.Activity} canonical 活動量", line.CanonicalValue, actual.CanonicalActivityValue);
            Compare(diffs, $"活動 {line.Activity} 排放", line.Emissions, actual.Emissions);
        }
        if ((expected.Lines?.Count ?? 0) != run.LineItems.Count)
        {
            diffs.Add($"明細筆數：預期 {expected.Lines?.Count ?? 0}，實際 {run.LineItems.Count}");
        }

        foreach (var (stage, value) in expected.Stages ?? [])
        {
            Compare(diffs, $"階段 {stage}", value, run.StageSummaries.Single(item => item.Stage == stage).Emissions);
        }
        if (expected.ProductTotal is { } total) Compare(diffs, "產品總計", total, run.ProductTotal);

        // Internal consistency holds for every successful case, independent of the expected values.
        Compare(diffs, "明細加總 = 產品總計", run.ProductTotal, run.LineItems.Sum(item => item.Emissions));
        Compare(diffs, "階段加總 = 產品總計", run.ProductTotal, run.StageSummaries.Sum(item => item.Emissions));

        if (run.PcrVersion != goldenCase.RecordedVersions.Pcr || run.RuleSetVersion != goldenCase.RecordedVersions.RuleSet
            || run.GwpVersion != goldenCase.RecordedVersions.Gwp || run.UnitCatalogueVersion != goldenCase.RecordedVersions.UnitCatalogue)
        {
            diffs.Add("計算版本未完整保存案例的 PCR／規則集／GWP／單位目錄版本");
        }
        // Each line must point at the exact factor version the case selected (the version record carries its dataset version).
        foreach (var activity in goldenCase.Activities)
        {
            var factor = snapshot.Activities.Single(item => item.Id == ActivityId(goldenCase, activity.Key)).FactorVersion;
            var line = run.LineItems.SingleOrDefault(item => item.ActivityId == ActivityId(goldenCase, activity.Key));
            if (line is not null && (line.FactorVersionId != factor.Id || line.FactorValue != factor.Value))
            {
                diffs.Add($"活動 {activity.Key}：明細未使用案例指定的係數版本 {activity.Factor} v{factor.VersionNumber}");
            }
            if (!run.CanonicalInputManifest.Contains($"\"factorVersionId\":\"{factor.Id}\"", StringComparison.Ordinal))
            {
                diffs.Add($"活動 {activity.Key}：輸入清單未凍結係數版本 {factor.Id}");
            }
        }

        if (expected.Warnings is { } warnings)
        {
            var actualCodes = run.Warnings.Select(item => item.Code).Order(StringComparer.Ordinal).ToArray();
            var expectedCodes = warnings.Order(StringComparer.Ordinal).ToArray();
            if (!actualCodes.SequenceEqual(expectedCodes))
            {
                diffs.Add($"警示：預期 [{string.Join(", ", expectedCodes)}]，實際 [{string.Join(", ", actualCodes)}]");
            }
        }

        if (expected.DataQuality is { } quality)
        {
            var actualQuality = string.Join(", ", run.DataQualitySummary.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"));
            var expectedQuality = string.Join(", ", quality.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"));
            if (actualQuality != expectedQuality) diffs.Add($"資料品質：預期 [{expectedQuality}]，實際 [{actualQuality}]");
        }

        if (expected.ReadinessBlocking is { } blocking)
        {
            var evidence = goldenCase.Activities
                .Where(item => item.Evidence != "missing")
                .Select(item => new ReadinessEvidence(
                    ActivityId(goldenCase, item.Key), CleanEvidenceSha256, item.Evidence == "pending-scan" ? "Pending" : "Clean"))
                .ToArray();
            var report = InventoryReadinessValidator.Validate(new InventoryReadinessInput(snapshot, [], evidence, null, ReadinessAsOf));
            var actualCodes = report.Findings.Where(item => item.Severity == ReadinessSeverity.BlockingError)
                .Select(item => item.Code).Distinct().Order(StringComparer.Ordinal).ToArray();
            var expectedCodes = blocking.Order(StringComparer.Ordinal).ToArray();
            if (!actualCodes.SequenceEqual(expectedCodes))
            {
                diffs.Add($"送審阻擋：預期 [{string.Join(", ", expectedCodes)}]，實際 [{string.Join(", ", actualCodes)}]");
            }
        }

        return diffs;
    }

    private static void Compare(List<string> diffs, string label, decimal expected, decimal actual)
    {
        // Stable rule: exact decimal equality (tolerance 0); scale differences such as 2.50 vs 2.5 are equal.
        if (expected != actual) diffs.Add($"{label}：預期 {Format(expected)}，實際 {Format(actual)}，差 {Format(actual - expected)}");
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static Guid ActivityId(GoldenCase goldenCase, string key) => KeyGuid("30", goldenCase.Id, key);

    /// <summary>Stable identifiers derived from the case id and key, so manifests and ordering are reproducible.</summary>
    private static Guid KeyGuid(string prefix, string caseId, string key)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{caseId}/{key}"));
        return Guid.Parse(prefix + Convert.ToHexStringLower(hash)[..30]);
    }

    private static string CasesDirectory => Path.Combine(AppContext.BaseDirectory, "Cases");

    private static IReadOnlyList<GoldenCase> LoadCases()
    {
        var cases = new List<GoldenCase>();
        foreach (var path in Directory.GetFiles(CasesDirectory, "*-cases.json").Order(StringComparer.Ordinal))
        {
            var suite = JsonSerializer.Deserialize<GoldenSuiteFile>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"無法讀取案例檔 {path}。");
            foreach (var item in suite.Cases)
            {
                item.SuiteVersion = suite.SuiteVersion;
                item.Versions ??= suite.Versions;
                cases.Add(item);
            }
        }
        return cases;
    }

    private static UnitCatalogue LoadUnits()
    {
        var file = JsonSerializer.Deserialize<UnitFile>(File.ReadAllText(Path.Combine(CasesDirectory, "units-p0-v2.json")), JsonOptions)
            ?? throw new InvalidDataException("無法讀取單位目錄。");
        return new UnitCatalogue(file.CatalogueVersion, file.Units.Select(item => new UnitDefinition(
            Guid.NewGuid(), item.Code, item.Dimension, item.ScaleToCanonical, 0m, item.CanonicalCode, file.CatalogueVersion, item.Aliases)));
    }

    private sealed class GoldenSuiteFile
    {
        public string SuiteVersion { get; init; } = "";
        public GoldenVersions? Versions { get; init; }
        public List<GoldenCase> Cases { get; init; } = [];
    }

    private sealed class UnitFile
    {
        public string CatalogueVersion { get; init; } = "";
        public List<UnitEntry> Units { get; init; } = [];
    }

    private sealed class UnitEntry
    {
        public string Code { get; init; } = "";
        public string Dimension { get; init; } = "";
        public decimal ScaleToCanonical { get; init; }
        public string CanonicalCode { get; init; } = "";
        public List<string> Aliases { get; init; } = [];
    }
}

public sealed class GoldenCase
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string SuiteVersion { get; set; } = "";
    public List<string> Covers { get; init; } = [];
    public GoldenVersions? Versions { get; set; }
    public GoldenVersions RecordedVersions => Versions ?? throw new InvalidDataException($"{Id} 未記錄 PCR／規則集／GWP／單位目錄版本。");
    public List<LifecycleStage> ApplicableStages { get; init; } = [];
    public string NotApplicableReason { get; init; } = "案例範圍外";
    public GoldenProject? Project { get; init; }
    public List<GoldenFactor> Factors { get; init; } = [];
    public List<GoldenActivity> Activities { get; init; } = [];
    public string? Derivation { get; init; }
    public GoldenExpected Expected { get; init; } = new();

    public override string ToString() => Id;
}

public sealed record GoldenVersions(string Pcr, string RuleSet, string Gwp, string UnitCatalogue);

public sealed class GoldenProject
{
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
    public decimal? CutoffThresholdPercent { get; init; }
    public int? RoundingDecimalPlaces { get; init; }
    public string? Exclusions { get; init; }
}

public sealed class GoldenFactor
{
    public string Key { get; init; } = "";
    public int Version { get; init; } = 1;
    public decimal Value { get; init; }
    public string Denominator { get; init; } = "";
    public string DatasetVersion { get; init; } = "";
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public FactorPublicationStatus Status { get; init; } = FactorPublicationStatus.Published;
    public FactorReviewStatus ReviewStatus { get; init; } = FactorReviewStatus.Approved;
}

public sealed class GoldenActivity
{
    public string Key { get; init; } = "";
    public LifecycleStage Stage { get; init; }
    public ActivityDataKind Kind { get; init; }
    public string Factor { get; init; } = "";
    public decimal? RawValue { get; init; }
    public string? RawUnit { get; init; }
    public string CanonicalUnit { get; init; } = "";
    public decimal? DistanceKm { get; init; }
    public decimal? WeightKg { get; init; }
    public decimal? Lifetime { get; init; }
    public decimal? Frequency { get; init; }
    public decimal? ConsumptionPerUse { get; init; }
    public decimal Allocation { get; init; } = 1m;
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
    public string DataQuality { get; init; } = "primary";
    public bool IsEstimated { get; init; }
    public string? EstimationReason { get; init; }
    /// <summary>clean (default), missing, or pending-scan.</summary>
    public string Evidence { get; init; } = "clean";
}

public sealed class GoldenExpected
{
    public string? Error { get; init; }
    public List<GoldenLine>? Lines { get; init; }
    public Dictionary<LifecycleStage, decimal>? Stages { get; init; }
    public decimal? ProductTotal { get; init; }
    public List<string>? Warnings { get; init; }
    public Dictionary<string, int>? DataQuality { get; init; }
    public List<string>? ReadinessBlocking { get; init; }
}

public sealed record GoldenLine(string Activity, decimal CanonicalValue, decimal Emissions);
