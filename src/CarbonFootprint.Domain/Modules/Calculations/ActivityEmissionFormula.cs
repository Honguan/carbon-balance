using CarbonFootprint.Domain.Modules.Inventories;
using System.Text.Json;

namespace CarbonFootprint.Domain.Modules.Calculations;

public sealed record ActivityEmissionFormulaDefinition(string Id);

public static class ActivityEmissionFormula
{
    public static ActivityEmissionFormulaDefinition Resolve(ActivityDataKind kind) => kind switch
    {
        ActivityDataKind.Material => new("material-input-times-factor-times-allocation-v1"),
        ActivityDataKind.MaterialTransport => new("material-transport-work-times-factor-times-allocation-v1"),
        ActivityDataKind.Energy => new("manufacturing-consumption-times-factor-times-allocation-v1"),
        ActivityDataKind.ManufacturingWaste => new("manufacturing-waste-times-factor-times-allocation-v1"),
        ActivityDataKind.OutsourcedTreatmentTransport => new("outsourced-transport-work-times-factor-times-allocation-v1"),
        ActivityDataKind.DistributionTransport => new("distribution-transport-work-times-factor-times-allocation-v1"),
        ActivityDataKind.UseEnergy => new("use-energy-consumption-times-factor-times-allocation-v1"),
        ActivityDataKind.UseConsumable => new("use-consumable-times-factor-times-allocation-v1"),
        ActivityDataKind.EndOfLifeTreatment => new("end-of-life-treatment-times-factor-times-allocation-v1"),
        ActivityDataKind.EndOfLifeTransport => new("end-of-life-transport-work-times-factor-times-allocation-v1"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支援的活動資料類型。")
    };

    public static decimal Calculate(decimal canonicalActivityValue, decimal factorValue, decimal allocationFactor) =>
        canonicalActivityValue * factorValue * allocationFactor;
}

public sealed record DerivedActivityAmount(
    decimal Value,
    string UnitCode,
    string FormulaId,
    IReadOnlyDictionary<string, decimal> Inputs,
    string FormulaTrace);

public static class ActivityAmountFormula
{
    public const string DirectFormulaId = "direct-activity-amount-v1";
    public const string TransportFormulaId = "transport-distance-times-weight-divided-by-1000-v1";
    public const string UseScenarioFormulaId = "use-lifetime-times-frequency-times-consumption-v1";

    public static bool IsTransport(ActivityDataKind kind) => kind is
        ActivityDataKind.MaterialTransport or
        ActivityDataKind.OutsourcedTreatmentTransport or
        ActivityDataKind.DistributionTransport or
        ActivityDataKind.EndOfLifeTransport;

    public static bool IsUseScenario(ActivityDataKind kind) => kind is
        ActivityDataKind.UseEnergy or ActivityDataKind.UseConsumable;

    public static DerivedActivityAmount Derive(
        ActivityDataKind kind,
        decimal? directValue,
        string directUnitCode,
        decimal? transportDistanceKm,
        decimal? transportWeightKg,
        decimal? useLifetime,
        decimal? useFrequency,
        decimal? useConsumptionPerUse)
    {
        if (IsTransport(kind))
        {
            if (transportDistanceKm is null or < 0m || transportWeightKg is null or < 0m)
            {
                throw new InvalidOperationException("運輸活動必須提供非負的運輸距離（km）與運輸重量（kg）。");
            }

            var tonneKilometres = transportDistanceKm.Value * transportWeightKg.Value / 1000m;
            return new DerivedActivityAmount(
                tonneKilometres,
                "tonne-km",
                TransportFormulaId,
                new Dictionary<string, decimal>
                {
                    ["distanceKm"] = transportDistanceKm.Value,
                    ["weightKg"] = transportWeightKg.Value
                },
                $"{transportDistanceKm.Value} km × {transportWeightKg.Value} kg ÷ 1000 = {tonneKilometres} tonne-km");
        }

        if (IsUseScenario(kind))
        {
            if (useLifetime is null or < 0m || useFrequency is null or < 0m || useConsumptionPerUse is null or < 0m)
            {
                throw new InvalidOperationException("使用階段必須提供非負的使用壽命、使用頻率與每次消耗量。");
            }

            var scenarioConsumption = useLifetime.Value * useFrequency.Value * useConsumptionPerUse.Value;
            return new DerivedActivityAmount(
                scenarioConsumption,
                directUnitCode,
                UseScenarioFormulaId,
                new Dictionary<string, decimal>
                {
                    ["lifetime"] = useLifetime.Value,
                    ["frequency"] = useFrequency.Value,
                    ["consumptionPerUse"] = useConsumptionPerUse.Value
                },
                $"{useLifetime.Value} × {useFrequency.Value} × {useConsumptionPerUse.Value} = {scenarioConsumption} {directUnitCode}");
        }

        if (directValue is null or < 0m)
        {
            throw new InvalidOperationException("活動量必須為非負值。");
        }

        return new DerivedActivityAmount(
            directValue.Value,
            directUnitCode,
            DirectFormulaId,
            new Dictionary<string, decimal> { ["value"] = directValue.Value },
            $"{directValue.Value} {directUnitCode}");
    }

    public static void ValidateDerived(
        ActivityDataKind kind,
        string formulaId,
        string formulaInputsJson,
        decimal derivedValue)
    {
        if (string.Equals(formulaId, DirectFormulaId, StringComparison.Ordinal))
        {
            return;
        }

        var expectedFormulaId = IsTransport(kind)
            ? TransportFormulaId
            : IsUseScenario(kind) ? UseScenarioFormulaId : DirectFormulaId;
        if (!string.Equals(formulaId, expectedFormulaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"活動類型 {kind} 不可使用活動量公式 {formulaId}。");
        }

        try
        {
            using var inputs = JsonDocument.Parse(formulaInputsJson);
            var root = inputs.RootElement;
            var expectedValue = formulaId switch
            {
                TransportFormulaId => ReadDecimal(root, "distanceKm") * ReadDecimal(root, "weightKg") / 1000m,
                UseScenarioFormulaId => ReadDecimal(root, "lifetime")
                    * ReadDecimal(root, "frequency")
                    * ReadDecimal(root, "consumptionPerUse"),
                _ => throw new InvalidOperationException($"未知的活動量公式版本：{formulaId}。")
            };
            if (expectedValue != derivedValue)
            {
                throw new InvalidOperationException("活動量公式輸入與保存的推導結果不一致。");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("活動量公式輸入不是有效的結構化資料。", exception);
        }
    }

    private static decimal ReadDecimal(JsonElement inputs, string name)
    {
        if (!inputs.TryGetProperty(name, out var value) || !value.TryGetDecimal(out var result) || result < 0m)
        {
            throw new InvalidOperationException($"活動量公式缺少有效的 {name} 輸入。");
        }

        return result;
    }
}
