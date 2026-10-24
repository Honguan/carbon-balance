using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class FactorGovernanceService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope) : IFactorGovernance
{
    private const string ResourceType = "EmissionFactorVersion";

    public async Task<OperationResult> ReviewAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        var (failure, factor) = await BeginAsync(context, factorVersionId, rowVersion, "ReviewFactor", cancellationToken);
        if (failure is not null) return failure;
        if (factor!.PublicationStatus != FactorPublicationStatus.Draft.ToString()) return OperationResult.BadRequest;

        factor.ReviewStatus = FactorReviewStatus.Approved.ToString();
        factor.ReviewedAt = DateTimeOffset.UtcNow;
        factor.ReviewedBy = context.ActorId;
        Audit(context, "factor.version.reviewed", factor.Id);
        return await SaveAsync(context, "ReviewFactor", factor.Id, OperationResult.Success(), cancellationToken);
    }

    public async Task<OperationResult> PublishAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        var (failure, factor) = await BeginAsync(context, factorVersionId, rowVersion, "PublishFactor", cancellationToken);
        if (failure is not null) return failure;
        if (!string.Equals(factor!.PublicationStatus, FactorPublicationStatus.Draft.ToString(), StringComparison.Ordinal)
            || factor.ReviewStatus != FactorReviewStatus.Approved.ToString())
        {
            return OperationResult.Invalid("factor", "只有草稿係數版本可發布。");
        }

        var publishedPredecessors = await db.EmissionFactorVersions
            .Where(item => item.FactorId == factor.FactorId && item.Id != factor.Id
                && item.PublicationStatus == FactorPublicationStatus.Published.ToString())
            .ToArrayAsync(cancellationToken);
        foreach (var predecessor in publishedPredecessors)
        {
            predecessor.PublicationStatus = FactorPublicationStatus.Withdrawn.ToString();
            predecessor.WithdrawnAt = DateTimeOffset.UtcNow;
            Audit(context, "factor.version.withdrawn", predecessor.Id);
        }

        factor.PublicationStatus = FactorPublicationStatus.Published.ToString();
        factor.PublishedAt = DateTimeOffset.UtcNow;
        Audit(context, "factor.version.published", factor.Id);
        return await SaveAsync(context, "PublishFactor", factor.Id, OperationResult.Success("係數版本已發布。"), cancellationToken);
    }

    public async Task<OperationResult> WithdrawAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        var (failure, factor) = await BeginAsync(context, factorVersionId, rowVersion, "WithdrawFactor", cancellationToken);
        if (failure is not null) return failure;
        if (!string.Equals(factor!.PublicationStatus, FactorPublicationStatus.Published.ToString(), StringComparison.Ordinal))
        {
            return OperationResult.Invalid("factor", "只有已發布係數版本可撤回。");
        }

        factor.PublicationStatus = FactorPublicationStatus.Withdrawn.ToString();
        factor.WithdrawnAt = DateTimeOffset.UtcNow;
        Audit(context, "factor.version.withdrawn", factor.Id);
        return await SaveAsync(context, "WithdrawFactor", factor.Id, OperationResult.Success("係數版本已撤回；歷史計算不受影響。"), cancellationToken);
    }

    private async Task<(OperationResult? Failure, EmissionFactorVersionRecord? Factor)> BeginAsync(
        OperationContext context, Guid factorVersionId, uint rowVersion, string operation, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return (OperationResult.Forbidden, null);
        var factor = await db.EmissionFactorVersions.SingleOrDefaultAsync(item => item.Id == factorVersionId, cancellationToken);
        if (factor is null) return (OperationResult.NotFound, null);
        if (rowVersion != factor.RowVersion) return (await StaleAsync(context, operation, factor.Id, cancellationToken), null);
        return (null, factor);
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
