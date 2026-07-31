namespace CarbonFootprint.Infrastructure.Persistence;

public static class GovernanceRecordTypes
{
    public const string DataQualityAssessment = "DataQualityAssessment";
    public const string AllocationPool = "AllocationPool";
    public const string AllocationResult = "AllocationResult";
    public const string TransportChain = "TransportChain";
    public const string TransportResult = "TransportResult";
    public const string ReadinessReport = "ReadinessReport";
    public const string ReadinessAcknowledgement = "ReadinessAcknowledgement";
    public const string ReviewCycle = "ReviewCycle";
    public const string ReviewFinding = "ReviewFinding";
    public const string VerificationRecord = "VerificationRecord";
    public const string ScenarioComparison = "ScenarioComparison";
    public const string DependencyReference = "DependencyReference";
}

public static class GovernanceDefinitionTypes
{
    public const string DataQualityRuleSet = "DataQualityRuleSet";
    public const string ActivityFormula = "ActivityFormula";
    public const string GlobalEmissionFactor = "GlobalEmissionFactor";
    public const string TransportRouteTemplate = "TransportRouteTemplate";
    public const string EvidenceRetentionPolicy = "EvidenceRetentionPolicy";
}

public sealed class GovernanceDefinitionRecord
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string DefinitionType { get; set; }
    public required string StableKey { get; set; }
    public int VersionNumber { get; set; }
    public required string Name { get; set; }
    public required string PublicationStatus { get; set; }
    public required string PayloadJson { get; set; }
    public required string CanonicalSha256 { get; set; }
    public string SourceStableId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceDatasetVersion { get; set; } = string.Empty;
    public string LicenseCode { get; set; } = string.Empty;
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public Guid? SourceEvidenceDocumentVersionId { get; set; }
    public Guid? SupersedesVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

public sealed class OrganizationDefinitionActivationRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DefinitionVersionId { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsProhibited { get; set; }
    public string DisplayAlias { get; set; } = string.Empty;
    public string InternalCategory { get; set; } = string.Empty;
    public string ApplicabilityNote { get; set; } = string.Empty;
    public string OverridePayloadJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class ProjectGovernanceRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectVersionId { get; set; }
    public Guid? TargetEntityId { get; set; }
    public required string RecordType { get; set; }
    public required string StableKey { get; set; }
    public int VersionNumber { get; set; }
    public required string Status { get; set; }
    public required string PayloadJson { get; set; }
    public required string CanonicalSha256 { get; set; }
    public bool IsImmutable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string LockReason { get; set; } = string.Empty;
}

public sealed class GovernanceEventRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectVersionId { get; set; }
    public required string EventType { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string PayloadJson { get; set; }
    public required string PayloadSha256 { get; set; }
    public Guid? ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class EvidenceDocumentRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Title { get; set; }
    public required string Category { get; set; }
    public DateOnly? CoverageStart { get; set; }
    public DateOnly? CoverageEnd { get; set; }
    public bool IsSensitive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public sealed class EvidenceDocumentVersionRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public required string OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string ObjectKey { get; set; }
    public string ObjectStorageVersion { get; set; } = string.Empty;
    public required string Sha256 { get; set; }
    public required string ScanStatus { get; set; }
    public string ScanEngine { get; set; } = string.Empty;
    public string ScanEngineVersion { get; set; } = string.Empty;
    public string ScanSignatureVersion { get; set; } = string.Empty;
    public string ScanDetails { get; set; } = string.Empty;
    public required string StorageStatus { get; set; }
    public Guid? ReplacesVersionId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public Guid? UploadedBy { get; set; }
}

public sealed class EvidenceLinkRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public Guid? LinkedBy { get; set; }
}

public sealed class EvidenceAccessLogRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public required string Action { get; set; }
    public Guid? ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string IpAddressHash { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class EvidenceRetentionLockRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public Guid? PolicyDefinitionVersionId { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public DateTimeOffset RetainUntil { get; set; }
    public required string Trigger { get; set; }
    public Guid? LockedBy { get; set; }
    public bool IsLegalHold { get; set; }
}

public sealed class VerificationArchiveRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectVersionId { get; set; }
    public Guid CalculationRunId { get; set; }
    public required string ExportSchemaVersion { get; set; }
    public required string ArchiveSha256 { get; set; }
    public required byte[] ArchiveBytes { get; set; }
    public required string FileIndexJson { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public Guid? GeneratedBy { get; set; }
}

public sealed class ProjectImpactRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectVersionId { get; set; }
    public required string ChangeType { get; set; }
    public required string DependencyType { get; set; }
    public required string DependencyKey { get; set; }
    public string PreviousVersion { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public decimal AffectedEmissions { get; set; }
    public string LifecycleStage { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
}
