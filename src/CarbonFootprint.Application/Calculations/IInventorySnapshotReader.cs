using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Application.Calculations;

public interface IInventorySnapshotReader
{
    Task<InventoryProjectSnapshot> ReadAsync(Guid projectVersionId, CancellationToken cancellationToken);
}
