using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Unit.Tests;

public sealed class GovernanceSelectionTests
{
    [Fact]
    public void Factor_MustBePublishedReviewedApplicableAndDateValid()
    {
        var pending = FactorReviewStatus.Pending;
        var factor = CreateFactor(pending, "Taiwan electricity");

        Assert.False(factor.IsSelectableOn(new DateOnly(2026, 1, 1)));
        Assert.True(CreateFactor(FactorReviewStatus.Approved, "Taiwan electricity")
            .IsSelectableOn(new DateOnly(2026, 1, 1)));
        Assert.False(CreateFactor(FactorReviewStatus.Approved, string.Empty)
            .IsSelectableOn(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Pcr_MustBePublishedReviewedAndDateValid()
    {
        var reference = new PcrVersionReference(
            Guid.NewGuid(),
            "PCR-P0",
            1,
            new DateOnly(2025, 1, 1),
            new DateOnly(2027, 12, 31),
            PcrPublicationStatus.Published,
            PcrReviewStatus.Pending);

        Assert.False(reference.IsAvailableOn(new DateOnly(2026, 1, 1)));
        var approved = reference with { ReviewStatus = PcrReviewStatus.Approved };
        Assert.True(approved.IsAvailableOn(new DateOnly(2026, 1, 1)));
    }

    private static EmissionFactorVersion CreateFactor(FactorReviewStatus reviewStatus, string applicability) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        "P0 factor",
        1m,
        "kgCO2e",
        "kg",
        "TW",
        new DateOnly(2025, 1, 1),
        new DateOnly(2027, 12, 31),
        FactorPublicationStatus.Published,
        "dataset-v1",
        "fixture",
        reviewStatus,
        applicability);
}
