using System.Globalization;
using System.Security.Claims;
using System.Text;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class ReportsModel : PageModel
{
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IOrganizationScope _organizationScope;

    public ReportsModel(
        CarbonFootprintDbContext dbContext,
        IAuthorizationService authorizationService,
        IOrganizationScope organizationScope)
    {
        _dbContext = dbContext;
        _authorizationService = authorizationService;
        _organizationScope = organizationScope;
    }

    public IReadOnlyList<CalculationRunRecord> Runs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (_organizationScope.OrganizationId.HasValue)
        {
            Runs = await _dbContext.CalculationRuns.AsNoTracking()
                .OrderByDescending(item => item.CreatedAt)
                .ToArrayAsync(cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostInventoryCsvAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var run = await _dbContext.CalculationRuns.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        var project = await _dbContext.InventoryProjectVersions.SingleAsync(
            item => item.Id == run.ProjectVersionId,
            cancellationToken);
        var lines = await _dbContext.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.ActivityId)
            .ToArrayAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("run_id,input_sha256,workflow_status,pcr_version,functional_unit,stage,activity_id,formula_id,activity_value,activity_unit,factor_version_id,factor_value,factor_unit,emissions,emissions_unit");
        foreach (var line in lines)
        {
            builder.AppendLine(string.Join(",",
                Csv(run.Id),
                Csv(run.InputSha256),
                Csv(project.WorkflowStatus),
                Csv(run.PcrVersion),
                Csv(project.FunctionalUnit),
                Csv(((LifecycleStage)line.LifecycleStage).ToString()),
                Csv(line.ActivityId),
                Csv(line.FormulaId),
                Csv(line.CanonicalActivityValue),
                Csv(line.ActivityUnitCode),
                Csv(line.FactorVersionId),
                Csv(line.FactorValue),
                Csv(line.FactorUnit),
                Csv(line.Emissions),
                Csv(line.EmissionsUnitCode)));
        }

        await AddExportAuditAsync("report.inventory-exported", run.Id, cancellationToken);
        return File(WithUtf8Bom(builder.ToString()), "text/csv; charset=utf-8", $"inventory-{run.Id:N}.csv");
    }

    public async Task<IActionResult> OnPostEvidenceIndexCsvAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var run = await _dbContext.CalculationRuns.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        var activityIds = await _dbContext.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .Select(item => item.ActivityId)
            .ToArrayAsync(cancellationToken);
        var evidenceFiles = await _dbContext.EvidenceFiles.AsNoTracking()
            .Where(item => activityIds.Contains(item.ActivityDataId))
            .OrderBy(item => item.ActivityDataId)
            .ToArrayAsync(cancellationToken);
        var builder = new StringBuilder("run_id,activity_id,file_name,content_type,size_bytes,sha256,scan_status,object_key\r\n");
        foreach (var evidence in evidenceFiles)
        {
            builder.AppendLine(string.Join(",",
                Csv(run.Id),
                Csv(evidence.ActivityDataId),
                Csv(evidence.OriginalFileName),
                Csv(evidence.ContentType),
                Csv(evidence.SizeBytes),
                Csv(evidence.Sha256),
                Csv(evidence.ScanStatus),
                Csv(evidence.ObjectKey)));
        }

        await AddExportAuditAsync("report.evidence-index-exported", run.Id, cancellationToken);
        return File(WithUtf8Bom(builder.ToString()), "text/csv; charset=utf-8", $"evidence-index-{run.Id:N}.csv");
    }

    public async Task<IActionResult> OnPostManifestAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var run = await _dbContext.CalculationRuns.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        await AddExportAuditAsync("report.manifest-exported", run.Id, cancellationToken);
        return File(
            Encoding.UTF8.GetBytes(run.CanonicalInputManifest),
            "application/json; charset=utf-8",
            $"calculation-manifest-{run.Id:N}.json");
    }

    private async Task<bool> CanViewAsync()
    {
        var result = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            new OrganizationPermissionRequirement(OrganizationPermission.ViewInventory));
        return result.Succeeded;
    }

    private async Task AddExportAuditAsync(string action, Guid runId, CancellationToken cancellationToken)
    {
        var organizationId = _organizationScope.OrganizationId
            ?? throw new InvalidOperationException("缺少組織範圍。");
        var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : (Guid?)null;
        _dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actorId,
            OrganizationId = organizationId,
            Action = action,
            ResourceType = "CalculationRun",
            ResourceId = runId,
            BeforeHash = null,
            AfterHash = null,
            CorrelationId = HttpContext.TraceIdentifier,
            MetadataJson = "{}"
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Csv(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static byte[] WithUtf8Bom(string value) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(value)];
}
