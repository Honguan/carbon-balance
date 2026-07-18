namespace CarbonFootprint.Domain.Modules.Audit;

public sealed record AuditEvent(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid? ActorId,
    Guid OrganizationId,
    string Action,
    string ResourceType,
    Guid ResourceId,
    string? BeforeHash,
    string? AfterHash,
    string CorrelationId,
    string MetadataJson);

