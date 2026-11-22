using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Application.Readiness;

/// <summary>Builds the readiness report and the calculation/PCR currency checks from persisted state.</summary>
public interface IInventoryReadiness
{
    Task<ReadinessReport> BuildReportAsync(
        Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken);

    /// <summary>Report and current-run verdict from one pass over the project, so callers needing both read it once.</summary>
    Task<ReadinessAssessment> AssessAsync(
        Guid projectVersionId, IReadOnlyList<ReadinessAcknowledgement> acknowledgements, CancellationToken cancellationToken);

    /// <summary>Why the latest calculation cannot be used for approval; null when it is current for the whole project.</summary>
    Task<string?> GetCurrentRunErrorAsync(Guid projectVersionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PcrRuleViolation>> ValidatePcrAsync(Guid projectVersionId, bool requireCompleteInventory, CancellationToken cancellationToken);
}

public sealed record ReadinessAssessment(ReadinessReport Report, string? CurrentRunError);
