namespace CarbonFootprint.Domain.Modules.Calculations;

public sealed record CalculationBuildProvenance
{
    public const string CurrentManifestSchemaVersion = "calculation-manifest-v2";
    public const string CurrentArchiveFormatVersion = "verification-manifest-v1";

    private CalculationBuildProvenance(string applicationVersion, string sourceRevision)
    {
        ApplicationVersion = applicationVersion;
        SourceRevision = sourceRevision;
        EngineBuild = sourceRevision == "dev" ? "dev" : $"git:{sourceRevision}";
    }

    public string ApplicationVersion { get; }

    public string SourceRevision { get; }

    public string EngineBuild { get; }

    public bool IsVerifiable => SourceRevision != "dev";

    public string ManifestSchemaVersion => CurrentManifestSchemaVersion;

    public string ArchiveFormatVersion => CurrentArchiveFormatVersion;

    public static CalculationBuildProvenance Create(
        string? applicationVersion,
        string? sourceRevision,
        bool allowDevelopment = false)
    {
        var version = applicationVersion?.Trim() ?? string.Empty;
        var revision = sourceRevision?.Trim().ToLowerInvariant() ?? string.Empty;
        if (allowDevelopment && revision == "dev" && !string.IsNullOrWhiteSpace(version))
        {
            return new CalculationBuildProvenance(version, revision);
        }

        if (string.IsNullOrWhiteSpace(version)
            || version.Equals("dev", StringComparison.OrdinalIgnoreCase)
            || revision.Length is not (40 or 64)
            || revision.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("Build provenance 必須包含應用程式版本與完整 Git commit SHA。");
        }

        return new CalculationBuildProvenance(version, revision);
    }
}
