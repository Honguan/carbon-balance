namespace CarbonFootprint.Domain.Modules.Formulas;

public enum FormulaPublicationStatus
{
    Draft = 1,
    Published = 2,
    Withdrawn = 3,
    Superseded = 4
}

public enum FormulaCalculationStrategy
{
    DirectAmount = 1,
    DerivedAmount = 2,
    FactorBased = 3,
    MassBalance = 4,
    EnergyBalance = 5,
    PcrSpecific = 6
}

public enum ActivityCategory
{
    Material = 1,
    StationaryCombustion = 2,
    MobileCombustion = 3,
    ProcessEmission = 4,
    RefrigerantLeakage = 5,
    PurchasedElectricity = 6,
    SteamHeatCooling = 7,
    Water = 8,
    Wastewater = 9,
    Packaging = 10,
    Warehousing = 11,
    RenewableEnergy = 12,
    BiogenicCarbon = 13,
    Transport = 14,
    EndOfLife = 15,
    OtherApproved = 99
}

public sealed record FormulaInputDefinition(
    string Key,
    string DisplayName,
    string Dimension,
    string CanonicalUnit,
    bool IsRequired,
    decimal? Minimum,
    decimal? Maximum);

public sealed record ActivityFormulaDefinitionVersion(
    Guid Id,
    Guid FormulaId,
    int VersionNumber,
    string Code,
    ActivityCategory Category,
    FormulaCalculationStrategy Strategy,
    FormulaPublicationStatus Status,
    IReadOnlyList<FormulaInputDefinition> Inputs,
    string OutputDimension,
    string OutputUnit,
    string ImplementationKey,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? PublishedAt,
    Guid? SupersedesVersionId = null)
{
    public bool IsSelectable => Status == FormulaPublicationStatus.Published;
}

public sealed record FormulaValue(
    string Key,
    decimal RawValue,
    string RawUnit,
    decimal NormalizedValue,
    string NormalizedUnit,
    string ConversionRuleVersion);

public sealed record FormulaExecutionContext(
    Guid ActivityId,
    ActivityFormulaDefinitionVersion Definition,
    IReadOnlyDictionary<string, FormulaValue> Values,
    IReadOnlyDictionary<string, string> TextValues,
    DateTimeOffset ExecutedAt);

public sealed record FormulaIntermediateValue(
    string Key,
    decimal Value,
    string Unit,
    string Description);

public sealed record FormulaExecutionResult(
    Guid ActivityId,
    Guid FormulaVersionId,
    string FormulaCode,
    decimal Result,
    string Unit,
    IReadOnlyList<FormulaIntermediateValue> IntermediateValues,
    IReadOnlyList<FormulaValue> NormalizedInputs,
    string Trace);

public sealed record FormulaValidationError(
    string Code,
    string InputKey,
    string Message);

public interface IActivityFormulaImplementation
{
    string ImplementationKey { get; }

    FormulaExecutionResult Execute(FormulaExecutionContext context);
}

public sealed class ActivityFormulaRegistry
{
    private readonly IReadOnlyDictionary<string, IActivityFormulaImplementation> _implementations;
    private readonly IReadOnlyDictionary<Guid, ActivityFormulaDefinitionVersion> _definitions;

    public ActivityFormulaRegistry(
        IEnumerable<IActivityFormulaImplementation> implementations,
        IEnumerable<ActivityFormulaDefinitionVersion> definitions)
    {
        ArgumentNullException.ThrowIfNull(implementations);
        ArgumentNullException.ThrowIfNull(definitions);

        _implementations = implementations.ToDictionary(
            item => item.ImplementationKey,
            StringComparer.Ordinal);
        _definitions = definitions.ToDictionary(item => item.Id);
    }

    public IReadOnlyList<ActivityFormulaDefinitionVersion> SelectableFor(ActivityCategory category) =>
        _definitions.Values
            .Where(definition => definition.Category == category && definition.IsSelectable)
            .OrderBy(definition => definition.Code, StringComparer.Ordinal)
            .ThenByDescending(definition => definition.VersionNumber)
            .ToArray();

