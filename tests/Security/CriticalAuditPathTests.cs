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
            var saveIndexes = new[] { "_dbContext.SaveChangesAsync", "SaveOrRejectStaleAsync(" }
                .Select(marker => body.IndexOf(marker, StringComparison.Ordinal)).Where(position => position >= 0).ToArray();
            if (saveIndexes.Length == 0) continue;
            mutations.Add(methods[index].Groups[1].Value);
            Assert.Contains("AddAudit(", body);
            Assert.True(body.IndexOf("AddAudit(", StringComparison.Ordinal) < saveIndexes.Min(), methods[index].Groups[1].Value);
        }
        foreach (var critical in new[] { "OnPostCreatePcrAsync", "OnPostCreateFactorAsync", "OnPostSupersedeFactorAsync" })
            Assert.Contains(critical, mutations);

        // Submit/review moved behind IInventoryWorkflow; the service must keep audit routing before persistence.
        var workflow = File.ReadAllText(Path.Combine(root, "src/CarbonFootprint.Infrastructure/Governance/InventoryWorkflowService.cs"));
        Assert.Contains("OperationAudit.AppendTracked(", workflow);
        Assert.True(workflow.IndexOf("OperationAudit.AppendTracked(", StringComparison.Ordinal) < workflow.IndexOf("SaveChangesAsync", StringComparison.Ordinal));

        // Extracted operations: every one that persists must append its audit before the first save.
        var persisting = new List<string>();
        foreach (var service in new[] { "ActivityDataService", "OrganizationAdministrationService", "ProductCatalogueService", "InventoryProjectService" })
        {
            var serviceSource = File.ReadAllText(Path.Combine(root, $"src/CarbonFootprint.Infrastructure/Governance/{service}.cs"));
            foreach (var operation in serviceSource.Split("public async Task<OperationResult> ")[1..])
            {
                var name = operation[..operation.IndexOf('(')];
                var saves = new[] { "db.SaveChangesAsync", "await SaveAsync(" }
                    .Select(marker => operation.IndexOf(marker, StringComparison.Ordinal)).Where(position => position >= 0).ToArray();
                if (saves.Length == 0) continue;
                persisting.Add(name);
                var audit = new[] { "OperationAudit.AppendTracked(", "new AuditWriter(db).Append(" }
                    .Select(marker => operation.IndexOf(marker, StringComparison.Ordinal)).Where(position => position >= 0).DefaultIfEmpty(-1).Min();
                Assert.True(audit >= 0 && audit < saves.Min(), $"{service}.{name}");
            }
        }
        foreach (var critical in new[] { "SaveAsync", "RetireAsync", "AttachEvidenceAsync", "SaveMailSettingsAsync", "SendTestMailAsync",
                     "RevokeInvitationAsync", "RevokeMemberAsync", "CreateFacilityAsync", "CreateProductAsync",
                     "CreateAsync", "SetStageApplicabilityAsync" })
            Assert.Contains(critical, persisting);
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
