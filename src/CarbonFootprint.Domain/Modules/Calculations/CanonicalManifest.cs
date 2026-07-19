using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Domain.Modules.Calculations;

public static class CanonicalManifest
{
    public static (string Json, string Sha256) Create(InventoryProjectSnapshot snapshot, string engineBuild)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("organizationId", snapshot.OrganizationId);
            writer.WriteString("projectVersionId", snapshot.ProjectVersionId);
            writer.WriteString("productVersionId", snapshot.ProductVersionId);
            writer.WriteString("periodStart", snapshot.PeriodStart.ToString("yyyy-MM-dd", null));
            writer.WriteString("periodEnd", snapshot.PeriodEnd.ToString("yyyy-MM-dd", null));
            writer.WriteString("functionalUnit", snapshot.FunctionalUnit);
            writer.WriteString("declaredUnit", snapshot.DeclaredUnit);
            writer.WriteString("systemBoundary", snapshot.SystemBoundary);
            writer.WriteString("allocationMethod", snapshot.AllocationMethod);
            writer.WriteString("allocationReason", snapshot.AllocationReason);
            writer.WriteString("exclusions", snapshot.Exclusions);
            writer.WriteString("assumptions", snapshot.Assumptions);
            writer.WriteString("estimationReason", snapshot.EstimationReason);
            writer.WriteString("pcrVersion", snapshot.PcrVersion);
            writer.WriteString("ruleSetVersion", snapshot.RuleSetVersion);
            writer.WriteString("gwpVersion", snapshot.GwpVersion);
            writer.WriteString("unitCatalogueVersion", snapshot.UnitCatalogueVersion);
            writer.WriteString("engineBuild", engineBuild);
            writer.WriteStartArray("stages");
            foreach (var stage in snapshot.Stages.OrderBy(item => item.Stage))
            {
                writer.WriteStartObject();
                writer.WriteString("stage", stage.Stage.ToString());
                writer.WriteBoolean("isApplicable", stage.IsApplicable);
                if (stage.Reason is null)
                {
                    writer.WriteNull("reason");
                }
                else
                {
                    writer.WriteString("reason", stage.Reason);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("activities");
            foreach (var activity in snapshot.Activities.OrderBy(item => item.Id))
            {
                writer.WriteStartObject();
                writer.WriteString("id", activity.Id);
                writer.WriteString("stage", activity.Stage.ToString());
                writer.WriteString("name", activity.Name);
                writer.WriteString("kind", activity.Kind.ToString());
                writer.WriteString("supplierOrScenario", activity.SupplierOrScenario);
                writer.WriteString("equipmentCategory", activity.EquipmentCategory);
                writer.WriteString("dataSourceType", activity.DataSourceType);
                writer.WriteString("dataProvider", activity.DataProvider);
                writer.WriteString("collectionMethod", activity.CollectionMethod);
                writer.WriteString("sourceReference", activity.SourceReference);
                writer.WriteNumber("rawValue", activity.RawValue);
                writer.WriteString("rawUnitCode", activity.RawUnitCode);
                writer.WriteNumber("canonicalValue", activity.CanonicalValue);
                writer.WriteString("canonicalUnitCode", activity.CanonicalUnitCode);
                writer.WriteString("conversionRuleVersion", activity.ConversionRuleVersion);
                writer.WriteString("amountFormulaId", activity.AmountFormulaId);
                writer.WritePropertyName("formulaInputs");
                using (var formulaInputs = JsonDocument.Parse(activity.FormulaInputsJson))
                {
                    formulaInputs.RootElement.WriteTo(writer);
                }
                writer.WriteString("periodStart", activity.PeriodStart.ToString("yyyy-MM-dd", null));
                writer.WriteString("periodEnd", activity.PeriodEnd.ToString("yyyy-MM-dd", null));
                writer.WriteString("factorVersionId", activity.FactorVersion.Id);
                writer.WriteNumber("factorValue", activity.FactorVersion.Value);
                writer.WriteString("factorNumeratorUnit", activity.FactorVersion.NumeratorUnitCode);
                writer.WriteString("factorDenominatorUnit", activity.FactorVersion.DenominatorUnitCode);
                writer.WriteNumber("allocationFactor", activity.AllocationFactor);
                writer.WriteBoolean("isEstimated", activity.IsEstimated);
                writer.WriteString("estimationReason", activity.EstimationReason);
                writer.WriteString("dataQuality", activity.DataQuality);
                if (activity.EvidenceSha256 is null)
                {
                    writer.WriteNull("evidenceSha256");
                }
                else
                {
                    writer.WriteString("evidenceSha256", activity.EvidenceSha256);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        return (Encoding.UTF8.GetString(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static string ComputeSha256(string canonicalManifest) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalManifest)));

    public static bool HasValidSha256(string canonicalManifest, string expectedSha256) =>
        string.Equals(ComputeSha256(canonicalManifest), expectedSha256, StringComparison.Ordinal);

    public static bool Matches(InventoryProjectSnapshot snapshot, string engineBuild, string expectedSha256) =>
        string.Equals(Create(snapshot, engineBuild).Sha256, expectedSha256, StringComparison.Ordinal);
}
