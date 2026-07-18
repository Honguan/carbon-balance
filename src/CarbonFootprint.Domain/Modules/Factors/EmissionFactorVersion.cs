namespace CarbonFootprint.Domain.Modules.Factors;

public enum FactorPublicationStatus
{
    Draft,
    Published,
    Withdrawn
}

public sealed record EmissionFactorVersion(
    Guid Id,
    Guid FactorId,
    int VersionNumber,
    string Name,
    decimal Value,
    string NumeratorUnitCode,
    string DenominatorUnitCode,
    string Geography,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    FactorPublicationStatus Status,
    string SourceDatasetVersion,
    string LicenseCode)
{
    public bool IsSelectableOn(DateOnly date) =>
        Status == FactorPublicationStatus.Published
        && (ValidFrom is null || ValidFrom <= date)
        && (ValidTo is null || ValidTo >= date);
}