    public IReadOnlyList<FormulaValidationError> Validate(FormulaExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<FormulaValidationError>();
        var definition = context.Definition;

        if (!_definitions.TryGetValue(definition.Id, out var registered)
            || registered != definition)
        {
            errors.Add(new("FORMULA-NOT-REGISTERED", string.Empty, "Formula definition version is not registered."));
            return errors;
        }

        if (!definition.IsSelectable)
        {
            errors.Add(new("FORMULA-NOT-PUBLISHED", string.Empty, "Formula definition version is not published."));
        }

        if (!_implementations.ContainsKey(definition.ImplementationKey))
        {
            errors.Add(new("FORMULA-IMPLEMENTATION", string.Empty, "Formula implementation is not registered."));
        }

        foreach (var input in definition.Inputs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!context.Values.TryGetValue(input.Key, out var value))
            {
                if (input.IsRequired)
                {
                    errors.Add(new("FORMULA-INPUT-REQUIRED", input.Key, $"Required input '{input.DisplayName}' is missing."));
                }

                continue;
            }

            if (!string.Equals(value.NormalizedUnit, input.CanonicalUnit, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new("FORMULA-INPUT-UNIT", input.Key, $"Input '{input.DisplayName}' must normalize to {input.CanonicalUnit}."));
            }

            if (input.Minimum is not null && value.NormalizedValue < input.Minimum)
            {
                errors.Add(new("FORMULA-INPUT-MIN", input.Key, $"Input '{input.DisplayName}' is below the allowed minimum."));
            }

            if (input.Maximum is not null && value.NormalizedValue > input.Maximum)
            {
                errors.Add(new("FORMULA-INPUT-MAX", input.Key, $"Input '{input.DisplayName}' exceeds the allowed maximum."));
            }
        }

        return errors;
    }

    public FormulaExecutionResult Execute(FormulaExecutionContext context)
    {
        var errors = Validate(context);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors.Select(error => $"{error.Code}:{error.InputKey}:{error.Message}")));
        }

        return _implementations[context.Definition.ImplementationKey].Execute(context);
    }
}

public sealed class DirectAmountFormula : IActivityFormulaImplementation
{
    public const string ImplementationIdentifier = "direct-amount-v1";

    public string ImplementationKey => ImplementationIdentifier;

    public FormulaExecutionResult Execute(FormulaExecutionContext context)
    {
        var amount = Required(context, "amount");
        return CreateResult(
            context,
            amount.NormalizedValue,
            context.Definition.OutputUnit,
            [new("amount", amount.NormalizedValue, amount.NormalizedUnit, "Direct normalized amount")]);
    }

    private static FormulaValue Required(FormulaExecutionContext context, string key) =>
        context.Values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Missing required input: {key}.");

    internal static FormulaExecutionResult CreateResult(
        FormulaExecutionContext context,
        decimal result,
        string unit,
        IReadOnlyList<FormulaIntermediateValue> intermediateValues)
    {
        var orderedInputs = context.Values.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var traceParts = new List<string>
        {
            $"activity={context.ActivityId:D}",
            $"formula={context.Definition.Code}@{context.Definition.VersionNumber}",
            $"implementation={context.Definition.ImplementationKey}"
        };
        traceParts.AddRange(orderedInputs.Select(input =>
            $"input={input.Key},{input.RawValue:G29},{input.RawUnit},{input.NormalizedValue:G29},{input.NormalizedUnit},{input.ConversionRuleVersion}"));
        traceParts.AddRange(intermediateValues.Select(item =>
            $"intermediate={item.Key},{item.Value:G29},{item.Unit}"));
        traceParts.Add($"result={result:G29},{unit}");

        return new(
            context.ActivityId,
            context.Definition.Id,
            context.Definition.Code,
            result,
            unit,
            intermediateValues,
            orderedInputs,
            string.Join("|", traceParts));
    }
}

