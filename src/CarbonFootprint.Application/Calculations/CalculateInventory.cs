using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Application.Calculations;

public sealed record CalculateInventoryCommand(
    Guid RunId,
    InventoryProjectSnapshot Snapshot,
    string EngineBuild,
    Guid? SupersedesRunId);

public interface ICalculationRunStore
{
    Task SaveAsync(CalculationRun run, CancellationToken cancellationToken);
}

public sealed class CalculateInventoryHandler
{
    private readonly CalculationEngine _engine;
    private readonly ICalculationRunStore _store;

    public CalculateInventoryHandler(CalculationEngine engine, ICalculationRunStore store)
    {
        _engine = engine;
        _store = store;
    }

    public async Task<CalculationRun> HandleAsync(
        CalculateInventoryCommand command,
        CancellationToken cancellationToken)
    {
        var run = _engine.Calculate(
            command.RunId,
            command.Snapshot,
            command.EngineBuild,
            command.SupersedesRunId);

        await _store.SaveAsync(run, cancellationToken);
        return run;
    }
}

