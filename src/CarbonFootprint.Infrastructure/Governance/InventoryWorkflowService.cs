using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class InventoryWorkflowService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope, IInventoryReadiness readiness) : IInventoryWorkflow
{
    private const string ResourceType = "InventoryProjectVersion";
    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    public async Task<OperationResult> SubmitAsync(
        OperationContext context, Guid projectVersionId, uint rowVersion,
        IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.EditInventory, requireMfa: true)) return OperationResult.Forbidden;
        var project = await db.InventoryProjectVersions.SingleOrDefaultAsync(item => item.Id == projectVersionId, cancellationToken);
        if (project is null) return OperationResult.NotFound;
        if (rowVersion != project.RowVersion) return await StaleAsync(context, "SubmitInventory", project.Id, cancellationToken);

        var report = await readiness.BuildReportAsync(project.Id, acknowledgements, cancellationToken);
        if (!report.CanSubmit)
        {
            return OperationResult.Invalid("review", report.Findings.Where(item => item.Severity == ReadinessSeverity.BlockingError)
                .Select(finding => $"{finding.Code}：{finding.Message}")
                .Concat(report.UnacknowledgedExplanations.Select(finding => $"{finding.Code}：{finding.Message}（須填寫說明並確認）"))
                .ToArray());
        }
        if (TryEnsureTransition(project, InventoryWorkflowStatus.Submitted) is { } rejected) return rejected;

        project.WorkflowStatus = InventoryWorkflowStatus.Submitted.ToString();
        project.SubmittedAt = DateTimeOffset.UtcNow;
        OperationAudit.AppendTracked(db, context, OrganizationId, "inventory.submitted", ResourceType, project.Id, metadata: new Dictionary<string, object?>
        {
            ["readinessReportSha256"] = report.Sha256(),
            ["readinessAsOf"] = report.AsOf.ToString("yyyy-MM-dd"),
            ["readinessFindingCounts"] = report.Findings.GroupBy(item => item.Severity.ToString())
                .OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (object?)group.Count()),
            ["readinessAcknowledgements"] = acknowledgements
                .Where(ack => report.Findings.Any(item => item.Code == ack.Code && item.EntityKey == ack.EntityKey))
                .OrderBy(ack => ack.Code, StringComparer.Ordinal).ThenBy(ack => ack.EntityKey, StringComparer.Ordinal)
                .Select(ack => new Dictionary<string, object?> { ["code"] = ack.Code, ["entityKey"] = ack.EntityKey, ["justification"] = ack.Justification })
                .ToArray()
        });
        return await SaveAsync(context, "SubmitInventory", project.Id, OperationResult.Success("盤查版本已送審。"), cancellationToken);
    }

    public async Task<OperationResult> ReviewAsync(
        OperationContext context, Guid projectVersionId, InventoryWorkflowStatus decision, string? reviewComment,
        uint rowVersion, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ReviewInventory, requireMfa: true)) return OperationResult.Forbidden;
        if (context.ActorId is not { } reviewerId) return OperationResult.Forbidden;
        if (decision is not InventoryWorkflowStatus.Approved and not InventoryWorkflowStatus.ChangesRequested) return OperationResult.BadRequest;
        if (decision == InventoryWorkflowStatus.ChangesRequested && string.IsNullOrWhiteSpace(reviewComment))
        {
            return OperationResult.Invalid("review", "要求補正時必須提供審查意見。");
        }

        var project = await db.InventoryProjectVersions.SingleOrDefaultAsync(item => item.Id == projectVersionId, cancellationToken);
        if (project is null) return OperationResult.NotFound;
        if (rowVersion != project.RowVersion) return await StaleAsync(context, "ReviewInventory", project.Id, cancellationToken);
        if (TryEnsureTransition(project, decision) is { } rejected) return rejected;

        if (decision == InventoryWorkflowStatus.Approved
            && await readiness.GetCurrentRunErrorAsync(project.Id, cancellationToken) is { } currentRunError)
        {
            return OperationResult.Invalid("review", currentRunError);
        }

        project.WorkflowStatus = decision.ToString();
        project.ReviewedAt = DateTimeOffset.UtcNow;
        project.ReviewedBy = reviewerId;
        project.ReviewComment = reviewComment?.Trim();
        OperationAudit.AppendTracked(
            db, context, OrganizationId,
            decision == InventoryWorkflowStatus.Approved ? "inventory.approved" : "inventory.changes-requested",
            ResourceType, project.Id, reason: project.ReviewComment);
        return await SaveAsync(context, "ReviewInventory", project.Id,
            OperationResult.Success(decision == InventoryWorkflowStatus.Approved ? "盤查版本已核准。" : "盤查版本已退回補正。"), cancellationToken);
    }

    private static OperationResult? TryEnsureTransition(InventoryProjectVersionRecord project, InventoryWorkflowStatus next)
    {
        try
        {
            InventoryWorkflow.EnsureTransition(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus), next);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return OperationResult.Invalid("review", exception.Message);
        }
    }

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
