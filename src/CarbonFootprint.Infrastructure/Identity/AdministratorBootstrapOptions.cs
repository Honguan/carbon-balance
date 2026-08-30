namespace CarbonFootprint.Infrastructure.Identity;

public sealed class AdministratorBootstrapOptions
{
    public const string SectionName = "AdministratorBootstrap";

    public string Token { get; set; } = string.Empty;

    public static bool IsValidProductionToken(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Length is >= 32 and <= 128
        && !token.Contains("from-secret-manager", StringComparison.OrdinalIgnoreCase)
        && !token.Contains("replace-with", StringComparison.OrdinalIgnoreCase)
        && !token.Contains("change-this", StringComparison.OrdinalIgnoreCase);
}
