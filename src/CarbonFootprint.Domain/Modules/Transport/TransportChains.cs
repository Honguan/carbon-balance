namespace CarbonFootprint.Domain.Modules.Transport;

public enum TransportMode
{
    Road = 1,
    Rail = 2,
    Sea = 3,
    Air = 4,
    InlandWaterway = 5,
    Pipeline = 6,
    Custom = 99
}

public enum TransportCalculationMethod
{
    TonneKilometre = 1,
    VehicleKilometre = 2,
    ActualFuelUse = 3,
    ShipmentAllocation = 4,
    PcrDefined = 5
}

[Flags]
public enum TransportEmissionBoundary
{
    None = 0,
    TankToWheel = 1,
    WellToTank = 2,
    WellToWheel = TankToWheel | WellToTank
}

public sealed record TransportFactorComponents(
    Guid FactorVersionId,
    decimal TankToWheelFactor,
    decimal WellToTankFactor,
    string DenominatorUnit,
    string ResultUnit)
{
    public decimal FactorFor(TransportEmissionBoundary boundary) =>
        (boundary.HasFlag(TransportEmissionBoundary.TankToWheel) ? TankToWheelFactor : 0m)
        + (boundary.HasFlag(TransportEmissionBoundary.WellToTank) ? WellToTankFactor : 0m);
}

public sealed record TransportLeg(
    Guid Id,
    int Sequence,
    string Origin,
    string Destination,
    string Region,
    TransportMode Mode,
    string VehicleClass,
    string FuelType,
    decimal DistanceKilometres,
    string DistanceSource,
    decimal CargoMassTonnes,
    decimal VehicleCapacityTonnes,
    decimal LoadFactor,
    decimal EmptyReturnRatio,
    bool IsRefrigerated,
    decimal RefrigerationEnergyKwh,
    string TransshipmentDetails,
    TransportCalculationMethod CalculationMethod,
    TransportEmissionBoundary Boundary,
    decimal VehicleKilometres,
    decimal ActualFuelUse,
    string FuelUnit,
    decimal ShipmentAllocationShare,
    TransportFactorComponents Factor,
    IReadOnlyList<Guid> EvidenceDocumentVersionIds);

public sealed record TransportChainVersion(
    Guid Id,
    Guid ChainId,
    int VersionNumber,
    Guid OrganizationId,
    Guid ProjectVersionId,
    Guid ActivityId,
    string Name,
    IReadOnlyList<TransportLeg> Legs,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    bool IsTemplate,
    Guid? TemplateSourceVersionId,
    Guid? SupersedesVersionId = null);

public sealed record TransportLegResult(
    Guid LegId,
    int Sequence,
    decimal ActivityAmount,
    string ActivityUnit,
    decimal TankToWheelEmissions,
    decimal WellToTankEmissions,
    decimal RefrigerationEmissions,
    decimal TotalEmissions,
    string ResultUnit,
    string Trace);

public sealed record TransportChainResult(
    Guid ChainVersionId,
    IReadOnlyList<TransportLegResult> Legs,
    decimal TotalEmissions,
    string ResultUnit);

public sealed record TransportValidationError(
    string Code,
    string EntityKey,
    string Message);

public static class TransportChainCalculator
{
    public static IReadOnlyList<TransportValidationError> Validate(TransportChainVersion chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var errors = new List<TransportValidationError>();
        var chainKey = chain.Id.ToString("D");

        if (chain.Legs.Count == 0)
        {
            errors.Add(new("TRANSPORT-LEG-MISSING", chainKey, "A transport chain must contain at least one leg."));
            return errors;
        }

        var ordered = chain.Legs.OrderBy(leg => leg.Sequence).ToArray();
        if (ordered.Select(leg => leg.Sequence).Distinct().Count() != ordered.Length)
        {
            errors.Add(new("TRANSPORT-SEQUENCE-DUPLICATE", chainKey, "Transport leg sequence numbers must be unique."));
        }

        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Sequence != index + 1)
            {
                errors.Add(new("TRANSPORT-SEQUENCE-GAP", ordered[index].Id.ToString("D"), "Transport leg sequence must start at one and be continuous."));
            }

