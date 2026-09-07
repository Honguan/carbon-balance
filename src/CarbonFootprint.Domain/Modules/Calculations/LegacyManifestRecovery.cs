using System.Text;
using System.Text.Json;

namespace CarbonFootprint.Domain.Modules.Calculations;

public static class LegacyManifestRecovery
{
    private static readonly string[] RootOrder =
    [
        "manifestSchemaVersion", "archiveFormatVersion", "applicationVersion", "sourceRevision", "engineBuild",
        "organizationId", "projectVersionId", "productVersionId", "periodStart", "periodEnd", "functionalUnit",
        "declaredUnit", "systemBoundary", "allocationMethod", "allocationReason", "exclusions", "assumptions",
        "estimationReason", "pcrVersion", "ruleSetVersion", "cutoffThresholdPercent", "roundingDecimalPlaces",
        "reportingRequirements", "gwpVersion", "unitCatalogueVersion", "stages", "activities"
    ];
    private static readonly string[] StageOrder = ["stage", "isApplicable", "reason"];
    private static readonly string[] ActivityOrder =
    [
        "id", "stage", "name", "kind", "supplierOrScenario", "equipmentCategory", "dataSourceType", "dataProvider",
        "collectionMethod", "sourceReference", "rawValue", "rawUnitCode", "canonicalValue", "canonicalUnitCode",
        "conversionRuleVersion", "amountFormulaId", "formulaInputs", "periodStart", "periodEnd", "factorVersionId",
        "factorValue", "factorNumeratorUnit", "factorDenominatorUnit", "allocationFactor", "isEstimated",
        "estimationReason", "dataQuality", "evidenceSha256"
    ];

    public static bool TryRecover(string storedJson, string expectedSha256, out string canonicalJson)
    {
        canonicalJson = string.Empty;
        if (CanonicalManifest.HasValidSha256(storedJson, expectedSha256))
        {
            canonicalJson = storedJson;
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(storedJson);
            var root = document.RootElement;
            if (root.GetProperty("manifestSchemaVersion").GetString() != "calculation-manifest-v2" ||
                root.GetProperty("archiveFormatVersion").GetString() != "verification-manifest-v1") return false;

            // Only bounded, known writer orders are tried; the original digest is the acceptance criterion.
            foreach (var restoreFormulaOrder in new[] { false, true })
            {
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    WriteObject(writer, root, RootOrder, restoreFormulaOrder);
                }
                var candidate = Encoding.UTF8.GetString(stream.ToArray());
                if (!CanonicalManifest.HasValidSha256(candidate, expectedSha256)) continue;
                canonicalJson = candidate;
                return true;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
        return false;
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement element, string[] order, bool restoreFormulaOrder)
    {
        RequireProperties(element, order);
        writer.WriteStartObject();
        foreach (var name in order)
        {
            var value = element.GetProperty(name);
            writer.WritePropertyName(name);
            switch (name)
            {
                case "stages":
                case "activities":
                    writer.WriteStartArray();
                    foreach (var item in value.EnumerateArray())
                        WriteObject(writer, item, name == "stages" ? StageOrder : ActivityOrder, restoreFormulaOrder);
                    writer.WriteEndArray();
                    break;
                case "formulaInputs":
                    WriteFormulaInputs(writer, value, element.GetProperty("amountFormulaId").GetString(), restoreFormulaOrder);
                    break;
                case "cutoffThresholdPercent":
                case "rawValue":
                case "canonicalValue":
                case "factorValue":
                case "allocationFactor":
                    writer.WriteNumberValue(value.GetDecimal());
                    break;
                case "roundingDecimalPlaces":
                    writer.WriteNumberValue(value.GetInt32());
                    break;
                case "isApplicable":
                case "isEstimated":
                    writer.WriteBooleanValue(value.GetBoolean());
                    break;
                default:
                    writer.WriteStringValue(value.GetString());
                    break;
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteFormulaInputs(Utf8JsonWriter writer, JsonElement inputs, string? formulaId, bool restoreOrder)
    {
        string[] order = formulaId switch
        {
            ActivityAmountFormula.DirectFormulaId => inputs.EnumerateObject().Any() ? ["value"] : [],
            ActivityAmountFormula.TransportFormulaId => ["distanceKm", "weightKg"],
            ActivityAmountFormula.UseScenarioFormulaId => ["lifetime", "frequency", "consumptionPerUse"],
            _ => throw new InvalidOperationException("Unsupported legacy amount formula.")
        };
        RequireProperties(inputs, order);
        writer.WriteStartObject();
        foreach (var name in restoreOrder ? order : inputs.EnumerateObject().Select(property => property.Name))
        {
            var value = inputs.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Number) throw new InvalidOperationException("Unsupported formula input.");
            writer.WritePropertyName(name);
            // The original manifest copied formula number lexemes, unlike its decimal fields.
            value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static void RequireProperties(JsonElement element, string[] expected)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != expected.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
            actual.Except(expected, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Unsupported or duplicate manifest properties.");
    }
}
