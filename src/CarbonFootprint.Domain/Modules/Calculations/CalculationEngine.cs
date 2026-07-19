using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Domain.Modules.Calculations;

public sealed class CalculationEngine
{
    public CalculationRun Calculate(
        Guid runId,
        InventoryProjectSnapshot snapshot,
        string engineBuild,
        Guid? supersedesRunId = null)
    {
        Validate(snapshot);
        var (manifest, hash) = CanonicalManifest.Create(snapshot, engineBuild);
        var lines = snapshot.Activities
            .OrderBy(activity => activity.Stage)
            .ThenBy(activity => activity.Id)
            .Select(activity =>
            {
                var formula = ActivityEmissionFormula.Resolve(activity.Kind);
                return new CalculationLineItem(
                    activity.Id,
                    activity.Stage,
                    formula.Id,
                    activity.CanonicalValue,
                    activity.CanonicalUnitCode,
                    activity.FactorVersion.Id,
                    activity.FactorVersion.Value,
                    $"{activity.FactorVersion.NumeratorUnitCode}/{activity.FactorVersion.DenominatorUnitCode}",
                    ActivityEmissionFormula.Calculate(
                        activity.CanonicalValue,
                        activity.FactorVersion.Value,
                        activity.AllocationFactor),
                    activity.FactorVersion.NumeratorUnitCode,
                    activity.AllocationFactor,
                    activity.AmountFormulaId,
                    activity.FormulaInputsJson);
            })
            .ToArray();

        var summaries = Enum.GetValues<LifecycleStage>()
            .Select(stage => new CalculationStageSummary(
                stage,
                lines.Where(line => line.Stage == stage).Sum(line => line.Emissions)))
            .ToArray();

        var warnings = snapshot.Stages
            .Where(stage => !stage.IsApplicable)
            .Select(stage => new CalculationWarning(
                "STAGE_NOT_APPLICABLE",
                $"{stage.Stage} 不適用：{stage.Reason}"))
            .Concat(snapshot.Activities
                .Where(activity => activity.IsEstimated)
                .Select(activity => new CalculationWarning(
                    "ESTIMATED_ACTIVITY_DATA",
                    $"{activity.Name} 使用估算資料：{activity.EstimationReason}")))
            .Concat(string.IsNullOrWhiteSpace(snapshot.Exclusions)
                ? []
                : [new CalculationWarning("INVENTORY_EXCLUSIONS", snapshot.Exclusions)])
            .Concat(string.IsNullOrWhiteSpace(snapshot.Assumptions)
                ? []
                : [new CalculationWarning("INVENTORY_ASSUMPTIONS", snapshot.Assumptions)])
            .Concat(string.IsNullOrWhiteSpace(snapshot.EstimationReason)
                ? []
                : [new CalculationWarning("INVENTORY_ESTIMATION_REASON", snapshot.EstimationReason)])
            .ToArray();

        var dataQualitySummary = snapshot.Activities
            .GroupBy(activity => activity.DataQuality, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new CalculationRun(
            runId,
            snapshot.OrganizationId,
            snapshot.ProjectVersionId,
            supersedesRunId,
            manifest,
            hash,
            engineBuild,
            snapshot.RuleSetVersion,
            snapshot.UnitCatalogueVersion,
            snapshot.GwpVersion,
            snapshot.PcrVersion,
            lines,
            summaries,
            warnings,
            dataQualitySummary);
    }

    private static void Validate(InventoryProjectSnapshot snapshot)
    {
        if (snapshot.OrganizationId == Guid.Empty || snapshot.ProjectVersionId == Guid.Empty)
        {
            throw new InvalidOperationException("盤查快照缺少穩定識別或組織所有權。");
        }

        if (snapshot.PeriodStart > snapshot.PeriodEnd)
        {
            throw new InvalidOperationException("盤查期間起日不可晚於迄日。");
        }

        var declarations = snapshot.Stages.GroupBy(stage => stage.Stage).ToDictionary(group => group.Key);
        foreach (var stage in Enum.GetValues<LifecycleStage>())
        {
            if (!declarations.TryGetValue(stage, out var declaration) || declaration.Count() != 1)
            {
                throw new InvalidOperationException($"生命週期階段 {stage} 必須且只能宣告一次。");
            }

            var item = declaration.Single();
            if (!item.IsApplicable && string.IsNullOrWhiteSpace(item.Reason))
            {
                throw new InvalidOperationException($"不適用階段 {stage} 必須提供理由。");
            }

            if (item.IsApplicable && !snapshot.Activities.Any(activity => activity.Stage == stage))
            {
                throw new InvalidOperationException($"適用階段 {stage} 至少需要一筆活動數據。");
            }
        }

        foreach (var activity in snapshot.Activities)
        {
            ActivityAmountFormula.ValidateDerived(
                activity.Kind,
                activity.AmountFormulaId,
                activity.FormulaInputsJson,
                activity.RawValue);

            if (activity.OrganizationId != snapshot.OrganizationId)
            {
                throw new InvalidOperationException("活動數據與盤查快照的組織不一致。");
            }

            if (activity.RawValue < 0m || activity.CanonicalValue < 0m)
            {
                throw new InvalidOperationException("P0 一般活動數據不得為負值；移除量需使用後續受控規則。");
            }

            if (!ActivityKindRules.IsAllowed(activity.Stage, activity.Kind))
            {
                throw new InvalidOperationException($"活動類型 {activity.Kind} 不適用於 {activity.Stage} 階段。");
            }

            if (activity.AllocationFactor <= 0m || activity.AllocationFactor > 1m)
            {
                throw new InvalidOperationException("分配比例必須大於 0 且小於或等於 1。");
            }

            if (activity.IsEstimated && string.IsNullOrWhiteSpace(activity.EstimationReason))
            {
                throw new InvalidOperationException("估算活動數據必須提供估算或替代資料理由。");
            }

            if (string.IsNullOrWhiteSpace(activity.DataQuality))
            {
                throw new InvalidOperationException("活動數據必須標示資料品質。");
            }

            if (activity.PeriodStart > activity.PeriodEnd
                || activity.PeriodStart < snapshot.PeriodStart
                || activity.PeriodEnd > snapshot.PeriodEnd)
            {
                throw new InvalidOperationException("活動數據期間必須落在盤查期間內。");
            }

            if (!activity.FactorVersion.IsSelectableOn(activity.PeriodEnd))
            {
                throw new InvalidOperationException($"係數版本 {activity.FactorVersion.Id} 未發布、已撤回或不在有效期。");
            }

            if (!string.Equals(
                    activity.CanonicalUnitCode,
                    activity.FactorVersion.DenominatorUnitCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("活動 canonical 單位與係數分母不一致。");
            }
        }
    }
}
