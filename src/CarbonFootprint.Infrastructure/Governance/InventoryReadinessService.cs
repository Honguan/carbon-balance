using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class InventoryReadinessService(CarbonFootprintDbContext db, IInventorySnapshotReader snapshotReader) : IInventoryReadiness
{
    public const string PendingStageFormulaRuleSetVersion = "legacy-stage-formulas-pending-review-v1";

    public async Task<ReadinessReport> BuildReportAsync(
        Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken) =>
        (await AssessAsync(projectVersionId, acknowledgements, cancellationToken)).Report;

    public async Task<string?> GetCurrentRunErrorAsync(Guid projectVersionId, CancellationToken cancellationToken) =>
        (await AssessAsync(projectVersionId, [], cancellationToken)).CurrentRunError;

    public async Task<ReadinessAssessment> AssessAsync(
        Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(projectVersionId, cancellationToken);
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var violations = await ValidatePcrAsync(project, requireCompleteInventory: true, cancellationToken);
        var run = await LatestRunAsync(project.Id, cancellationToken);
        var pendingRules = run is not null && string.Equals(run.RuleSetVersion, PendingStageFormulaRuleSetVersion, StringComparison.Ordinal);
        var provenanceMissing = run is not null && !pendingRules
            && (!CanonicalManifest.TryReadBuildProvenance(run.CanonicalInputManifest, out var provenance) || !provenance.IsVerifiable);

        if (!project.PcrVersionId.HasValue)
        {
            var missing = violations
                .Select(violation => new ReadinessFinding(violation.Code, ReadinessSeverity.BlockingError, violation.EntityType,
                    violation.EntityKey, null, string.Empty, violation.Message, "選擇適用且有效的 PCR 版本。"))
                .ToArray();
            return new ReadinessAssessment(
                new ReadinessReport(missing, [], asOf),
                RunError(run, pendingRules, provenanceMissing, violations, snapshot: null));
        }

        var snapshot = await snapshotReader.ReadAsync(project.Id, cancellationToken);
        var activityIds = snapshot.Activities.Select(item => item.Id).ToArray();
        var evidence = await db.EvidenceFiles.AsNoTracking()
            .Where(item => activityIds.Contains(item.ActivityDataId))
            .Select(item => new ReadinessEvidence(item.ActivityDataId, item.Sha256, item.ScanStatus))
            .ToArrayAsync(cancellationToken);
        var matches = run is not null && CanonicalManifest.Matches(snapshot, run.CanonicalInputManifest, run.InputSha256);
        var issue = run is null
            ? new ReadinessCalculationIssue("RDY-CALC-MISSING", "盤查至少需要一個不可變計算版本才能提交。")
            : pendingRules
                ? new ReadinessCalculationIssue("RDY-CALC-PENDING-RULES", "階段計算公式尚待領域審查，目前不可提交。")
                : provenanceMissing
                    ? new ReadinessCalculationIssue("RDY-CALC-PROVENANCE", "計算版本缺少可追溯的 build provenance。")
                    : matches
                        ? null
                        : new ReadinessCalculationIssue("RDY-CALC-STALE", "盤查資料已在最近一次計算後變更或快照雜湊不符，請重新計算。");
        return new ReadinessAssessment(
            InventoryReadinessValidator.Validate(new InventoryReadinessInput(snapshot, violations, evidence, issue, asOf, acknowledgements)),
            RunError(run, pendingRules, provenanceMissing, violations, snapshot));
    }

    private static string? RunError(
        CalculationRunRecord? run, bool pendingRules, bool provenanceMissing,
        IReadOnlyList<PcrRuleViolation> pcrViolations, InventoryProjectSnapshot? snapshot)
    {
        if (run is null) return "盤查至少需要一個不可變計算版本才能提交或核准。";
        if (pendingRules) return "階段計算公式尚待領域審查，目前不可提交或核准。";
        if (provenanceMissing) return "計算版本缺少可追溯的 build provenance，不可提交或核准。";
        if (pcrViolations.Count > 0)
        {
            return string.Join("；", pcrViolations.Select(violation => $"{violation.Code}：{violation.Message}"));
        }
        if (snapshot!.Activities.Any(activity => !activity.FactorVersion.IsSelectableOn(activity.PeriodEnd)))
        {
            return "計算所引用的係數未發布、已撤回、未通過審查或不在有效期，不可提交或核准。";
        }
        return CanonicalManifest.Matches(snapshot, run.CanonicalInputManifest, run.InputSha256)
            ? null : "盤查資料已在最近一次計算後變更或快照雜湊不符，請重新計算再提交或核准。";
    }

    public async Task<IReadOnlyList<PcrRuleViolation>> ValidatePcrAsync(
        Guid projectVersionId, bool requireCompleteInventory, CancellationToken cancellationToken) =>
        await ValidatePcrAsync(await LoadProjectAsync(projectVersionId, cancellationToken), requireCompleteInventory, cancellationToken);

    private Task<InventoryProjectVersionRecord> LoadProjectAsync(Guid projectVersionId, CancellationToken cancellationToken) =>
        db.InventoryProjectVersions.AsNoTracking().SingleAsync(item => item.Id == projectVersionId, cancellationToken);

    private Task<CalculationRunRecord?> LatestRunAsync(Guid projectVersionId, CancellationToken cancellationToken) =>
        db.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<PcrRuleViolation>> ValidatePcrAsync(
        InventoryProjectVersionRecord project, bool requireCompleteInventory, CancellationToken cancellationToken)
    {
        if (!project.PcrVersionId.HasValue)
        {
            return [new("PCR-MISSING", "InventoryProjectVersion", project.Id.ToString(), "盤查版本未綁定 PCR 規則版本。")];
        }

        var pcr = await db.PcrVersions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == project.PcrVersionId.Value, cancellationToken);
        if (pcr is null)
        {
            return [new("PCR-NOT-FOUND", "InventoryProjectVersion", project.Id.ToString(), "盤查綁定的 PCR 規則版本不存在。")];
        }

        var stageRules = await db.PcrStageRules.AsNoTracking()
            .Where(item => item.PcrVersionId == pcr.Id)
            .OrderBy(item => item.LifecycleStage)
            .ToArrayAsync(cancellationToken);
        var categoryCode = await (
                from productVersion in db.ProductVersions.AsNoTracking()
                join product in db.Products.AsNoTracking() on productVersion.ProductId equals product.Id
                where productVersion.Id == project.ProductVersionId
                select product.CategoryCode)
            .SingleAsync(cancellationToken);
        var declarations = await db.LifecycleStageDeclarations.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == project.Id)
            .ToDictionaryAsync(item => (LifecycleStage)item.LifecycleStage, item => item.IsApplicable, cancellationToken);
        var activities = await db.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == project.Id && item.RetiredAt == null)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var activityContexts = activities.Select(activity => new PcrActivityContext(
            activity.Id,
            (LifecycleStage)activity.LifecycleStage,
            Enum.TryParse<ActivityDataKind>(activity.ActivityKind, out var kind) ? kind : (ActivityDataKind)0,
            PcrRuleSetMapper.PopulatedActivityFields(activity))).ToArray();

        return PcrRuleEngine.Validate(
            PcrRuleSetMapper.ToRuleSet(pcr, stageRules),
            new PcrProjectContext(
                project.Id, categoryCode, project.PeriodEnd, project.FunctionalUnit, project.DeclaredUnit, project.SystemBoundary,
                project.AllocationMethod, declarations, activityContexts, project.Exclusions),
            requireCompleteInventory);
    }
}
