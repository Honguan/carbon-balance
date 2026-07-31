using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarbonFootprint.Domain.Modules.Formulas;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Domain.Modules.Calculations;

public sealed class CalculationEngine
{
    private static readonly IActivityFormulaImplementation[] Implementations =
    [
        new DirectAmountFormula(),
        new FactorBasedFormula(),
        new MassBalanceFormula(),
        new EnergyBalanceFormula()
    ];

    public CalculationRun Calculate(
        Guid runId,
        InventoryProjectSnapshot snapshot,
        string engineBuild,
        Guid? supersedesRunId = null)
    {
        Validate(snapshot);
        var (manifest, hash) = CanonicalManifest.Create(snapshot, engineBuild);
        var formulaDefinitions = snapshot.Activities
            .Select(ResolveFormulaDefinition)
            .GroupBy(definition => definition.Id)
            .Select(group => group.First())
            .ToArray();
        var formulaRegistry = new ActivityFormulaRegistry(Implementations, formulaDefinitions);
        var lines = snapshot.Activities
            .OrderBy(activity => activity.Stage)
            .ThenBy(activity => activity.Id)
            .Select(activity => CalculateLine(activity, formulaRegistry, snapshot.RuleSetVersion))
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

    private static CalculationLineItem CalculateLine(
        ActivityDataSnapshot activity,
        ActivityFormulaRegistry registry,
        string ruleSetVersion)
    {
        var definition = ResolveFormulaDefinition(activity);
        var values = BuildFormulaValues(activity, definition);
        var result = registry.Execute(new FormulaExecutionContext(
            activity.Id,
            definition,
            values,
            new Dictionary<string, string>(StringComparer.Ordinal),
            DateTimeOffset.UnixEpoch));
        var governanceTrace = JsonSerializer.Serialize(new
        {
            dataQuality = JsonDocument.Parse(activity.DataQualityAssessmentJson).RootElement,
            allocation = JsonDocument.Parse(activity.AllocationTraceJson).RootElement,
            transport = JsonDocument.Parse(activity.TransportTraceJson).RootElement,
            evidence = JsonDocument.Parse(activity.EvidenceIndexJson).RootElement
        });

        var formulaId = activity.EmissionFormula is null
            ? ActivityEmissionFormula.Resolve(ruleSetVersion, activity.Kind).Id
            : $"{definition.Code}@{definition.VersionNumber}";

        return new CalculationLineItem(
            activity.Id,
            activity.Stage,
            formulaId,
            activity.CanonicalValue,
            activity.CanonicalUnitCode,
            activity.FactorVersion.Id,
            activity.FactorVersion.Value,
            $"{activity.FactorVersion.NumeratorUnitCode}/{activity.FactorVersion.DenominatorUnitCode}",
            result.Result,
            result.Unit,
            activity.AllocationFactor,
            activity.AmountFormulaId,
            activity.FormulaInputsJson,
            definition.Id,
            result.Trace,
            governanceTrace);
    }

    private static ActivityFormulaDefinitionVersion ResolveFormulaDefinition(ActivityDataSnapshot activity)
    {
        if (activity.EmissionFormula is not null)
        {
            return activity.EmissionFormula;
        }

        var factorUnit = $"{activity.FactorVersion.NumeratorUnitCode}/{activity.FactorVersion.DenominatorUnitCode}";
        var stableId = DeterministicGuid($"factor-based-v1|{activity.CanonicalUnitCode}|{factorUnit}");
        return new ActivityFormulaDefinitionVersion(
            stableId,
            DeterministicGuid("factor-based"),
            1,
            "factor-based-v1",
            ActivityCategory.OtherApproved,
            FormulaCalculationStrategy.FactorBased,
            FormulaPublicationStatus.Published,
            [
                new("activityAmount", "Activity amount", "activity", activity.CanonicalUnitCode, true, 0m, null),
                new("emissionFactor", "Emission factor", "emission-factor", factorUnit, true, 0m, null),
                new("allocationFactor", "Allocation factor", "ratio", "ratio", false, 0m, 1m)
            ],
            "emissions",
            activity.FactorVersion.NumeratorUnitCode,
            FactorBasedFormula.ImplementationIdentifier,
            DateTimeOffset.UnixEpoch,
            "system",
            DateTimeOffset.UnixEpoch);
    }

    private static IReadOnlyDictionary<string, FormulaValue> BuildFormulaValues(
        ActivityDataSnapshot activity,
        ActivityFormulaDefinitionVersion definition)
    {
        var factorUnit = $"{activity.FactorVersion.NumeratorUnitCode}/{activity.FactorVersion.DenominatorUnitCode}";
        var values = new Dictionary<string, FormulaValue>(StringComparer.Ordinal)
        {
            ["activityAmount"] = new(
                "activityAmount",
                activity.RawValue,
                activity.RawUnitCode,
                activity.CanonicalValue,
                activity.CanonicalUnitCode,
                activity.ConversionRuleVersion),
            ["emissionFactor"] = new(
                "emissionFactor",
                activity.FactorVersion.Value,
                factorUnit,
                activity.FactorVersion.Value,
                factorUnit,
                $"factor-version:{activity.FactorVersion.Id:D}"),
            ["allocationFactor"] = new(
                "allocationFactor",
                activity.AllocationFactor,
                "ratio",
                activity.AllocationFactor,
                "ratio",
                "allocation-result-v1"),
            ["amount"] = new(
                "amount",
                activity.RawValue,
                activity.RawUnitCode,
                activity.CanonicalValue,
                activity.CanonicalUnitCode,
                activity.ConversionRuleVersion)
        };

        using var document = JsonDocument.Parse(activity.EmissionFormulaValuesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Emission formula values must be a JSON object.");
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                var input = definition.Inputs.SingleOrDefault(item => string.Equals(item.Key, property.Name, StringComparison.Ordinal));
                var unit = input?.CanonicalUnit ?? string.Empty;
                values[property.Name] = new(
                    property.Name,
                    property.Value.GetDecimal(),
                    unit,
                    property.Value.GetDecimal(),
                    unit,
                    "identity-v1");
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Formula input '{property.Name}' must be a number or an object.");
            }

            var value = property.Value;
            var rawValue = RequiredDecimal(value, "rawValue");
            var normalizedValue = value.TryGetProperty("normalizedValue", out var normalizedElement)
                ? normalizedElement.GetDecimal()
                : rawValue;
            var inputDefinition = definition.Inputs.SingleOrDefault(item => string.Equals(item.Key, property.Name, StringComparison.Ordinal));
            var rawUnit = OptionalString(value, "rawUnit") ?? inputDefinition?.CanonicalUnit ?? string.Empty;
            var normalizedUnit = OptionalString(value, "normalizedUnit") ?? inputDefinition?.CanonicalUnit ?? rawUnit;
            values[property.Name] = new(
                property.Name,
                rawValue,
                rawUnit,
                normalizedValue,
                normalizedUnit,
                OptionalString(value, "conversionRuleVersion") ?? "identity-v1");
        }

        return values;
    }

