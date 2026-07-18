namespace CarbonFootprint.Domain.Modules.Standards;

public sealed record PcrVersionReference(
    Guid Id,
    string RegistrationNumber,
    int VersionNumber,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string Status)
{
    public bool IsAvailableOn(DateOnly date) =>
        string.Equals(Status, "published", StringComparison.OrdinalIgnoreCase)
        && (ValidFrom is null || ValidFrom <= date)
        && (ValidTo is null || ValidTo >= date);
}

