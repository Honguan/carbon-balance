using System.Text.RegularExpressions;

namespace CarbonFootprint.Security.Tests;

public sealed class CriticalAuditPathTests
{
    [Fact]
    public void EveryWorkspaceMutationRetainsAuditRoutingBeforePersistence()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs"));
        var methods = Regex.Matches(source, @"public async Task<IActionResult> (OnPost\w+Async)\(");
        var mutations = new List<string>();
        for (var index = 0; index < methods.Count; index++)
        {
            var end = index + 1 < methods.Count ? methods[index + 1].Index : source.IndexOf("private async", methods[index].Index, StringComparison.Ordinal);
            if (end < 0) end = source.Length;
            var body = source[methods[index].Index..end];
            if (!body.Contains("_dbContext.SaveChangesAsync", StringComparison.Ordinal)) continue;
            mutations.Add(methods[index].Groups[1].Value);
            Assert.Contains("AddAudit(", body);
            Assert.True(body.IndexOf("AddAudit(", StringComparison.Ordinal) < body.IndexOf("_dbContext.SaveChangesAsync", StringComparison.Ordinal), methods[index].Groups[1].Value);
        }
        foreach (var critical in new[] { "OnPostSaveMailSettingsAsync", "OnPostCreateFacilityAsync", "OnPostRevokeInvitationAsync",
                     "OnPostRevokeMemberAsync", "OnPostCreateProductAsync", "OnPostCreateInventoryAsync", "OnPostSubmitInventoryAsync", "OnPostReviewInventoryAsync" })
            Assert.Contains(critical, mutations);
    }

    [Fact]
    public void SecurityInventoryAndExportWritersShareVersionedContract()
    {
        var root = RepositoryRoot();
        foreach (var path in new[]
        {
            "src/CarbonFootprint.Infrastructure/Organizations/OrganizationOnboardingService.cs",
            "src/CarbonFootprint.Infrastructure/Organizations/OrganizationInvitationService.cs",
            "src/CarbonFootprint.Infrastructure/Identity/SystemAdministratorService.cs",
            "src/CarbonFootprint.Infrastructure/Persistence/CalculationRunStore.cs",
            "src/CarbonFootprint.Infrastructure/Persistence/CanonicalManifestRepair.cs",
            "src/CarbonFootprint.Infrastructure/LegacyImport/LegacyFactorCsvImporter.cs",
            "src/CarbonFootprint.Web/Services/MoenvFactorSynchronizationService.cs",
            "src/CarbonFootprint.Web/Pages/Reports.cshtml.cs"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, path));
            Assert.Contains("AuditWriter", source);
            Assert.DoesNotContain("new AuditEventRecord", source);
            Assert.DoesNotContain("new SystemAuditEventRecord", source);
            Assert.DoesNotContain("MetadataJson = \"{}\"", source);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root is required for critical audit path enumeration.");
    }
}
