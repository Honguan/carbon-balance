namespace CarbonFootprint.Domain.Modules.Standards;

public enum PcrPublicationStatus
{
    Draft,
    Published,
    Withdrawn
}

public sealed record PcrVersionReference(
    Guid Id,
    string RegistrationNumber,
    int VersionNumber,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    PcrPublicationStatus Status)
{
    public bool IsAvailableOn(DateOnly date) =>
        Status == PcrPublicationStatus.Published
        && (ValidFrom is null || ValidFrom <= date)
        && (ValidTo is null || ValidTo >= date);
}
