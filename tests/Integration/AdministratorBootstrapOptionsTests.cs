using CarbonFootprint.Infrastructure.Identity;

namespace CarbonFootprint.Integration.Tests;

public sealed class AdministratorBootstrapOptionsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("short", false)]
    [InlineData("from-secret-manager-at-least-32-characters", false)]
    [InlineData("replace-with-secret-from-provider-123456", false)]
    [InlineData("change-this-production-bootstrap-token-123", false)]
    [InlineData("valid-production-bootstrap-token-123456", true)]
    public void ProductionToken_RejectsMissingWeakAndPlaceholderValues(string? token, bool expected)
    {
        Assert.Equal(expected, AdministratorBootstrapOptions.IsValidProductionToken(token));
    }
}
