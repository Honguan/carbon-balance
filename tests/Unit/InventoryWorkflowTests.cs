using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Unit.Tests;

public sealed class InventoryWorkflowTests
{
    [Theory]
    [InlineData(InventoryWorkflowStatus.Draft, InventoryWorkflowStatus.Submitted, true)]
    [InlineData(InventoryWorkflowStatus.Submitted, InventoryWorkflowStatus.Approved, true)]
    [InlineData(InventoryWorkflowStatus.Submitted, InventoryWorkflowStatus.ChangesRequested, true)]
    [InlineData(InventoryWorkflowStatus.ChangesRequested, InventoryWorkflowStatus.Submitted, true)]
    [InlineData(InventoryWorkflowStatus.Draft, InventoryWorkflowStatus.Approved, false)]
    [InlineData(InventoryWorkflowStatus.Approved, InventoryWorkflowStatus.Draft, false)]
    public void TransitionMatrix_IsExplicit(
        InventoryWorkflowStatus current,
        InventoryWorkflowStatus next,
        bool expected)
    {
        Assert.Equal(expected, InventoryWorkflow.CanTransition(current, next));
    }

    [Theory]
    [InlineData(InventoryWorkflowStatus.Draft, true)]
    [InlineData(InventoryWorkflowStatus.ChangesRequested, true)]
    [InlineData(InventoryWorkflowStatus.Submitted, false)]
    [InlineData(InventoryWorkflowStatus.Approved, false)]
    public void AllowsEditing_OnlyForEditableStates(InventoryWorkflowStatus status, bool expected)
    {
        Assert.Equal(expected, InventoryWorkflow.AllowsEditing(status));
    }
}
