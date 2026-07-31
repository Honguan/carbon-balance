using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Domain.Modules.Readiness;

public enum InventoryValidationSeverity
{
    BlockingError = 1,
    RequiredExplanation = 2,
    Warning = 3,
    Recommendation = 4
}

public sealed record InventoryValidationResult(
    string Code,
    InventoryValidationSeverity Severity,
    string EntityType,
    string EntityKey,
    LifecycleStage? Stage,
    string DataOwner,
    string Message,
    string Remediation,
    bool RequiresAcknowledgement = false);

public sealed record ReadinessFactorContext(
    Guid FactorVersionId,
    bool IsPublished,
    bool IsReviewed,
    bool IsGeographyCompatible,
    bool IsWithinValidityPeriod,
    bool IsUnitCompatible,
    bool IsWithdrawn = false);

public sealed record ReadinessEvidenceContext(
    Guid EvidenceId,
    bool IsRequired,
    bool HasVerifiedSha256,
    bool MalwareScanPassed,
    bool IsRetained);

public sealed record ReadinessAllocationContext(
    Guid PoolId,
    decimal ShareTotal,
    decimal Tolerance,
    bool HasCompleteBasis,
    bool HasEvidence);

public sealed record ReadinessActivityContext(
    Guid ActivityId,
    LifecycleStage Stage,
    string DataOwner,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Emissions,
    bool IsEstimated,
    string EstimationReason,
    bool IsExcluded,
    string ExclusionReason,
    ReadinessFactorContext? Factor,
    IReadOnlyList<ReadinessEvidenceContext> Evidence);

public sealed record InventoryReadinessContext(
    Guid ProjectVersionId,
    DateOnly EvaluationDate,
    DateOnly InventoryPeriodStart,
    DateOnly InventoryPeriodEnd,
    int MaximumDataAgeDays,
    bool PcrIsAvailable,
    bool PcrIsCompatible,
    IReadOnlySet<LifecycleStage> RequiredStages,
    IReadOnlyList<ReadinessActivityContext> Activities,
    IReadOnlyList<ReadinessAllocationContext> AllocationPools,
    decimal MaximumEstimatedEmissionShare,
    string Assumptions,
    string Exclusions,
    bool CalculationRunExists,
    bool CalculationManifestMatches,
    bool FormulaRuleSetIsPublished,
    IReadOnlySet<string> AcknowledgedRuleCodes);

public sealed class InventoryReadinessReport
{
    public InventoryReadinessReport(Guid projectVersionId, IReadOnlyList<InventoryValidationResult> results)
    {
        ProjectVersionId = projectVersionId;
        Results = results;
    }

    public Guid ProjectVersionId { get; }

    public IReadOnlyList<InventoryValidationResult> Results { get; }

    public bool IsReady => Results.All(result =>
        result.Severity != InventoryValidationSeverity.BlockingError
        && (!result.RequiresAcknowledgement || result.Severity == InventoryValidationSeverity.RequiredExplanation));

    public bool CanSubmit(IReadOnlySet<string> acknowledgements) =>
        Results.All(result =>
            result.Severity != InventoryValidationSeverity.BlockingError
            && (!result.RequiresAcknowledgement || acknowledgements.Contains(result.Code)));
}

public static class InventoryReadinessValidator
{
    public static InventoryReadinessReport Validate(InventoryReadinessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<InventoryValidationResult>();
        ValidateProject(context, results);
        ValidateStages(context, results);
        ValidateActivities(context, results);
        ValidateAllocations(context, results);
        ValidateEstimatedShare(context, results);
        ValidateCalculation(context, results);

        var ordered = results
            .OrderBy(result => result.Severity)
            .ThenBy(result => result.Stage)
            .ThenBy(result => result.DataOwner, StringComparer.Ordinal)
            .ThenBy(result => result.Code, StringComparer.Ordinal)
            .ThenBy(result => result.EntityKey, StringComparer.Ordinal)
            .ToArray();

        return new InventoryReadinessReport(context.ProjectVersionId, ordered);
    }