public sealed class FactorBasedFormula : IActivityFormulaImplementation
{
    public const string ImplementationIdentifier = "factor-based-v1";

    public string ImplementationKey => ImplementationIdentifier;

    public FormulaExecutionResult Execute(FormulaExecutionContext context)
    {
        var amount = Required(context, "activityAmount");
        var factor = Required(context, "emissionFactor");
        var allocation = context.Values.TryGetValue("allocationFactor", out var allocationValue)
            ? allocationValue.NormalizedValue
            : 1m;
        var result = amount.NormalizedValue * factor.NormalizedValue * allocation;

        return DirectAmountFormula.CreateResult(
            context,
            result,
            context.Definition.OutputUnit,
            [
                new("normalizedActivityAmount", amount.NormalizedValue, amount.NormalizedUnit, "Normalized activity amount"),
                new("emissionFactor", factor.NormalizedValue, factor.NormalizedUnit, "Selected immutable factor version value"),
                new("allocationFactor", allocation, "ratio", "Approved allocation share"),
                new("emissions", result, context.Definition.OutputUnit, "activity amount × emission factor × allocation")
            ]);
    }

    private static FormulaValue Required(FormulaExecutionContext context, string key) =>
        context.Values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Missing required input: {key}.");
}

public sealed class MassBalanceFormula : IActivityFormulaImplementation
{
    public const string ImplementationIdentifier = "mass-balance-v1";

    public string ImplementationKey => ImplementationIdentifier;

    public FormulaExecutionResult Execute(FormulaExecutionContext context)
    {
        var inputMass = Required(context, "inputMass");
        var outputMass = Required(context, "outputMass");
        var carbonFraction = Required(context, "carbonFraction");
        var oxidationFactor = context.Values.TryGetValue("oxidationFactor", out var oxidation)
            ? oxidation.NormalizedValue
            : 1m;
        var netMass = inputMass.NormalizedValue - outputMass.NormalizedValue;
        var result = netMass * carbonFraction.NormalizedValue * oxidationFactor * (44m / 12m);

        return DirectAmountFormula.CreateResult(
            context,
            result,
            context.Definition.OutputUnit,
            [
                new("netMass", netMass, inputMass.NormalizedUnit, "Input mass minus output mass"),
                new("carbonFraction", carbonFraction.NormalizedValue, "ratio", "Carbon fraction"),
                new("oxidationFactor", oxidationFactor, "ratio", "Oxidation factor"),
                new("co2", result, context.Definition.OutputUnit, "Net carbon converted to CO2 using molecular mass ratio")
            ]);
    }

    private static FormulaValue Required(FormulaExecutionContext context, string key) =>
        context.Values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Missing required input: {key}.");
}

public sealed class EnergyBalanceFormula : IActivityFormulaImplementation
{
    public const string ImplementationIdentifier = "energy-balance-v1";

    public string ImplementationKey => ImplementationIdentifier;

    public FormulaExecutionResult Execute(FormulaExecutionContext context)
    {
        var fuelQuantity = Required(context, "fuelQuantity");
        var netCalorificValue = Required(context, "netCalorificValue");
        var emissionFactor = Required(context, "energyEmissionFactor");
        var energy = fuelQuantity.NormalizedValue * netCalorificValue.NormalizedValue;
        var result = energy * emissionFactor.NormalizedValue;

        return DirectAmountFormula.CreateResult(
            context,
            result,
            context.Definition.OutputUnit,
            [
                new("energy", energy, "MJ", "Fuel quantity × net calorific value"),
                new("emissions", result, context.Definition.OutputUnit, "Energy × emission factor")
            ]);
    }

    private static FormulaValue Required(FormulaExecutionContext context, string key) =>
        context.Values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Missing required input: {key}.");
}
