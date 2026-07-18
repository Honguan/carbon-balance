namespace CarbonFootprint.Domain.Modules.LegacyImport;

public enum LegacyParseStatus
{
    Pending,
    Parsed,
    Invalid,
    Conflict,
    Published
}

public sealed record LegacyStagingRow(
    Guid Id,
    Guid ImportBatchId,
    string SourceFile,
    string SourceTable,
    long SourceRowNumber,
    string RawPayloadJson,
    string RawSha256,
    LegacyParseStatus ParseStatus,
    string? ValidationErrorsJson);