    private static void ValidateProject(
        InventoryReadinessContext context,
        ICollection<InventoryValidationResult> results)
    {
        var projectKey = context.ProjectVersionId.ToString("D");

        if (context.InventoryPeriodStart > context.InventoryPeriodEnd)
        {
            AddBlocking(results, "INV-PERIOD-ORDER", "InventoryProjectVersion", projectKey, null, string.Empty,
                "盤查期間起日不可晚於迄日。", "修正盤查期間。" );
        }

        if (!context.PcrIsAvailable)
        {
            AddBlocking(results, "INV-PCR-STATUS", "InventoryProjectVersion", projectKey, null, string.Empty,
                "PCR 未發布、已撤回、已過期或尚未生效。", "選擇目前有效且已核准的 PCR 版本。" );
        }

        if (!context.PcrIsCompatible)
        {
            AddBlocking(results, "INV-PCR-COMPATIBILITY", "InventoryProjectVersion", projectKey, null, string.Empty,
                "產品、功能單位、宣告單位或系統邊界不符合 PCR。", "依 PCR 修正盤查設定或選擇正確的 PCR。" );
        }

        if (string.IsNullOrWhiteSpace(context.Assumptions))
        {
            results.Add(new(
                "INV-ASSUMPTIONS",
                InventoryValidationSeverity.RequiredExplanation,
                "InventoryProjectVersion",
                projectKey,
                null,
                string.Empty,
                "尚未記錄盤查假設。",
                "記錄無假設或列出所有會影響結果的假設。",
                true));
        }

        if (string.IsNullOrWhiteSpace(context.Exclusions))
        {
            results.Add(new(
                "INV-EXCLUSIONS",
                InventoryValidationSeverity.RequiredExplanation,
                "InventoryProjectVersion",
                projectKey,
                null,
                string.Empty,
                "尚未記錄排除項目。",
                "明確記錄無排除，或說明排除項目與截斷依據。",
                true));
        }
    }

    private static void ValidateStages(
        InventoryReadinessContext context,
        ICollection<InventoryValidationResult> results)
    {
        foreach (var stage in context.RequiredStages.OrderBy(value => value))
        {
            if (context.Activities.Any(activity => activity.Stage == stage && !activity.IsExcluded))
            {
                continue;
            }

            AddBlocking(
                results,
                "INV-STAGE-REQUIRED",
                "LifecycleStage",
                stage.ToString(),
                stage,
                string.Empty,
                $"PCR 要求的 {stage} 階段沒有有效活動資料。",
                "新增該階段活動，或依 PCR 提供合法的不適用說明。" );
        }
    }

    private static void ValidateActivities(
        InventoryReadinessContext context,
        ICollection<InventoryValidationResult> results)
    {
        foreach (var activity in context.Activities.OrderBy(item => item.ActivityId))
        {
            var key = activity.ActivityId.ToString("D");
            var owner = activity.DataOwner ?? string.Empty;

            if (activity.PeriodStart > activity.PeriodEnd)
            {
                AddBlocking(results, "INV-ACTIVITY-PERIOD", "ActivityData", key, activity.Stage, owner,
                    "活動資料期間起日不可晚於迄日。", "修正活動資料期間。" );
            }

            var ageDays = context.EvaluationDate.DayNumber - activity.PeriodEnd.DayNumber;
            if (context.MaximumDataAgeDays >= 0 && ageDays > context.MaximumDataAgeDays)
            {
                results.Add(new(
                    "INV-DATA-AGE",
                    InventoryValidationSeverity.RequiredExplanation,
                    "ActivityData",
                    key,
                    activity.Stage,
                    owner,
                    $"活動資料已超過允許資料年齡 {context.MaximumDataAgeDays} 天。",
                    "更新資料，或記錄使用舊資料的理由與風險。",
                    true));
            }

            if (activity.IsEstimated && string.IsNullOrWhiteSpace(activity.EstimationReason))
            {
                AddBlocking(results, "INV-ESTIMATION-REASON", "ActivityData", key, activity.Stage, owner,
                    "估算資料缺少估算理由。", "補上估算方法、資料來源與使用理由。" );
            }

            if (activity.IsExcluded && string.IsNullOrWhiteSpace(activity.ExclusionReason))
            {
                AddBlocking(results, "INV-EXCLUSION-REASON", "ActivityData", key, activity.Stage, owner,
                    "排除活動缺少排除理由。", "補上 PCR 截斷依據與排除理由。" );
            }

            ValidateFactor(activity, results);
            ValidateEvidence(activity, results);
        }
    }

