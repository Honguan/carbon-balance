using System.Security.Cryptography;
using System.Text;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Application.Readiness;

public enum ReadinessSeverity
{
    BlockingError = 0,
    RequiredExplanation = 1,
    Warning = 2,
    Recommendation = 3
}

/// <param name="Code">Stable rule code; never reused for a different rule.</param>
/// <param name="Stage">Lifecycle stage the finding belongs to, null for project-level findings.</param>
/// <param name="Owner">Data owner (activity data provider) to route remediation to; empty when project-level.</param>
public sealed record ReadinessFinding(
    string Code,
    ReadinessSeverity Severity,
    string EntityType,
    string EntityKey,
    LifecycleStage? Stage,
    string Owner,
    string Message,
    string Remediation);

public sealed record ReadinessEvidence(Guid ActivityId, string Sha256, string ScanStatus);

/// <summary>Why the latest immutable calculation run cannot back a submission; null issue means it is current.</summary>
public sealed record ReadinessCalculationIssue(string Code, string Message);

public sealed record ReadinessAcknowledgement(string Code, string EntityKey, string Justification);

public sealed record ReadinessOptions
{
    /// <summary>Engineering default pending domain review (see DECISIONS_NEEDED DEC-007); callers may override.</summary>
    public int MaxDataAgeMonths { get; init; } = 36;
    public decimal MaxEstimatedActivityRatio { get; init; } = 0.30m;
}

public sealed record InventoryReadinessInput(
    InventoryProjectSnapshot Snapshot,
    IReadOnlyList<PcrRuleViolation> PcrViolations,
    IReadOnlyList<ReadinessEvidence> Evidence,
    ReadinessCalculationIssue? CalculationIssue,
    DateOnly AsOf,
    IReadOnlyList<ReadinessAcknowledgement>? Acknowledgements = null,
    ReadinessOptions? Options = null);

