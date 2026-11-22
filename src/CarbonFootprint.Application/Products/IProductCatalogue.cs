using CarbonFootprint.Application.Operations;

namespace CarbonFootprint.Application.Products;

/// <summary>Facilities and products of the current organization; a new product always starts with version 1.</summary>
public interface IProductCatalogue
{
    Task<OperationResult> CreateFacilityAsync(OperationContext context, string? code, string? name, CancellationToken cancellationToken);

    Task<OperationResult> CreateProductAsync(
        OperationContext context, string? name, string? categoryCode, Guid facilityId, CancellationToken cancellationToken);
}
