using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Unit.Tests;

public sealed class LegacyManifestRecoveryTests
{
    [Theory]
    [InlineData(ActivityAmountFormula.DirectFormulaId, "{}")]
    [InlineData(ActivityAmountFormula.DirectFormulaId, "{\"value\":1.2300}")]
    [InlineData(ActivityAmountFormula.TransportFormulaId, "{\"distanceKm\":12.3400,\"weightKg\":56.7800}")]
    [InlineData(ActivityAmountFormula.UseScenarioFormulaId, "{\"lifetime\":1.20,\"frequency\":2.300,\"consumptionPerUse\":0.00400}")]
    public void Recover_RestoresKnownWriterOrdersAndExactDecimalScale(string formula, string inputs)
    {
        foreach (var formulaInputs in new[] { inputs, JsonbLike(inputs) })
        {
            var original = CreateManifest(formula, formulaInputs);
            var stored = JsonbLike(original.Json);
            Assert.NotEqual(original.Json, stored);
            Assert.True(LegacyManifestRecovery.TryRecover(stored, original.Sha256, out var recovered));
            Assert.Equal(original.Json, recovered);
            Assert.Contains("1.2300", recovered, StringComparison.Ordinal);
            Assert.Contains("0.0000000000000000000000000001", recovered, StringComparison.Ordinal);
            Assert.Contains("79228162514264337593543950335", recovered, StringComparison.Ordinal);
            Assert.Contains("\\u003C", recovered, StringComparison.Ordinal);
            Assert.Contains("\\u78B3", recovered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Recover_AlreadyMatchingBytesAreReturnedUnchanged()
    {
        const string original = "{ \"legacy\": true }";
        Assert.True(LegacyManifestRecovery.TryRecover(original, CanonicalManifest.ComputeSha256(original), out var recovered));
        Assert.Equal(original, recovered);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("unknown-root")]
    [InlineData("unknown-stage")]
    [InlineData("unknown-activity")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("reordered-array")]
    [InlineData("invalid-json")]
    public void Recover_DoesNotDiscardDataOrAcceptTampering(string change)
    {
        var original = CreateManifest(ActivityAmountFormula.DirectFormulaId, "{}");
        var node = JsonNode.Parse(original.Json)!.AsObject();
        switch (change)
        {
            case "tampered": node["functionalUnit"] = "changed"; break;
            case "unknown-root": node["newProperty"] = 1; break;
            case "unknown-stage": node["stages"]![0]!["newProperty"] = 1; break;
            case "unknown-activity": node["activities"]![0]!["newProperty"] = 1; break;
            case "missing": node.Remove("functionalUnit"); break;
            case "reordered-array":
                var stages = node["stages"]!.AsArray();
                var first = stages[0];
                stages.RemoveAt(0);
                stages.Add(first);
                break;
        }
        var stored = JsonbLike(node.ToJsonString());
        if (change == "duplicate") stored = "{\"functionalUnit\":\"discarded\"," + stored[1..];
        if (change == "invalid-json") stored = "{";

        Assert.False(LegacyManifestRecovery.TryRecover(stored, original.Sha256, out var recovered));
        Assert.Equal(string.Empty, recovered);
    }

    [Theory]
    [InlineData(ActivityAmountFormula.DirectFormulaId, "{\"value\":1.230e2}")]
    [InlineData(ActivityAmountFormula.DirectFormulaId, "{\"value\":1,\"extra\":2}")]
    [InlineData(ActivityAmountFormula.DirectFormulaId, "{\"value\":{\"nested\":1}}")]
    [InlineData("unknown-formula", "{}")]
    public void Recover_RefusesUnknownInputsAndLostNumberLexemes(string formula, string inputs)
    {
        var original = CreateManifest(formula, inputs);
        Assert.False(LegacyManifestRecovery.TryRecover(JsonbLike(original.Json), original.Sha256, out var recovered));
        Assert.Equal(string.Empty, recovered);
    }

    [Fact]
    public void Recover_RejectsUnsupportedManifestVersion()
    {
        var original = CreateManifest(ActivityAmountFormula.DirectFormulaId, "{}").Json
            .Replace("calculation-manifest-v2", "calculation-manifest-v99", StringComparison.Ordinal);
        Assert.False(LegacyManifestRecovery.TryRecover(JsonbLike(original), CanonicalManifest.ComputeSha256(original), out _));
    }

    private static (string Json, string Sha256) CreateManifest(string formula, string inputs)
    {
        var organization = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);
        var factor = new EmissionFactorVersion(Guid.NewGuid(), Guid.NewGuid(), 1, "factor", decimal.MaxValue,
            "kgCO2e", "kg", "TW", start, end, FactorPublicationStatus.Published, "dataset", "license");
        var activity = new ActivityDataSnapshot(Guid.NewGuid(), organization, LifecycleStage.RawMaterial,
            "碳足跡 中文 日本語 한국어 <tag> & \"quote\" 😀\n", 1.2300m, "kg", 0.0000000000000000000000000001m,
            "kg", "units-1", start, end, factor, null, AllocationFactor: 0.5000m,
            AmountFormulaId: formula, FormulaInputsJson: inputs);
        var snapshot = new InventoryProjectSnapshot(organization, Guid.NewGuid(), Guid.NewGuid(), start, end,
            "1 item", "pcr-1", ActivityEmissionFormula.PcrFormulaRuleSetV1, "gwp-1", "units-1",
            [new(LifecycleStage.RawMaterial, true, null), new(LifecycleStage.Manufacturing, false, "不適用")],
            [activity, activity with { Id = Guid.NewGuid(), Name = "second" }], CutoffThresholdPercent: 0.0000m);
        return CanonicalManifest.Create(snapshot, CalculationBuildProvenance.Create("1.0-test", new string('a', 40)));
    }

    // Simulate jsonb's lost formatting and object order without requiring a database in Domain unit tests.
    private static string JsonbLike(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteJsonbLike(writer, document.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJsonbLike(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => Encoding.UTF8.GetByteCount(property.Name))
                         .ThenBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteJsonbLike(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteJsonbLike(writer, item);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Number) writer.WriteNumberValue(value.GetDecimal());
        else value.WriteTo(writer);
    }
}