public sealed record ReadinessReport(
    IReadOnlyList<ReadinessFinding> Findings,
    IReadOnlyList<ReadinessFinding> UnacknowledgedExplanations,
    DateOnly AsOf)
{
    public int BlockingCount => Findings.Count(item => item.Severity == ReadinessSeverity.BlockingError);

    public bool CanSubmit => BlockingCount == 0 && UnacknowledgedExplanations.Count == 0;

    /// <summary>Deterministic fingerprint of the findings, recorded in the audit trail at submission.</summary>
    public string Sha256()
    {
        var text = string.Join("\n", Findings.Select(item =>
            $"{item.Code}|{item.Severity}|{item.EntityType}|{item.EntityKey}|{item.Stage}|{item.Message}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>Pure, deterministic pre-submission checks over an immutable project snapshot.</summary>
public static class InventoryReadinessValidator
{
    public static ReadinessReport Validate(InventoryReadinessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var snapshot = input.Snapshot;
        var options = input.Options ?? new ReadinessOptions();
        var findings = new List<ReadinessFinding>();
        var projectKey = snapshot.ProjectVersionId.ToString();

        void Add(string code, ReadinessSeverity severity, string entityType, string entityKey, LifecycleStage? stage,
            string owner, string message, string remediation) =>
            findings.Add(new ReadinessFinding(code, severity, entityType, entityKey, stage, owner, message, remediation));

        foreach (var violation in input.PcrViolations)
        {
            Add(violation.Code, ReadinessSeverity.BlockingError, violation.EntityType, violation.EntityKey,
                Enum.TryParse<LifecycleStage>(violation.EntityKey, out var violationStage) ? violationStage : null,
                string.Empty, violation.Message, "調整盤查資料以符合所選 PCR，或改選適用且有效的 PCR 版本。");
        }

        if (input.CalculationIssue is { } calculation)
        {
            Add(calculation.Code, ReadinessSeverity.BlockingError, "CalculationRun", projectKey, null, string.Empty,
                calculation.Message, "重新執行計算以建立與目前輸入一致的不可變計算版本。");
        }

        if (snapshot.PeriodStart > snapshot.PeriodEnd)
        {
            Add("RDY-PERIOD-INVALID", ReadinessSeverity.BlockingError, "InventoryProjectVersion", projectKey, null,
                string.Empty, "盤查期間起日晚於迄日。", "更正盤查期間。");
        }

        foreach (var stage in Enum.GetValues<LifecycleStage>())
        {
            var declarations = snapshot.Stages.Where(item => item.Stage == stage).ToArray();
            var stageKey = stage.ToString();
            if (declarations.Length != 1)
            {
                Add("RDY-STAGE-DECLARATION", ReadinessSeverity.BlockingError, "LifecycleStage", stageKey, stage,
                    string.Empty, $"生命週期階段 {stage} 必須且只能宣告一次。", "補齊階段適用性宣告。");
                continue;
            }
            var declaration = declarations[0];
            var hasActivities = snapshot.Activities.Any(item => item.Stage == stage);
            if (!declaration.IsApplicable)
            {
                if (string.IsNullOrWhiteSpace(declaration.Reason))
                {
                    Add("RDY-STAGE-REASON", ReadinessSeverity.BlockingError, "LifecycleStage", stageKey, stage,
                        string.Empty, $"不適用階段 {stage} 沒有填寫原因。", "填寫不適用原因。");
                }
                else
                {
                    Add("RDY-STAGE-EXCLUDED", ReadinessSeverity.Warning, "LifecycleStage", stageKey, stage,
                        string.Empty, $"階段 {stage} 已標示不適用：{declaration.Reason}", "確認排除理由可被查驗者接受。");
                }
            }
            else if (!hasActivities)
            {
                Add("RDY-STAGE-ACTIVITY", ReadinessSeverity.BlockingError, "LifecycleStage", stageKey, stage,
                    string.Empty, $"適用階段 {stage} 沒有任何活動數據。", "新增活動數據，或標示階段不適用並說明原因。");
            }
        }

        var evidenceByActivity = input.Evidence.ToLookup(item => item.ActivityId);
        foreach (var activity in snapshot.Activities.OrderBy(item => item.Stage).ThenBy(item => item.Id))
        {
            var key = activity.Id.ToString();
            void AddActivity(string code, ReadinessSeverity severity, string message, string remediation) =>
                Add(code, severity, "ActivityData", key, activity.Stage, activity.DataProvider, message, remediation);

            if (!ActivityKindRules.IsAllowed(activity.Stage, activity.Kind))
                AddActivity("RDY-ACTIVITY-KIND", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」的類型 {activity.Kind} 不適用於 {activity.Stage} 階段。", "改用該階段允許的活動類型。");
            if (activity.RawValue < 0m || activity.CanonicalValue < 0m)
                AddActivity("RDY-ACTIVITY-NEGATIVE", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」為負值。", "一般活動數據不得為負值。");
            if (activity.PeriodStart > activity.PeriodEnd || activity.PeriodStart < snapshot.PeriodStart || activity.PeriodEnd > snapshot.PeriodEnd)
                AddActivity("RDY-ACTIVITY-PERIOD", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」期間不在盤查期間內。", "更正活動期間。");
            if (activity.PeriodEnd < input.AsOf.AddMonths(-options.MaxDataAgeMonths))
                AddActivity("RDY-ACTIVITY-AGE", ReadinessSeverity.RequiredExplanation,
                    $"活動「{activity.Name}」資料已超過 {options.MaxDataAgeMonths} 個月。", "說明為何此資料仍具代表性。");
            if (string.IsNullOrWhiteSpace(activity.DataQuality))
                AddActivity("RDY-DATA-QUALITY", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」未標示資料品質。", "標示資料品質。");

            var factor = activity.FactorVersion;
            if (factor.Status != Domain.Modules.Factors.FactorPublicationStatus.Published
                || factor.ReviewStatus is not (Domain.Modules.Factors.FactorReviewStatus.Approved or Domain.Modules.Factors.FactorReviewStatus.NotRequired)
                || string.IsNullOrWhiteSpace(factor.Applicability))
                AddActivity("RDY-FACTOR-STATUS", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」引用的係數未發布、已撤回、未通過審查或缺少適用性。", "改用已發布且已審查的係數版本。");
            else if ((factor.ValidFrom is { } from && from > activity.PeriodEnd) || (factor.ValidTo is { } to && to < activity.PeriodEnd))
                AddActivity("RDY-FACTOR-VALIDITY", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」引用的係數不在有效期內。", "改用涵蓋活動期間的係數版本。");
            if (!string.Equals(activity.CanonicalUnitCode, factor.DenominatorUnitCode, StringComparison.OrdinalIgnoreCase))
                AddActivity("RDY-FACTOR-UNIT", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」單位 {activity.CanonicalUnitCode} 與係數分母 {factor.DenominatorUnitCode} 不一致。", "選用單位相容的係數。");

            if (activity.AllocationFactor <= 0m || activity.AllocationFactor > 1m)
                AddActivity("RDY-ALLOCATION-RANGE", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」分配比例必須大於 0 且不超過 1。", "更正分配比例。");
            else if (activity.AllocationFactor < 1m && string.IsNullOrWhiteSpace(snapshot.AllocationReason))
                AddActivity("RDY-ALLOCATION-REASON", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」使用分配比例但盤查未說明分配依據。", "補充分配方法依據。");

            if (activity.IsEstimated && string.IsNullOrWhiteSpace(activity.EstimationReason))
                AddActivity("RDY-ESTIMATE-REASON", ReadinessSeverity.BlockingError,
                    $"估算活動「{activity.Name}」沒有估算理由。", "填寫估算或替代資料理由。");

            var evidence = evidenceByActivity[activity.Id].ToArray();
            if (string.IsNullOrWhiteSpace(activity.EvidenceSha256) || evidence.Length == 0)
            {
                AddActivity("RDY-EVIDENCE-MISSING", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」沒有佐證文件。", "上傳並附上佐證文件。");
            }
            else if (!evidence.Any(item => item.ScanStatus == "Clean"
                         && SourceDocumentIntegrity.TryNormalizeSha256(item.Sha256, out var hash)
                         && string.Equals(hash, activity.EvidenceSha256, StringComparison.OrdinalIgnoreCase)))
            {
                AddActivity("RDY-EVIDENCE-INTEGRITY", ReadinessSeverity.BlockingError,
                    $"活動「{activity.Name}」的佐證未通過掃描或雜湊與活動記錄不符。", "重新上傳佐證，待掃描通過後附上。");
            }
        }

        if (snapshot.Activities.Count > 0)
        {
            var ratio = (decimal)snapshot.Activities.Count(item => item.IsEstimated) / snapshot.Activities.Count;
            if (ratio > options.MaxEstimatedActivityRatio)
                Add("RDY-ESTIMATE-RATIO", ReadinessSeverity.RequiredExplanation, "InventoryProjectVersion", projectKey, null,
                    string.Empty, $"估算活動占 {ratio:P0}，超過上限 {options.MaxEstimatedActivityRatio:P0}。", "說明估算資料占比偏高的原因與改善計畫。");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Exclusions))
            Add("RDY-EXCLUSIONS-BLANK", ReadinessSeverity.Recommendation, "InventoryProjectVersion", projectKey, null,
                string.Empty, "盤查未記錄排除項目。", "記錄排除項目，或明確聲明無排除。");
        if (string.IsNullOrWhiteSpace(snapshot.Assumptions))
            Add("RDY-ASSUMPTIONS-BLANK", ReadinessSeverity.Recommendation, "InventoryProjectVersion", projectKey, null,
                string.Empty, "盤查未記錄假設。", "記錄計算與資料採用的假設。");

        var ordered = findings
            .OrderBy(item => item.Severity)
            .ThenBy(item => item.Stage is null ? int.MaxValue : (int)item.Stage)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.EntityKey, StringComparer.Ordinal)
            .ToArray();
        var acknowledgements = (input.Acknowledgements ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Justification))
            .Select(item => (item.Code, item.EntityKey))
            .ToHashSet();
        var unacknowledged = ordered
            .Where(item => item.Severity == ReadinessSeverity.RequiredExplanation && !acknowledgements.Contains((item.Code, item.EntityKey)))
            .ToArray();
        return new ReadinessReport(ordered, unacknowledged, input.AsOf);
    }
}
