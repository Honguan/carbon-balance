using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Unit.Tests;

public sealed class PcrVersionReferenceTests
{
    [Theory]
    [InlineData(PcrPublicationStatus.Draft, false)]
    [InlineData(PcrPublicationStatus.Published, true)]
    [InlineData(PcrPublicationStatus.Withdrawn, false)]
    public void IsAvailableOn_RequiresPublishedVersionWithinValidity(
        PcrPublicationStatus status,
        bool expected)
    {
        var reference = new PcrVersionReference(
            Guid.NewGuid(),
            "PCR-TEST",
            1,
            new DateOnly(2025, 1, 1),
            new DateOnly(2027, 12, 31),
            status);

        Assert.Equal(expected, reference.IsAvailableOn(new DateOnly(2026, 12, 31)));
        Assert.False(reference.IsAvailableOn(new DateOnly(2028, 1, 1)));
    }
}