    private static void ValidateFactor(
        ReadinessActivityContext activity,
        ICollection<InventoryValidationResult> results)
    {
        var activityKey = activity.ActivityId.ToString("D");
        var owner = activity.DataOwner ?? string.Empty;

        if (activity.IsExcluded)
        {
            return;
        }

        if (activity.Factor is null)
        {
            AddBlocking(results, "INV-FACTOR-MISSING", "ActivityData", activityKey, activity.Stage, owner,
                "活動尚未選擇排放係數。", "選擇已發布且適用的排放係數版本。" );
            return;
        }

        var factor = activity.Factor;
        var factorKey = factor.FactorVersionId.ToString("D");

        if (!factor.IsPublished || !factor.IsReviewed || factor.IsWithdrawn)
        {
            AddBlocking(results, "INV-FACTOR-STATUS", "EmissionFactorVersion", factorKey, activity.Stage, owner,
                "排放係數未發布、未完成審查或已撤回。", "改用可用的已發布係數版本。" );
        }

        if (!factor.IsGeographyCompatible)
        {
            results.Add(new(
                "INV-FACTOR-GEOGRAPHY",
                InventoryValidationSeverity.RequiredExplanation,
                "EmissionFactorVersion",
                factorKey,
                activity.Stage,
                owner,
                "排放係數地域與活動資料不相容。",
                "選擇相容地域係數，或記錄代理係數理由。",
                true));
        }

        if (!factor.IsWithinValidityPeriod)
        {
            AddBlocking(results, "INV-FACTOR-VALIDITY", "EmissionFactorVersion", factorKey, activity.Stage, owner,
                "排放係數不涵蓋活動資料期間。", "選擇有效期間涵蓋活動資料的係數版本。" );
        }

        if (!factor.IsUnitCompatible)
        {
            AddBlocking(results, "INV-FACTOR-UNIT", "EmissionFactorVersion", factorKey, activity.Stage, owner,
                "活動量單位與係數分母單位不相容。", "修正單位或選擇維度相容的係數。" );
        }
    }

    private static void ValidateEvidence(
        ReadinessActivityContext activity,
        ICollection<InventoryValidationResult> results)
    {
        var owner = activity.DataOwner ?? string.Empty;
        var requiredEvidence = activity.Evidence.Where(item => item.IsRequired).ToArray();

        if (!activity.IsExcluded && requiredEvidence.Length == 0)
        {
            AddBlocking(results, "INV-EVIDENCE-MISSING", "ActivityData", activity.ActivityId.ToString("D"), activity.Stage, owner,
                "活動缺少必要佐證文件。", "上傳並連結發票、帳單、量測紀錄或其他必要佐證。" );
            return;
        }

        foreach (var evidence in requiredEvidence.OrderBy(item => item.EvidenceId))
        {
            var key = evidence.EvidenceId.ToString("D");
            if (!evidence.HasVerifiedSha256)
            {
                AddBlocking(results, "INV-EVIDENCE-HASH", "EvidenceDocumentVersion", key, activity.Stage, owner,
                    "佐證文件缺少伺服器驗證的 SHA-256。", "重新上傳原始文件並完成完整性驗證。" );
            }

            if (!evidence.MalwareScanPassed)
            {
                AddBlocking(results, "INV-EVIDENCE-SCAN", "EvidenceDocumentVersion", key, activity.Stage, owner,
                    "佐證文件未通過惡意程式掃描。", "隔離文件並上傳通過掃描的版本。" );
            }

            if (!evidence.IsRetained)
            {
                AddBlocking(results, "INV-EVIDENCE-RETENTION", "EvidenceDocumentVersion", key, activity.Stage, owner,
                    "佐證文件不符合保存政策。", "修正物件保存狀態或重新連結可保存版本。" );
            }
        }
    }

