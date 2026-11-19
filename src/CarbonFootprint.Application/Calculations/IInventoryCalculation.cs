using CarbonFootprint.Application.Operations;

namespace CarbonFootprint.Application.Calculations;

/// <summary>Creates a new immutable calculation run for an editable inventory version.</summary>
public interface IInventoryCalculation
{
    Task<OperationResult> CalculateAsync(OperationContext context, Guid projectVersionId, CancellationToken cancellationToken);
}
