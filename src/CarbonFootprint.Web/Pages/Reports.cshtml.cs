using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Calculations;
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
    public IReadOnlySet<Guid> InvalidRunIds { get; private set; } = new HashSet<Guid>();
    public bool CanExport { get; private set; }

    public IReadOnlyDictionary<Guid, CanonicalManifest.ReportingRules> PcrRulesByRunId { get; private set; } =
        new Dictionary<Guid, CanonicalManifest.ReportingRules>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanExport = await CanViewAsync();
        if (_organizationScope.OrganizationId.HasValue)
        {
            Runs = await _dbContext.CalculationRuns.AsNoTracking()
                .OrderByDescending(item => item.CreatedAt)
                .ToArrayAsync(cancellationToken);
            var rules = new Dictionary<Guid, CanonicalManifest.ReportingRules>();
            var invalidRunIds = new HashSet<Guid>();
            foreach (var run in Runs)
            {
                if (RunExportValidation.TryReadRules(run, out var rule))
                {
                    rules.Add(run.Id, rule);
                }
                else
                {
                    invalidRunIds.Add(run.Id);
                }
            }
            PcrRulesByRunId = rules;
            InvalidRunIds = invalidRunIds;
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

        if (!RunExportValidation.TryReadRules(run, out var pcr))
        {
            return RunExportValidation.ConflictResult();
        }
        var project = await _dbContext.InventoryProjectVersions.SingleAsync(
            item => item.Id == run.ProjectVersionId,
            cancellationToken);
        var lines = await _dbContext.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.ActivityId)
            .ToArrayAsync(cancellationToken);
        var roundingDecimalPlaces = Math.Clamp(pcr.RoundingDecimalPlaces, 0, 12);
        var builder = new StringBuilder();
        builder.AppendLine("run_id,input_sha256,workflow_status,pcr_version,functional_unit,stage,activity_id,formula_id,activity_value,activity_unit,factor_version_id,factor_value,factor_unit,allocation_factor,emissions,emissions_unit,reported_emissions,cutoff_threshold_percent,rounding_decimal_places,reporting_requirements");
        foreach (var line in lines)
        {
            builder.AppendLine(string.Join(",",
                Csv(run.Id),
                Csv(run.InputSha256),
                Csv(project.WorkflowStatus),
                Csv(run.PcrVersion),
                Csv(pcr.FunctionalUnit),
                Csv(((LifecycleStage)line.LifecycleStage).ToString()),
                Csv(line.ActivityId),
                Csv(line.FormulaId),
                Csv(line.CanonicalActivityValue),
                Csv(line.ActivityUnitCode),
                Csv(line.FactorVersionId),
                Csv(line.FactorValue),
                Csv(line.FactorUnit),
                Csv(line.AllocationFactor),
                Csv(line.Emissions),
                Csv(line.EmissionsUnitCode),
                Csv(line.Emissions.ToString($"F{roundingDecimalPlaces}", CultureInfo.InvariantCulture)),
                Csv(pcr.CutoffThresholdPercent),
                Csv(roundingDecimalPlaces),
                Csv(pcr.ReportingRequirements)));
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

        if (!RunExportValidation.TryReadRules(run, out _))
        {
            return RunExportValidation.ConflictResult();
        }
        var references = CanonicalManifest.ReadEvidenceReferences(run.CanonicalInputManifest, run.InputSha256);
        var activityIds = references.Keys.ToArray();
        var evidenceFiles = await _dbContext.EvidenceFiles.AsNoTracking()
            .Where(item => activityIds.Contains(item.ActivityDataId))
            .OrderBy(item => item.ActivityDataId)
            .ToArrayAsync(cancellationToken);
        var builder = new StringBuilder("run_id,activity_id,file_name,content_type,size_bytes,sha256,scan_status,object_key\r\n");
        foreach (var evidence in evidenceFiles.Where(item =>
            string.Equals(references[item.ActivityDataId], item.Sha256, StringComparison.OrdinalIgnoreCase)))
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

        if (!RunExportValidation.TryReadRules(run, out _))
        {
            return RunExportValidation.ConflictResult();
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
        if (!result.Succeeded)
        {
            return false;
        }

        var mfaResult = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            new MfaEnabledRequirement());
        return mfaResult.Succeeded;
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

internal static class RunExportValidation
{
    public static bool TryReadRules(CalculationRunRecord run, out CanonicalManifest.ReportingRules rules)
    {
        rules = null!;
        if (!CanonicalManifest.HasValidSha256(run.CanonicalInputManifest, run.InputSha256)
            || !CanonicalManifest.TryReadBuildProvenance(run.CanonicalInputManifest, out _))
        {
            return false;
        }
        try
        {
            rules = CanonicalManifest.ReadReportingRules(run.CanonicalInputManifest, run.InputSha256);
            _ = CanonicalManifest.ReadEvidenceReferences(run.CanonicalInputManifest, run.InputSha256);
            return rules.RoundingDecimalPlaces is >= 0 and <= 12
                && rules.CutoffThresholdPercent is >= 0m and <= 100m;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public static ObjectResult ConflictResult() => new(
        "此計算版本的輸入快照雜湊不符或格式不受支援，暫停匯出。請由管理者檢查原始資料與修復紀錄；本次操作未變更任何資料。")
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}
