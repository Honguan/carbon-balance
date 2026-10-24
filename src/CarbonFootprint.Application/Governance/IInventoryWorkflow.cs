using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Application.Governance;

/// <summary>Inventory submission and review transitions.</summary>
public interface IInventoryWorkflow
{
    Task<OperationResult> SubmitAsync(
        OperationContext context, Guid projectVersionId, uint rowVersion,
        IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken);

    Task<OperationResult> ReviewAsync(
        OperationContext context, Guid projectVersionId, InventoryWorkflowStatus decision, string? reviewComment,
        uint rowVersion, CancellationToken cancellationToken);
}
