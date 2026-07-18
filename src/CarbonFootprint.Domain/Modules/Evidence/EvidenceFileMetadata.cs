namespace CarbonFootprint.Domain.Modules.Evidence;

public enum MalwareScanStatus
{
    Pending,
    Clean,
    Rejected,
    Failed
}

public sealed record EvidenceFileMetadata(
    Guid Id,
    Guid OrganizationId,
    string ObjectKey,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    MalwareScanStatus ScanStatus);

