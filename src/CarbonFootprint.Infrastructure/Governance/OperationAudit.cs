using CarbonFootprint.Application.Operations;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

/// <summary>Adds transactional audit evidence for the tracked changes of one application operation; never saves.</summary>
public static class OperationAudit
{
    public static void AppendTracked(
        CarbonFootprintDbContext db, OperationContext context, Guid organizationId, string action,
        string resourceType, Guid resourceId, string? reason = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var writer = new AuditWriter(db);
        db.ChangeTracker.DetectChanges();
        var entries = db.ChangeTracker.Entries().Where(entry => AuditProjection.Supports(entry.Metadata.ClrType)).ToArray();
        var primary = entries.Single(entry => AuditProjection.ResourceType(entry.Metadata.ClrType) == resourceType
            && (Guid)entry.Property("Id").CurrentValue! == resourceId);
        writer.AppendTracked(action, primary, context.ActorId,
            string.IsNullOrWhiteSpace(reason) ? $"Authorized workspace operation: {action}" : reason,
            context.CorrelationId, context.SessionReference, metadata);
        // Related resources (e.g. product+version, PCR+rules, evidence+activity) share this operation's transaction/correlation.
        foreach (var related in entries.Where(entry => entry.Metadata.ClrType != primary.Metadata.ClrType
                     && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var relatedId = (Guid)related.Property("Id").CurrentValue!;
            if (db.AuditEvents.Local.Any(audit => audit.ResourceId == relatedId && audit.CorrelationId == context.CorrelationId)) continue;
            writer.AppendTracked($"{action}.related", related, context.ActorId, $"Related resource changed by {action}",
                context.CorrelationId, context.SessionReference);
        }
    }

    /// <summary>Records a rejected stale request as a non-mutating attempt, after discarding the unsaved changes.</summary>
    public static async Task RecordStaleRejectionAsync(
        CarbonFootprintDbContext db, OperationContext context, Guid organizationId, string operation,
        string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        new AuditWriter(db).Append("concurrency.rejected", resourceType, resourceId, organizationId, context.ActorId, null, null,
            "Stale request rejected; no resource state mutation", context.CorrelationId, context.SessionReference, "attempt",
            new Dictionary<string, object?> { ["attemptedOperation"] = operation });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}
