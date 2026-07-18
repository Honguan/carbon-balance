namespace CarbonFootprint.Domain.Modules.Standards;

public enum PcrPublicationStatus
{
    Draft,
    Published,
    Withdrawn
}

public enum PcrReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public sealed record PcrVersionReference(
    Guid Id,
    string RegistrationNumber,
    int VersionNumber,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    PcrPublicationStatus Status,
    PcrReviewStatus ReviewStatus = PcrReviewStatus.Approved)
{
    public bool IsAvailableOn(DateOnly date) =>
        Status == PcrPublicationStatus.Published
        && ReviewStatus == PcrReviewStatus.Approved
        && (ValidFrom is null || ValidFrom <= date)
        && (ValidTo is null || ValidTo >= date);
}
