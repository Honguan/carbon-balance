using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Domain.Modules.Standards;

public enum PcrStageRequirement
{
    Optional,
    Mandatory,
    Prohibited
}

public enum PcrCustomApprovalStatus
{
    NotRequired,
    Pending,
    Approved,
    Rejected
}

public sealed record PcrLifecycleStageRule(
    LifecycleStage Stage,
    PcrStageRequirement Requirement,
    IReadOnlySet<ActivityDataKind> PermittedActivityKinds,
    IReadOnlySet<string> RequiredFields);

public sealed record PcrRuleSetVersion(
    Guid Id,
    Guid RuleSetId,
    string RegistrationNumber,
    int VersionNumber,
    string ProductCategoryPatterns,
    DateOnly? ApprovalDate,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    PcrPublicationStatus PublicationStatus,
    PcrReviewStatus ReviewStatus,
    string FunctionalUnitPattern,
    string DeclaredUnitCode,
    string SystemBoundaryCode,
    IReadOnlySet<string> PermittedAllocationMethods,
    decimal CutoffThresholdPercent,
    string FormulaRuleSetVersion,
    int RoundingDecimalPlaces,
    string ReportingRequirements,
    bool IsCustomRule,
    string CustomRuleJustification,
    PcrCustomApprovalStatus CustomApprovalStatus,
    DateTimeOffset? DeprecatedAt,
    Guid? SupersedesVersionId,
    IReadOnlyList<PcrLifecycleStageRule> StageRules)
{
    public bool IsPublishedAndApproved =>
        PublicationStatus == PcrPublicationStatus.Published
        && ReviewStatus == PcrReviewStatus.Approved
        && (!IsCustomRule || CustomApprovalStatus == PcrCustomApprovalStatus.Approved)
        && DeprecatedAt is null;

    public bool IsAvailableOn(DateOnly date) =>
        IsPublishedAndApproved
        && (EffectiveDate is null || EffectiveDate <= date)
        && (ExpiryDate is null || ExpiryDate >= date);
}

public sealed record PcrActivityContext(
    Guid ActivityId,
    LifecycleStage Stage,
    ActivityDataKind ActivityKind,
    IReadOnlySet<string> PopulatedFields);

public sealed record PcrProjectContext(
    Guid ProjectVersionId,
    string ProductCategoryCode,
    DateOnly InventoryEndDate,
    string FunctionalUnit,
    string DeclaredUnitCode,
    string SystemBoundaryCode,
    string AllocationMethod,
    IReadOnlyDictionary<LifecycleStage, bool> StageApplicability,
    IReadOnlyList<PcrActivityContext> Activities,
    string Exclusions = "");

public sealed record PcrRuleViolation(
    string Code,
    string EntityType,
    string EntityKey,
    string Message);

public static class PcrRuleEngine
{
    public static IReadOnlyList<PcrRuleViolation> Validate(
        PcrRuleSetVersion ruleSet,
        PcrProjectContext project,
        bool requireCompleteInventory)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(project);

        var violations = new List<PcrRuleViolation>();
        var projectKey = project.ProjectVersionId.ToString();

        if (!ruleSet.IsAvailableOn(project.InventoryEndDate))
        {
            violations.Add(new(
                "PCR-STATUS",
                "InventoryProjectVersion",
                projectKey,
                "PCR 規則版本未發布、未核准、尚未生效、已過期或已撤回。"));
        }

        if (!MatchesPatterns(project.ProductCategoryCode, ruleSet.ProductCategoryPatterns))
        {
            violations.Add(new(
                "PCR-PRODUCT-SCOPE",
                "Product",
                project.ProductCategoryCode,
                "產品分類不在 PCR 適用範圍內。"));
        }

        if (!MatchesPatterns(project.FunctionalUnit, ruleSet.FunctionalUnitPattern))
        {
            violations.Add(new(
                "PCR-FUNCTIONAL-UNIT",
                "InventoryProjectVersion",
                projectKey,
                "功能單位不符合 PCR 規則。"));
        }

