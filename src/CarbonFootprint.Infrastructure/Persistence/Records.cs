namespace CarbonFootprint.Infrastructure.Persistence;

public interface IOrganizationOwned
{
    Guid OrganizationId { get; }
}

public sealed class OrganizationRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    Guid IOrganizationOwned.OrganizationId => Id;
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OrganizationMembershipRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ProductRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ProductVersionRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProductId { get; set; }
    public int VersionNumber { get; set; }
    public required string NameZhTw { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InventoryProjectVersionRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProductVersionId { get; set; }
    public int VersionNumber { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public required string FunctionalUnit { get; set; }
    public Guid? PcrVersionId { get; set; }
    public required string PcrVersion { get; set; }
    public required string WorkflowStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? ReviewComment { get; set; }
}

public sealed class PcrVersionRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string RegistrationNumber { get; set; }
    public int VersionNumber { get; set; }
    public required string Title { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public required string PublicationStatus { get; set; }
    public required string SourceReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

public sealed class UnitRecord
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Symbol { get; set; }
    public required string Dimension { get; set; }
    public decimal ScaleToCanonical { get; set; }
    public decimal OffsetToCanonical { get; set; }
    public required string CanonicalCode { get; set; }
    public required string CatalogueVersion { get; set; }
}

public sealed class EmissionFactorVersionRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FactorId { get; set; }
    public int VersionNumber { get; set; }
    public required string Name { get; set; }
    public decimal Value { get; set; }
    public required string NumeratorUnitCode { get; set; }
    public required string DenominatorUnitCode { get; set; }
    public required string Geography { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public required string PublicationStatus { get; set; }
    public required string SourceDatasetVersion { get; set; }
    public required string LicenseCode { get; set; }
    public Guid? SupersedesVersionId { get; set; }
}

public sealed class ActivityDataRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid InventoryProjectVersionId { get; set; }
    public int LifecycleStage { get; set; }
    public required string Name { get; set; }
    public decimal RawValue { get; set; }
    public required string RawUnitCode { get; set; }
    public decimal CanonicalValue { get; set; }
    public required string CanonicalUnitCode { get; set; }
    public required string ConversionRuleVersion { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid FactorVersionId { get; set; }
    public string? EvidenceSha256 { get; set; }
}

public sealed class EvidenceFileRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ActivityDataId { get; set; }
    public required string ObjectKey { get; set; }
    public required string OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public required string ScanStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CalculationRunRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectVersionId { get; set; }
    public Guid? SupersedesRunId { get; set; }
    public required string CanonicalInputManifest { get; set; }
    public required string InputSha256 { get; set; }
    public required string EngineBuild { get; set; }
    public required string RuleSetVersion { get; set; }
    public required string UnitCatalogueVersion { get; set; }
    public required string GwpVersion { get; set; }
    public required string PcrVersion { get; set; }
    public decimal ProductTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CalculationLineRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CalculationRunId { get; set; }
    public Guid ActivityId { get; set; }
    public int LifecycleStage { get; set; }
    public required string FormulaId { get; set; }
    public decimal CanonicalActivityValue { get; set; }
    public required string ActivityUnitCode { get; set; }
    public Guid FactorVersionId { get; set; }
    public decimal FactorValue { get; set; }
    public required string FactorUnit { get; set; }
    public decimal Emissions { get; set; }
    public required string EmissionsUnitCode { get; set; }
}

public sealed class CalculationStageSummaryRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CalculationRunId { get; set; }
    public int LifecycleStage { get; set; }
    public decimal Emissions { get; set; }
}

public sealed class CalculationWarningRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CalculationRunId { get; set; }
    public required string Code { get; set; }
    public required string Message { get; set; }
}

public sealed class AuditEventRecord : IOrganizationOwned
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid? ActorId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }
    public required string CorrelationId { get; set; }
    public required string MetadataJson { get; set; }
}
