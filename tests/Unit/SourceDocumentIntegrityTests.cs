using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Tests.Unit;

public sealed class SourceDocumentIntegrityTests
{
    [Fact]
    public void Accepts_uppercase_and_normalizes_to_lowercase()
    {
        Assert.True(SourceDocumentIntegrity.TryNormalizeSha256(new string('A', 64), out var value));
        Assert.Equal(new string('a', 64), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Rejects_invalid_values(string input)
    {
        Assert.False(SourceDocumentIntegrity.TryNormalizeSha256(input, out var value));
        Assert.Empty(value);
    }
}
