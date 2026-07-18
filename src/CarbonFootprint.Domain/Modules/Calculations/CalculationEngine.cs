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
            .Select(activity => new CalculationLineItem(
                activity.Id,
                activity.Stage,
                "activity-times-factor-v1",
                activity.CanonicalValue,
                activity.CanonicalUnitCode,
                activity.FactorVersion.Id,
                activity.FactorVersion.Value,
                $"{activity.FactorVersion.NumeratorUnitCode}/{activity.FactorVersion.DenominatorUnitCode}",
                activity.CanonicalValue * activity.FactorVersion.Value,
                activity.FactorVersion.NumeratorUnitCode))
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
            .ToArray();

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
            warnings);
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
            if (activity.OrganizationId != snapshot.OrganizationId)
            {
                throw new InvalidOperationException("活動數據與盤查快照的組織不一致。");
            }

            if (activity.RawValue < 0m || activity.CanonicalValue < 0m)
            {
                throw new InvalidOperationException("P0 一般活動數據不得為負值；移除量需使用後續受控規則。");
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

