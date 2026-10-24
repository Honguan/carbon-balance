using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Domain.Modules.Units;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class PcrGovernanceService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope) : IPcrGovernance
{
    private const string ResourceType = "PcrVersion";

    public Task<OperationResult> ReviewAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken) =>
        DecideAsync(context, pcrVersionId, rowVersion, approve: true, cancellationToken);

    public Task<OperationResult> RejectAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken) =>
        DecideAsync(context, pcrVersionId, rowVersion, approve: false, cancellationToken);

    private async Task<OperationResult> DecideAsync(
        OperationContext context, Guid pcrVersionId, uint rowVersion, bool approve, CancellationToken cancellationToken)
    {
        var operation = approve ? "ReviewPcr" : "RejectPcr";
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return OperationResult.Forbidden;
        var pcr = await db.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null) return OperationResult.NotFound;
        if (rowVersion != pcr.RowVersion) return await StaleAsync(context, operation, pcr.Id, cancellationToken);
        if (pcr.PublicationStatus != PcrPublicationStatus.Draft.ToString()
            || pcr.ReviewStatus != PcrReviewStatus.Pending.ToString()) return OperationResult.BadRequest;
        if (pcr.CreatedBy.HasValue && pcr.CreatedBy == context.ActorId)
        {
            return OperationResult.Invalid("pcr", approve
                ? "PCR 建立者不可核准自己的規則版本，請由另一位具權限且已啟用 MFA 的人員審查。"
                : "PCR 建立者不可審查自己建立的版本。");
        }

        pcr.ReviewStatus = (approve ? PcrReviewStatus.Approved : PcrReviewStatus.Rejected).ToString();
        if (pcr.IsCustomRule)
        {
            pcr.CustomApprovalStatus = (approve ? PcrCustomApprovalStatus.Approved : PcrCustomApprovalStatus.Rejected).ToString();
        }
        pcr.ReviewedAt = DateTimeOffset.UtcNow;
        pcr.ReviewedBy = context.ActorId;
        Audit(context, approve ? "pcr.version.reviewed" : "pcr.version.rejected", pcr.Id);
        return await SaveAsync(context, operation, pcr.Id, OperationResult.Success(), cancellationToken);
    }

    public async Task<OperationResult> PublishAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return OperationResult.Forbidden;
        var pcr = await db.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null) return OperationResult.NotFound;
        if (rowVersion != pcr.RowVersion) return await StaleAsync(context, "PublishPcr", pcr.Id, cancellationToken);

        var hasControlledDeclaredUnit = await db.Units.AnyAsync(
            item => item.CatalogueVersion == UnitCatalogue.CurrentVersion && item.Code == pcr.DeclaredUnitCode, cancellationToken);
        var hasLaterReleasedVersion = await db.PcrVersions.AnyAsync(
            item => item.RuleSetId == pcr.RuleSetId && item.VersionNumber > pcr.VersionNumber
                && item.PublicationStatus != PcrPublicationStatus.Draft.ToString(), cancellationToken);
        if (!string.Equals(pcr.PublicationStatus, PcrPublicationStatus.Draft.ToString(), StringComparison.Ordinal)
            || pcr.ReviewStatus != PcrReviewStatus.Approved.ToString()
            || hasLaterReleasedVersion
            || (pcr.IsCustomRule && pcr.CustomApprovalStatus != PcrCustomApprovalStatus.Approved.ToString())
            || !SourceDocumentIntegrity.TryNormalizeSha256(pcr.OriginalDocumentSha256, out _)
            || string.IsNullOrWhiteSpace(pcr.OriginalDocumentObjectKey)
            || !string.Equals(pcr.OriginalDocumentScanStatus, "Clean", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pcr.ProductCategoryPatterns)
            || string.IsNullOrWhiteSpace(pcr.FunctionalUnitPattern)
            || string.IsNullOrWhiteSpace(pcr.DeclaredUnitCode)
            || !hasControlledDeclaredUnit
            || string.IsNullOrWhiteSpace(pcr.SystemBoundaryCode)
            || !string.Equals(pcr.FormulaRuleSetVersion, ActivityEmissionFormula.PcrFormulaRuleSetV1, StringComparison.Ordinal)
            || await db.PcrStageRules.CountAsync(item => item.PcrVersionId == pcr.Id, cancellationToken) != Enum.GetValues<LifecycleStage>().Length)
        {
            return OperationResult.Invalid("pcr", "只有已核准、原始文件驗證成功且規則完整的 PCR 草稿可發布。");
        }

        pcr.PublicationStatus = PcrPublicationStatus.Published.ToString();
        pcr.PublishedAt = DateTimeOffset.UtcNow;
        var publishedPredecessors = await db.PcrVersions
            .Where(item => item.RuleSetId == pcr.RuleSetId && item.Id != pcr.Id && item.VersionNumber < pcr.VersionNumber
                && item.PublicationStatus == PcrPublicationStatus.Published.ToString())
            .ToArrayAsync(cancellationToken);
        foreach (var predecessor in publishedPredecessors)
        {
            predecessor.DeprecatedAt = DateTimeOffset.UtcNow;
            predecessor.DeprecationReason = $"由 {pcr.RegistrationNumber} 第 {pcr.VersionNumber} 版取代。";
            Audit(context, "pcr.version.superseded", predecessor.Id);
        }
        Audit(context, "pcr.version.published", pcr.Id);
        return await SaveAsync(context, "PublishPcr", pcr.Id, OperationResult.Success("PCR 版本已發布。"), cancellationToken);
    }

    public async Task<OperationResult> WithdrawAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return OperationResult.Forbidden;
        var pcr = await db.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null) return OperationResult.NotFound;
        if (rowVersion != pcr.RowVersion) return await StaleAsync(context, "WithdrawPcr", pcr.Id, cancellationToken);
        if (!string.Equals(pcr.PublicationStatus, PcrPublicationStatus.Published.ToString(), StringComparison.Ordinal))
        {
            return OperationResult.Invalid("pcr", "只有已發布 PCR 版本可撤回。");
        }

        pcr.PublicationStatus = PcrPublicationStatus.Withdrawn.ToString();
        pcr.WithdrawnAt = DateTimeOffset.UtcNow;
        Audit(context, "pcr.version.withdrawn", pcr.Id);
        return await SaveAsync(context, "WithdrawPcr", pcr.Id, OperationResult.Success("PCR 版本已撤回；歷史計算不受影響。"), cancellationToken);
    }

    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    private void Audit(OperationContext context, string action, Guid id) =>
        OperationAudit.AppendTracked(db, context, OrganizationId, action, ResourceType, id);

    private async Task<OperationResult> StaleAsync(OperationContext context, string operation, Guid id, CancellationToken cancellationToken)
    {
        await OperationAudit.RecordStaleRejectionAsync(db, context, OrganizationId, operation, ResourceType, id, cancellationToken);
        return OperationResult.Conflict;
    }

    private async Task<OperationResult> SaveAsync(
        OperationContext context, string operation, Guid id, OperationResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await StaleAsync(context, operation, id, cancellationToken);
        }
    }
}
