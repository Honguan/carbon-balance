namespace CarbonFootprint.Domain.Modules.Inventories;

public enum InventoryWorkflowStatus
{
    Draft,
    Submitted,
    ChangesRequested,
    Approved
}

public static class InventoryWorkflow
{
    public static bool CanTransition(InventoryWorkflowStatus current, InventoryWorkflowStatus next) =>
        (current, next) switch
        {
            (InventoryWorkflowStatus.Draft, InventoryWorkflowStatus.Submitted) => true,
            (InventoryWorkflowStatus.ChangesRequested, InventoryWorkflowStatus.Submitted) => true,
            (InventoryWorkflowStatus.Submitted, InventoryWorkflowStatus.ChangesRequested) => true,
            (InventoryWorkflowStatus.Submitted, InventoryWorkflowStatus.Approved) => true,
            _ => false
        };

    public static void EnsureTransition(InventoryWorkflowStatus current, InventoryWorkflowStatus next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"盤查狀態不可由 {current} 轉為 {next}。");
        }
    }

    public static bool AllowsEditing(InventoryWorkflowStatus status) =>
        status is InventoryWorkflowStatus.Draft or InventoryWorkflowStatus.ChangesRequested;
}
