using System.Text.Json;
using CarbonFootprint.Domain.Modules.Calculations;

namespace CarbonFootprint.Contract.Tests;

public sealed class CanonicalManifestContractTests
{
    [Fact]
    public void ManifestSource_UsesExplicitStableContractFields()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CarbonFootprint.Domain",
            "Modules",
            "Calculations",
            "CanonicalManifest.cs"));

        var requiredFields = new[]
        {
            "organizationId", "projectVersionId", "productVersionId", "periodStart", "periodEnd",
            "pcrVersion", "ruleSetVersion", "gwpVersion", "unitCatalogueVersion", "engineBuild",
            "activities", "factorVersionId", "factorValue"
        };

        foreach (var field in requiredFields)
        {
            Assert.Contains($"\"{field}\"", source, StringComparison.Ordinal);
        }

        Assert.NotNull(typeof(CanonicalManifest));
        Assert.NotNull(typeof(JsonDocument));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarbonFootprint.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到 repository root。");
    }
}