        if (!MatchesControlledValue(project.DeclaredUnitCode, ruleSet.DeclaredUnitCode))
        {
            violations.Add(new(
                "PCR-DECLARED-UNIT",
                "InventoryProjectVersion",
                projectKey,
                "宣告單位不符合 PCR 規則。"));
        }

        if (!MatchesControlledValue(project.SystemBoundaryCode, ruleSet.SystemBoundaryCode))
        {
            violations.Add(new(
                "PCR-SYSTEM-BOUNDARY",
                "InventoryProjectVersion",
                projectKey,
                "系統邊界不符合 PCR 規則。"));
        }

        if (ruleSet.PermittedAllocationMethods.Count > 0
            && !ruleSet.PermittedAllocationMethods.Contains(project.AllocationMethod, StringComparer.OrdinalIgnoreCase))
        {
            violations.Add(new(
                "PCR-ALLOCATION",
                "InventoryProjectVersion",
                projectKey,
                "分配方法不在 PCR 允許清單內。"));
        }

        if (ruleSet.CutoffThresholdPercent > 0m && string.IsNullOrWhiteSpace(project.Exclusions))
        {
            violations.Add(new(
                "PCR-CUTOFF-DISCLOSURE",
                "InventoryProjectVersion",
                projectKey,
                "PCR 設有截斷門檻，盤查必須明確記錄排除項目或聲明無排除。"));
        }

        foreach (var stageRule in ruleSet.StageRules.OrderBy(item => item.Stage))
        {
            var isApplicable = project.StageApplicability.GetValueOrDefault(stageRule.Stage);
            var stageKey = stageRule.Stage.ToString();
            if (stageRule.Requirement == PcrStageRequirement.Mandatory && !isApplicable)
            {
                violations.Add(new(
                    "PCR-STAGE-REQUIRED",
                    "LifecycleStage",
                    stageKey,
                    $"PCR 要求「{stageKey}」必須納入盤查。"));
                continue;
            }

            if (stageRule.Requirement == PcrStageRequirement.Prohibited && isApplicable)
            {
                violations.Add(new(
                    "PCR-STAGE-PROHIBITED",
                    "LifecycleStage",
                    stageKey,
                    $"PCR 不允許「{stageKey}」納入此盤查。"));
            }

            var stageActivities = project.Activities
                .Where(item => item.Stage == stageRule.Stage)
                .OrderBy(item => item.ActivityId)
                .ToArray();
            if (requireCompleteInventory
                && stageRule.Requirement == PcrStageRequirement.Mandatory
                && stageActivities.Length == 0)
            {
                violations.Add(new(
                    "PCR-STAGE-ACTIVITY-REQUIRED",
                    "LifecycleStage",
                    stageKey,
                    $"PCR 要求「{stageKey}」至少有一筆活動數據。"));
            }

            foreach (var activity in stageActivities)
            {
                if (stageRule.PermittedActivityKinds.Count > 0
                    && !stageRule.PermittedActivityKinds.Contains(activity.ActivityKind))
                {
                    violations.Add(new(
                        "PCR-ACTIVITY-TYPE",
                        "ActivityData",
                        activity.ActivityId.ToString(),
                        $"活動類型 {activity.ActivityKind} 不符合此階段的 PCR 規則。"));
                }

                foreach (var requiredField in stageRule.RequiredFields.Order(StringComparer.Ordinal))
                {
                    if (!activity.PopulatedFields.Contains(requiredField))
                    {
                        violations.Add(new(
                            "PCR-ACTIVITY-FIELD",
                            "ActivityData",
                            activity.ActivityId.ToString(),
                            $"PCR 要求活動數據填寫欄位「{requiredField}」。"));
                    }
                }
            }
        }

        return violations;
    }

    private static bool MatchesPatterns(string value, string patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns) || patterns.Trim() == "*")
        {
            return true;
        }

        var normalizedValue = value.Trim();
        return patterns
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(pattern =>
                pattern == "*"
                || (pattern.EndsWith('*')
                    ? normalizedValue.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
                    : string.Equals(normalizedValue, pattern, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool MatchesControlledValue(string value, string expected) =>
        string.IsNullOrWhiteSpace(expected)
        || expected.Trim() == "*"
        || string.Equals(value.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
