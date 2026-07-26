namespace CarbonFootprint.Domain.Modules.Standards;

public static class SourceDocumentIntegrity
{
    public static bool TryNormalizeSha256(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }
}
