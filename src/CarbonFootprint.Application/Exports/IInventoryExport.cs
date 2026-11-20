using CarbonFootprint.Application.Operations;

namespace CarbonFootprint.Application.Exports;

public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary><see cref="OperationStatus.Conflict"/> means the stored run failed its integrity check and nothing was exported.</summary>
public sealed record ExportOutcome(OperationStatus Status, ExportFile? File = null);

public interface IInventoryExport
{
    /// <summary>Exports a run's immutable inputs and results, or the current activity data when no run exists yet.</summary>
    Task<ExportOutcome> ExportExcelAsync(OperationContext context, Guid projectVersionId, Guid? runId, CancellationToken cancellationToken);
}
