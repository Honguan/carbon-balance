namespace CarbonFootprint.Domain.Modules.Factors;

public enum FactorPublicationStatus
{
    Draft,
    Published,
    Withdrawn
}

public enum FactorReviewStatus
{
    Pending,
    Approved,
    Rejected
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
    string LicenseCode,
    FactorReviewStatus ReviewStatus = FactorReviewStatus.Approved,
    string Applicability = "global")
{
    public bool IsSelectableOn(DateOnly date) =>
        Status == FactorPublicationStatus.Published
        && ReviewStatus == FactorReviewStatus.Approved
        && !string.IsNullOrWhiteSpace(Applicability)
        && (ValidFrom is null || ValidFrom <= date)
        && (ValidTo is null || ValidTo >= date);
}
