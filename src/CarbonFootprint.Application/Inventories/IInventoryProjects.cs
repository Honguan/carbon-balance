using CarbonFootprint.Application.Operations;

namespace CarbonFootprint.Application.Inventories;

public sealed record InventoryInput(
    Guid ProductVersionId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? FunctionalUnit,
    string? DeclaredUnit,
    string? SystemBoundary,
    string? AllocationMethod,
    string? AllocationReason,
    string? Exclusions,
    string? Assumptions,
    string? EstimationReason,
    Guid PcrVersionId);

/// <summary>Inventory project versions and their lifecycle stage declarations.</summary>
public interface IInventoryProjects
{
    /// <summary>Creates the next inventory version of a product, bound to a PCR, with stage declarations defaulted from that PCR.</summary>
    Task<OperationResult> CreateAsync(OperationContext context, InventoryInput input, CancellationToken cancellationToken);

    Task<OperationResult> SetStageApplicabilityAsync(
        OperationContext context, Guid stageDeclarationId, bool isApplicable, string? reason, uint rowVersion, CancellationToken cancellationToken);
}
