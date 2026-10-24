using System.Text.RegularExpressions;

namespace CarbonFootprint.Architecture.Tests;

/// <summary>
/// Ratchet for issue #48: domain state transitions live behind application operations, not in Razor handlers.
/// Extracted aggregates must stay at zero; remaining ones may only shrink as they are extracted.
/// </summary>
public sealed class RazorHandlerBoundaryTests
{
    // Inventory submit/review transitions are extracted; keep this at zero.
    private const int RemainingWorkflowStatusAssignments = 0;

    // Lower as handlers are extracted; never raise it.
    private const int MaxWorkspacePageModelLines = 2413;

    [Fact]
    public void ExtractedGovernanceTransitions_DoNotReappearInRazorPageModels()
    {
        var forbidden = new Regex(
            @"\b(pcr|factor|predecessor|current)\.(PublicationStatus|ReviewStatus|CustomApprovalStatus|WithdrawnAt|PublishedAt|DeprecatedAt|ReviewedAt|ReviewedBy)\s*=(?!=)");

        foreach (var file in PageModelFiles())
        {
            var matches = forbidden.Matches(File.ReadAllText(file));
            Assert.True(matches.Count == 0,
                $"{Path.GetFileName(file)} assigns governance state directly ({matches.FirstOrDefault()?.Value}); use IPcrGovernance/IFactorGovernance.");
        }
    }

    [Fact]
    public void InventoryWorkflowTransitions_OnlyShrinkInRazorPageModels()
    {
        var assignment = new Regex(@"\.WorkflowStatus\s*=(?!=)");
        var count = PageModelFiles().Sum(file => assignment.Matches(File.ReadAllText(file)).Count);

        Assert.True(count <= RemainingWorkflowStatusAssignments,
            $"Razor handlers assign WorkflowStatus {count} times (budget {RemainingWorkflowStatusAssignments}); move transitions into an application operation.");
    }

    [Fact]
    public void WorkspacePageModel_DoesNotGrowBeyondItsBudget()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs")).Length;

        Assert.True(lines <= MaxWorkspacePageModelLines,
            $"Workspace.cshtml.cs has {lines} lines (budget {MaxWorkspacePageModelLines}); extract logic instead of adding to it.");
    }

    private static IEnumerable<string> PageModelFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src/CarbonFootprint.Web/Pages"), "*.cshtml.cs", SearchOption.AllDirectories);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root is required.");
    }
}
