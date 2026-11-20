using CarbonFootprint.Application.Operations;

namespace CarbonFootprint.Application.Factors;

/// <summary>Synchronizes the current organization's factor catalogue from the official MOENV dataset.</summary>
public interface IFactorCatalogueSync
{
    Task<OperationResult> SynchronizeAsync(OperationContext context, CancellationToken cancellationToken);
}
