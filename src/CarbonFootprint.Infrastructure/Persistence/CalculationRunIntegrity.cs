using System.Text.Json;
using CarbonFootprint.Domain.Modules.Calculations;

namespace CarbonFootprint.Infrastructure.Persistence;

/// <summary>Decides whether a stored calculation run's manifest is intact and readable enough to be exported.</summary>
public static class CalculationRunIntegrity
{
    public static bool TryReadRules(CalculationRunRecord run, out CanonicalManifest.ReportingRules rules)
    {
        rules = null!;
        if (!CanonicalManifest.HasValidSha256(run.CanonicalInputManifest, run.InputSha256)
            || !CanonicalManifest.TryReadBuildProvenance(run.CanonicalInputManifest, out _))
        {
            return false;
        }
        try
        {
            rules = CanonicalManifest.ReadReportingRules(run.CanonicalInputManifest, run.InputSha256);
            _ = CanonicalManifest.ReadEvidenceReferences(run.CanonicalInputManifest, run.InputSha256);
            return rules.RoundingDecimalPlaces is >= 0 and <= 12
                && rules.CutoffThresholdPercent is >= 0m and <= 100m;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }
}
