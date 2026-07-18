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
                writer.WriteNumber("rawValue", activity.RawValue);
                writer.WriteString("rawUnitCode", activity.RawUnitCode);
                writer.WriteNumber("canonicalValue", activity.CanonicalValue);
                writer.WriteString("canonicalUnitCode", activity.CanonicalUnitCode);
                writer.WriteString("conversionRuleVersion", activity.ConversionRuleVersion);
                writer.WriteString("periodStart", activity.PeriodStart.ToString("yyyy-MM-dd", null));
                writer.WriteString("periodEnd", activity.PeriodEnd.ToString("yyyy-MM-dd", null));
                writer.WriteString("factorVersionId", activity.FactorVersion.Id);
                writer.WriteNumber("factorValue", activity.FactorVersion.Value);
                writer.WriteString("factorNumeratorUnit", activity.FactorVersion.NumeratorUnitCode);
                writer.WriteString("factorDenominatorUnit", activity.FactorVersion.DenominatorUnitCode);
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
}