    private static void ValidateAllocations(
        InventoryReadinessContext context,
        ICollection<InventoryValidationResult> results)
    {
        foreach (var allocation in context.AllocationPools.OrderBy(item => item.PoolId))
        {
            var key = allocation.PoolId.ToString("D");
            var delta = Math.Abs(allocation.ShareTotal - 1m);
            if (delta > Math.Abs(allocation.Tolerance))
            {
                AddBlocking(results, "INV-ALLOCATION-TOTAL", "AllocationPoolVersion", key, null, string.Empty,
                    "分配池比例總和不是 100%。", "修正分配基礎，使比例在容許誤差內合計為 100%。" );
            }

            if (!allocation.HasCompleteBasis)
            {
                AddBlocking(results, "INV-ALLOCATION-BASIS", "AllocationPoolVersion", key, null, string.Empty,
                    "分配池缺少必要分配基礎。", "補齊分母、各產品基礎值、單位與公式。" );
            }

            if (!allocation.HasEvidence)
            {
                results.Add(new(
                    "INV-ALLOCATION-EVIDENCE",
                    InventoryValidationSeverity.RequiredExplanation,
                    "AllocationPoolVersion",
                    key,
                    null,
                    string.Empty,
                    "分配池缺少來源佐證。",
                    "連結產量、價格、工時或量測資料的佐證文件。",
                    true));
            }
        }
    }

    private static void ValidateEstimatedShare(
        InventoryReadinessContext context,
        ICollection<InventoryValidationResult> results)
    {
        var included = context.Activities.Where(activity => !activity.IsExcluded).ToArray();
        var total = included.Sum(activity => Math.Max(0m, activity.Emissions));
        var estimated = included.Where(activity => activity.IsEstimated)
            .Sum(activity => Math.Max(0m, activity.Emissions));
        var share = total == 0m ? 0m : estimated / total;

        if (share > context.MaximumEstimatedEmissionShare)
        {
            results.Add(new(
                "INV-ESTIMATED-SHARE",
                InventoryValidationSeverity.RequiredExplanation,
                "InventoryProjectVersion",
                context.ProjectVersionId.ToString("D"),
                null,
                string.Empty,
                $"估算資料占排放量 {share:P2}，高於允許門檻 {context.MaximumEstimatedEmissionShare:P2}。",
                "以量測或供應商資料取代主要估算值，或完成管理者核准說明。",
                true));
        }
    }

    private static void ValidateCalculation(
        InventoryReadinessContext context,
        ICollection<InventoryValidationResult> results)
    {
        var key = context.ProjectVersionId.ToString("D");
        if (!context.CalculationRunExists)
        {
            AddBlocking(results, "INV-CALCULATION-MISSING", "InventoryProjectVersion", key, null, string.Empty,
                "尚未建立計算執行結果。", "完成計算後再提交。" );
        }

        if (!context.CalculationManifestMatches)
        {
            AddBlocking(results, "INV-MANIFEST-STALE", "InventoryProjectVersion", key, null, string.Empty,
                "最新計算清單與目前輸入不一致。", "重新計算並確認輸入雜湊一致。" );
        }

        if (!context.FormulaRuleSetIsPublished)
        {
            AddBlocking(results, "INV-FORMULA-STATUS", "InventoryProjectVersion", key, null, string.Empty,
                "計算使用未發布或已撤回的公式規則版本。", "使用已發布公式重新計算。" );
        }
    }

    private static void AddBlocking(
        ICollection<InventoryValidationResult> results,
        string code,
        string entityType,
        string entityKey,
        LifecycleStage? stage,
        string owner,
        string message,
        string remediation) =>
        results.Add(new(
            code,
            InventoryValidationSeverity.BlockingError,
            entityType,
            entityKey,
            stage,
            owner,
            message,
            remediation));
}