    private static decimal RequiredDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetDecimal()
            : throw new InvalidOperationException($"Formula value object is missing numeric '{name}'.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
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

        if (snapshot.CutoffThresholdPercent is < 0m or > 100m)
        {
            throw new InvalidOperationException("PCR cutoff threshold must be between 0 and 100 percent.");
        }

        if (snapshot.RoundingDecimalPlaces is < 0 or > 12)
        {
            throw new InvalidOperationException("PCR reporting rounding must be between 0 and 12 decimal places.");
        }

        using (var governance = JsonDocument.Parse(snapshot.GovernanceSnapshotJson))
        {
            if (governance.RootElement.ValueKind is not JsonValueKind.Object)
            {
                throw new InvalidOperationException("Governance snapshot must be a JSON object.");
            }
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
            if (activity.EmissionFormula is null)
            {
                _ = ActivityEmissionFormula.Resolve(snapshot.RuleSetVersion, activity.Kind);
            }

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

            using var formulaValues = JsonDocument.Parse(activity.EmissionFormulaValuesJson);
            using var quality = JsonDocument.Parse(activity.DataQualityAssessmentJson);
            using var allocation = JsonDocument.Parse(activity.AllocationTraceJson);
            using var transport = JsonDocument.Parse(activity.TransportTraceJson);
            using var evidence = JsonDocument.Parse(activity.EvidenceIndexJson);
        }
    }
}