            ValidateLeg(ordered[index], errors);
        }

        return errors
            .OrderBy(error => error.Code, StringComparer.Ordinal)
            .ThenBy(error => error.EntityKey, StringComparer.Ordinal)
            .ToArray();
    }

    public static TransportChainResult Calculate(
        TransportChainVersion chain,
        decimal refrigerationElectricityFactor = 0m)
    {
        var errors = Validate(chain);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        var results = chain.Legs
            .OrderBy(leg => leg.Sequence)
            .Select(leg => CalculateLeg(leg, refrigerationElectricityFactor))
            .ToArray();
        var unit = results.Select(result => result.ResultUnit).Distinct(StringComparer.OrdinalIgnoreCase).Single();

        return new(
            chain.Id,
            results,
            results.Sum(result => result.TotalEmissions),
            unit);
    }

    public static TransportChainVersion InstantiateTemplate(
        TransportChainVersion template,
        Guid newChainVersionId,
        Guid newChainId,
        Guid projectVersionId,
        Guid activityId,
        string name,
        string createdBy,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<Guid, TransportLeg> overrides)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(overrides);
        if (!template.IsTemplate)
        {
            throw new InvalidOperationException("Only route templates can be instantiated." );
        }

        var legs = template.Legs
            .OrderBy(leg => leg.Sequence)
            .Select(leg => overrides.TryGetValue(leg.Id, out var replacement)
                ? replacement with { Sequence = leg.Sequence }
                : leg with { Id = Guid.NewGuid() })
            .ToArray();

        return new(
            newChainVersionId,
            newChainId,
            1,
            template.OrganizationId,
            projectVersionId,
            activityId,
            name,
            legs,
            createdAt,
            createdBy,
            false,
            template.Id,
            null);
    }

    private static void ValidateLeg(
        TransportLeg leg,
        ICollection<TransportValidationError> errors)
    {
        var key = leg.Id.ToString("D");
        if (string.IsNullOrWhiteSpace(leg.Origin) || string.IsNullOrWhiteSpace(leg.Destination))
        {
            errors.Add(new("TRANSPORT-ENDPOINT", key, "Origin and destination are required."));
        }

        if (leg.DistanceKilometres <= 0m)
        {
            errors.Add(new("TRANSPORT-DISTANCE", key, "Distance must be positive."));
        }

        if (string.IsNullOrWhiteSpace(leg.DistanceSource))
        {
            errors.Add(new("TRANSPORT-DISTANCE-SOURCE", key, "Distance source is required."));
        }

        if (leg.CargoMassTonnes <= 0m)
        {
            errors.Add(new("TRANSPORT-CARGO-MASS", key, "Cargo mass must be positive."));
        }

        if (leg.LoadFactor <= 0m || leg.LoadFactor > 1m)
        {
            errors.Add(new("TRANSPORT-LOAD-FACTOR", key, "Load factor must be greater than zero and no more than one."));
        }

        if (leg.EmptyReturnRatio < 0m || leg.EmptyReturnRatio > 1m)
        {
            errors.Add(new("TRANSPORT-EMPTY-RETURN", key, "Empty-return ratio must be between zero and one."));
        }

        if (leg.ShipmentAllocationShare <= 0m || leg.ShipmentAllocationShare > 1m)
        {
            errors.Add(new("TRANSPORT-ALLOCATION", key, "Shipment allocation share must be greater than zero and no more than one."));
        }

        if (leg.Factor.FactorVersionId == Guid.Empty)
        {
            errors.Add(new("TRANSPORT-FACTOR", key, "A published transport factor version is required."));
        }

        if (leg.Boundary == TransportEmissionBoundary.None)
        {
            errors.Add(new("TRANSPORT-BOUNDARY", key, "At least one transport emission boundary component is required."));
        }

        switch (leg.CalculationMethod)
        {
            case TransportCalculationMethod.TonneKilometre:
                RequireDenominator(leg, "t.km", errors);
                break;
            case TransportCalculationMethod.VehicleKilometre:
                if (leg.VehicleKilometres <= 0m)
                {
                    errors.Add(new("TRANSPORT-VEHICLE-KM", key, "Vehicle-kilometre calculation requires positive vehicle kilometres."));
                }
                RequireDenominator(leg, "vehicle.km", errors);
                break;
            case TransportCalculationMethod.ActualFuelUse:
                if (leg.ActualFuelUse <= 0m || string.IsNullOrWhiteSpace(leg.FuelUnit))
                {
                    errors.Add(new("TRANSPORT-FUEL", key, "Actual-fuel calculation requires a positive fuel amount and unit."));
                }
                if (!string.Equals(leg.Factor.DenominatorUnit, leg.FuelUnit, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new("TRANSPORT-FUEL-FACTOR-UNIT", key, "Fuel amount unit does not match the factor denominator unit."));
                }
                break;
            case TransportCalculationMethod.ShipmentAllocation:
                if (leg.VehicleCapacityTonnes <= 0m)
                {
                    errors.Add(new("TRANSPORT-CAPACITY", key, "Shipment allocation requires positive vehicle capacity."));
                }
                RequireDenominator(leg, "vehicle.km", errors);
                break;
            case TransportCalculationMethod.PcrDefined:
                if (leg.EvidenceDocumentVersionIds.Count == 0)
                {
                    errors.Add(new("TRANSPORT-PCR-EVIDENCE", key, "PCR-defined transport methods require supporting evidence."));
                }
                break;
        }

        if (leg.Mode == TransportMode.Custom && string.IsNullOrWhiteSpace(leg.VehicleClass))
        {
            errors.Add(new("TRANSPORT-CUSTOM-MODE", key, "Custom transport mode requires a vehicle or vessel class."));
        }

        if (leg.IsRefrigerated && leg.RefrigerationEnergyKwh < 0m)
        {
            errors.Add(new("TRANSPORT-REFRIGERATION", key, "Refrigeration energy cannot be negative."));
        }
    }

    private static void RequireDenominator(
        TransportLeg leg,
        string expected,
        ICollection<TransportValidationError> errors)
    {
        if (!string.Equals(leg.Factor.DenominatorUnit, expected, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(
                "TRANSPORT-FACTOR-UNIT",
                leg.Id.ToString("D"),
                $"The selected calculation method requires a factor denominated in {expected}."));
        }
    }

    private static TransportLegResult CalculateLeg(
        TransportLeg leg,
        decimal refrigerationElectricityFactor)
    {
        var activityAmount = leg.CalculationMethod switch
        {
            TransportCalculationMethod.TonneKilometre =>
                leg.CargoMassTonnes
                * leg.DistanceKilometres
                * (1m + leg.EmptyReturnRatio)
                / leg.LoadFactor,
            TransportCalculationMethod.VehicleKilometre =>
                leg.VehicleKilometres * (1m + leg.EmptyReturnRatio),
            TransportCalculationMethod.ActualFuelUse => leg.ActualFuelUse,
            TransportCalculationMethod.ShipmentAllocation =>
                leg.DistanceKilometres
                * (1m + leg.EmptyReturnRatio)
                * leg.ShipmentAllocationShare,
            TransportCalculationMethod.PcrDefined =>
                leg.CargoMassTonnes
                * leg.DistanceKilometres
                * leg.ShipmentAllocationShare,
            _ => throw new InvalidOperationException("Unsupported transport calculation method." )
        };

        var ttw = leg.Boundary.HasFlag(TransportEmissionBoundary.TankToWheel)
            ? activityAmount * leg.Factor.TankToWheelFactor
            : 0m;
        var wtt = leg.Boundary.HasFlag(TransportEmissionBoundary.WellToTank)
            ? activityAmount * leg.Factor.WellToTankFactor
            : 0m;
        var refrigeration = leg.IsRefrigerated
            ? leg.RefrigerationEnergyKwh * refrigerationElectricityFactor
            : 0m;
        var total = ttw + wtt + refrigeration;

        var trace = string.Join(
            "|",
            $"leg={leg.Id:D}",
            $"sequence={leg.Sequence}",
            $"mode={leg.Mode}",
            $"method={leg.CalculationMethod}",
            $"distanceKm={leg.DistanceKilometres:G29}",
            $"cargoTonnes={leg.CargoMassTonnes:G29}",
            $"loadFactor={leg.LoadFactor:G29}",
            $"emptyReturn={leg.EmptyReturnRatio:G29}",
            $"activity={activityAmount:G29}:{leg.Factor.DenominatorUnit}",
            $"factorVersion={leg.Factor.FactorVersionId:D}",
            $"ttw={ttw:G29}",
            $"wtt={wtt:G29}",
            $"refrigeration={refrigeration:G29}",
            $"total={total:G29}:{leg.Factor.ResultUnit}");

        return new(
            leg.Id,
            leg.Sequence,
            activityAmount,
            leg.Factor.DenominatorUnit,
            ttw,
            wtt,
            refrigeration,
            total,
            leg.Factor.ResultUnit,
            trace);
    }
}
