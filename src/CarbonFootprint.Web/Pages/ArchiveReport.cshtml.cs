using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class ArchiveReportModel : PageModel
{
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IAuthorizationService _authorizationService;

    public ArchiveReportModel(CarbonFootprintDbContext dbContext, IAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
    }

    public CalculationRunRecord Run { get; private set; } = null!;

    public InventoryProjectVersionRecord Project { get; private set; } = null!;

    public IReadOnlyList<CalculationLineRecord> Lines { get; private set; } = [];

    public IReadOnlyList<CalculationWarningRecord> Warnings { get; private set; } = [];

    public IReadOnlyDictionary<Guid, string> EvidenceHashes { get; private set; } = new Dictionary<Guid, string>();

    public DateTimeOffset GeneratedAt { get; } = DateTimeOffset.UtcNow;

    public string ApplicationVersion => typeof(ArchiveReportModel).Assembly.GetName().Version?.ToString() ?? "unknown";

    public string DatasetVersions { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid runId, CancellationToken cancellationToken)
    {
        var permission = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            new OrganizationPermissionRequirement(OrganizationPermission.ViewInventory));
        var mfa = await _authorizationService.AuthorizeAsync(User, resource: null, new MfaEnabledRequirement());
        if (!permission.Succeeded || !mfa.Succeeded)
        {
            return Forbid();
        }

        var run = await _dbContext.CalculationRuns.AsNoTracking().SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }
        Run = run;
        Project = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .SingleAsync(item => item.Id == run.ProjectVersionId, cancellationToken);
        Lines = await _dbContext.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.ActivityId)
            .ToArrayAsync(cancellationToken);
        Warnings = await _dbContext.CalculationWarnings.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.Code)
            .ToArrayAsync(cancellationToken);
        var activityIds = Lines.Select(item => item.ActivityId).ToArray();
        var evidence = await _dbContext.EvidenceFiles.AsNoTracking()
            .Where(item => activityIds.Contains(item.ActivityDataId))
            .ToArrayAsync(cancellationToken);
        EvidenceHashes = evidence
            .GroupBy(item => item.ActivityDataId)
            .ToDictionary(group => group.Key, group => string.Join(";", group.Select(item => item.Sha256)));
        var factorIds = Lines.Select(item => item.FactorVersionId).Distinct().ToArray();
        DatasetVersions = string.Join(", ", await _dbContext.EmissionFactorVersions.AsNoTracking()
            .Where(item => factorIds.Contains(item.Id))
            .Select(item => item.SourceDatasetVersion)
            .Distinct()
            .OrderBy(item => item)
            .ToArrayAsync(cancellationToken));
        return Page();
    }
}
