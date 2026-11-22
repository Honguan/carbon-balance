using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Web.Pages;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Services;

public sealed record ProjectPicker(IReadOnlyList<InventoryProjectVersionRecord> Projects, int Total);

public sealed record FactorPage(IReadOnlyList<EmissionFactorVersionRecord> Items, int Total, int PageCount, int Page);

public sealed record StageData(
    IReadOnlyList<ActivityDataRecord> Activities,
    ActivityDataRecord? EditingActivity,
    IReadOnlyList<EvidenceFileRecord> Evidence,
    IReadOnlyDictionary<Guid, EmissionFactorVersionRecord> ReferencedFactors,
    IReadOnlyList<EmissionFactorVersionRecord> SelectableFactors,
    int SelectableFactorTotal);

public sealed record CalculationResults(
    IReadOnlyList<CalculationRunRecord> Runs,
    CanonicalManifest.ReportingRules? ReportingRules,
    bool? ManifestHashValid,
    IReadOnlyList<CalculationLineRecord> Lines,
    int LineTotal,
    IReadOnlyList<CalculationWarningRecord> Warnings,
    CalculationRunDifference? Difference);

/// <summary>
/// Bounded, project-scoped read queries for the workspace (#49): every list has a hard limit and the
/// heavy columns are only read for the single run/stage the page renders.
/// </summary>
public sealed class WorkspaceQueries(CarbonFootprintDbContext db)
{
    public const int MaxListedProjects = 100;
    public const int FactorPageSize = 50;
    public const int MaxSelectableFactors = 1000;
    public const int MaxListedCalculationLines = 200;

    /// <summary>Newest projects first, bounded; the requested project is always included when it exists.</summary>
    public async Task<ProjectPicker> ListProjectsAsync(Guid? requestedProjectId, CancellationToken cancellationToken)
    {
        var query = db.InventoryProjectVersions.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var projects = await query.OrderByDescending(item => item.CreatedAt).Take(MaxListedProjects).ToListAsync(cancellationToken);
        if (requestedProjectId.HasValue && projects.All(item => item.Id != requestedProjectId.Value)
            && await query.SingleOrDefaultAsync(item => item.Id == requestedProjectId.Value, cancellationToken) is { } requested)
        {
            projects.Add(requested);
        }
        return new ProjectPicker(projects, total);
    }

    public async Task<FactorPage> ListFactorsAsync(string? search, int page, CancellationToken cancellationToken)
    {
        var query = db.EmissionFactorVersions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(item => EF.Functions.ILike(item.Name, pattern, "\\")
                || EF.Functions.ILike(item.DenominatorUnitCode, pattern, "\\")
                || EF.Functions.ILike(item.SourceName, pattern, "\\")
                || EF.Functions.ILike(item.SourceDatasetVersion, pattern, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (total + FactorPageSize - 1) / FactorPageSize);
        var current = Math.Clamp(page, 1, pageCount);
        var items = await query.OrderBy(item => item.Name).ThenBy(item => item.VersionNumber).ThenBy(item => item.Id)
            .Skip((current - 1) * FactorPageSize).Take(FactorPageSize).ToArrayAsync(cancellationToken);
        return new FactorPage(items, total, pageCount, current);
    }

    /// <summary>Activities of one project (one stage when given), with only the evidence and factors they reference.</summary>
    public async Task<StageData> LoadStageAsync(
        Guid projectId, LifecycleStage? stage, Guid? editingActivityId, CancellationToken cancellationToken)
    {
        var active = db.ActivityData.AsNoTracking().Where(item => item.InventoryProjectVersionId == projectId && item.RetiredAt == null);
        var scoped = stage is { } value ? active.Where(item => item.LifecycleStage == (int)value) : active;
        var activities = await scoped.OrderBy(item => item.LifecycleStage).ThenBy(item => item.Name).ToArrayAsync(cancellationToken);
        var editing = editingActivityId is { } id
            ? await active.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            : null;
        var activityIds = activities.Select(item => item.Id).ToArray();
        var evidence = await db.EvidenceFiles.AsNoTracking()
            .Where(item => activityIds.Contains(item.ActivityDataId)).OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        var factorIds = activities.Select(item => item.FactorVersionId).Distinct().ToArray();
        var referenced = await db.EmissionFactorVersions.AsNoTracking()
            .Where(item => factorIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var published = db.EmissionFactorVersions.AsNoTracking()
            .Where(item => item.PublicationStatus == FactorPublicationStatus.Published.ToString());
        var publishedTotal = await published.CountAsync(cancellationToken);
        var selectable = await published.OrderBy(item => item.Name).ThenBy(item => item.VersionNumber).ThenBy(item => item.Id)
            .Take(MaxSelectableFactors).ToArrayAsync(cancellationToken);
        return new StageData(activities, editing, evidence, referenced, selectable, publishedTotal);
    }

    /// <summary>Latest run with its (capped) lines and warnings, plus the difference against the previous run.</summary>
    public async Task<CalculationResults> LoadCalculationResultsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var runs = await db.CalculationRuns.AsNoTracking().Where(item => item.ProjectVersionId == projectId)
            .OrderByDescending(item => item.CreatedAt).Take(2).ToArrayAsync(cancellationToken);
        if (runs.Length == 0)
        {
            return new CalculationResults(runs, null, null, [], 0, [], null);
        }

        var latest = runs[0];
        var rules = RunExportValidation.TryReadRules(latest, out var reportingRules) ? reportingRules : null;
        var hashValid = CanonicalManifest.HasValidSha256(latest.CanonicalInputManifest, latest.InputSha256);
        var lines = db.CalculationLineItems.AsNoTracking().Where(item => item.CalculationRunId == latest.Id);
        var lineTotal = await lines.CountAsync(cancellationToken);
        var listedLines = await lines.OrderBy(item => item.LifecycleStage).ThenBy(item => item.ActivityId)
            .Take(MaxListedCalculationLines).ToArrayAsync(cancellationToken);
        var warnings = await db.CalculationWarnings.AsNoTracking()
            .Where(item => item.CalculationRunId == latest.Id).OrderBy(item => item.Code).ToArrayAsync(cancellationToken);

        CalculationRunDifference? difference = null;
        if (runs.Length > 1)
        {
            var comparedRunIds = new[] { runs[0].Id, runs[1].Id };
            var summaries = await db.CalculationStageSummaries.AsNoTracking()
                .Where(item => comparedRunIds.Contains(item.CalculationRunId)).ToArrayAsync(cancellationToken);
            CalculationRunTotals Totals(CalculationRunRecord run) => new(
                run.Id, run.ProductTotal,
                summaries.Where(item => item.CalculationRunId == run.Id)
                    .ToDictionary(item => (LifecycleStage)item.LifecycleStage, item => item.Emissions));
            difference = CalculationRunDiff.Compare(Totals(runs[1]), Totals(runs[0]));
        }
        return new CalculationResults(runs, rules, hashValid, listedLines, lineTotal, warnings, difference);
    }
}
