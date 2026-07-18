namespace CarbonFootprint.Domain.Modules.Reporting;

public sealed record CalculationReport(
    Guid CalculationRunId,
    string ApplicationVersion,
    string EngineVersion,
    DateTimeOffset GeneratedAt,
    string TimeZone,
    string Checksum,
    string WorkflowStatus,
    string VerificationStatus,
    string Disclaimer);

