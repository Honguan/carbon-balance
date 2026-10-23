using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace CarbonFootprint.Infrastructure.Persistence;

/// <summary>Explicit safe field contract. Never enumerate all EF values or serialize persistence entities.</summary>
public static class AuditProjection
{
    private static readonly IReadOnlyDictionary<Type, string[]> Fields = new Dictionary<Type, string[]>
    {
        [typeof(OrganizationRecord)] = ["Id", "Name", "CreatedAt"],
        [typeof(OrganizationMailSettingsRecord)] = ["Id", "OrganizationId", "Host", "Port", "EnableSsl", "UpdatedAt", "UpdatedBy"],
        [typeof(OrganizationMembershipRecord)] = ["Id", "OrganizationId", "UserId", "Role", "CreatedAt", "RevokedAt"],
        [typeof(OrganizationInvitationRecord)] = ["Id", "OrganizationId", "Role", "InvitedBy", "CreatedAt", "ExpiresAt", "AcceptedAt", "RevokedAt"],
        [typeof(FacilityRecord)] = ["Id", "OrganizationId", "Code", "Name", "CreatedAt"],
        [typeof(ProductRecord)] = ["Id", "OrganizationId", "Name", "CategoryCode", "FacilityId", "CreatedAt"],
        [typeof(ProductVersionRecord)] = ["Id", "OrganizationId", "ProductId", "VersionNumber", "NameZhTw", "CreatedAt"],
        [typeof(InventoryProjectVersionRecord)] = ["Id", "OrganizationId", "ProductVersionId", "VersionNumber", "PeriodStart", "PeriodEnd", "FunctionalUnit", "DeclaredUnit", "SystemBoundary", "AllocationMethod", "PcrVersionId", "PcrVersion", "WorkflowStatus", "CreatedAt", "SubmittedAt", "ReviewedAt", "ReviewedBy"],
        [typeof(PcrVersionRecord)] = ["Id", "OrganizationId", "RuleSetId", "RegistrationNumber", "VersionNumber", "Title", "ApprovalDate", "ValidFrom", "ValidTo", "PublicationStatus", "StandardCode", "CccClassification", "ProductCategoryPatterns", "FunctionalUnitPattern", "PermittedAllocationMethodsCsv", "DeclaredUnitCode", "SystemBoundaryCode", "CutoffThresholdPercent", "FormulaRuleSetVersion", "RoundingDecimalPlaces", "IsCustomRule", "CustomApprovalStatus", "SupersedesVersionId", "DeprecatedAt", "ReviewStatus", "CreatedBy", "ReviewedBy", "ReviewedAt", "CreatedAt", "PublishedAt", "WithdrawnAt"],
        [typeof(PcrStageRuleRecord)] = ["Id", "OrganizationId", "PcrVersionId", "LifecycleStage", "Requirement", "PermittedActivityKindsCsv", "RequiredFieldsCsv"],
        [typeof(LifecycleStageDeclarationRecord)] = ["Id", "OrganizationId", "InventoryProjectVersionId", "LifecycleStage", "IsApplicable", "Reason"],
        [typeof(EmissionFactorVersionRecord)] = ["Id", "OrganizationId", "FactorId", "VersionNumber", "Name", "Value", "NumeratorUnitCode", "DenominatorUnitCode", "Geography", "ValidFrom", "ValidTo", "PublicationStatus", "SourceDatasetVersion", "LicenseCode", "SourceType", "DatasetName", "ReviewStatus", "ReviewedBy", "ReviewedAt", "PublishedAt", "WithdrawnAt", "SupersedesVersionId"],
        [typeof(ActivityDataRecord)] = ["Id", "OrganizationId", "InventoryProjectVersionId", "LifecycleStage", "Name", "ActivityKind", "EquipmentCategory", "DataSourceType", "CollectionMethod", "RawValue", "RawUnitCode", "CanonicalValue", "CanonicalUnitCode", "ConversionRuleVersion", "AmountFormulaId", "PeriodStart", "PeriodEnd", "FactorVersionId", "AllocationFactor", "IsEstimated", "DataQuality", "RetiredAt"],
        [typeof(EvidenceFileRecord)] = ["Id", "OrganizationId", "ActivityDataId", "ContentType", "SizeBytes", "ScanStatus", "CreatedAt"],
        [typeof(CalculationRunRecord)] = ["Id", "OrganizationId", "ProjectVersionId", "SupersedesRunId", "EngineBuild", "RuleSetVersion", "UnitCatalogueVersion", "GwpVersion", "PcrVersion", "ProductTotal", "CreatedAt"],
        [typeof(LegacyImportBatchRecord)] = ["Id", "OrganizationId", "EntityType", "Status", "ParsedRows", "InvalidRows", "ConflictRows", "CreatedAt", "CompletedAt"]
    };

    public static bool Supports(Type type) => Fields.ContainsKey(type);

    public static string ResourceType(Type type) => type == typeof(ActivityDataRecord) ? "ActivityDataVersion" : type.Name[..^"Record".Length];

    public static IReadOnlyDictionary<string, object?> Capture(EntityEntry entry, bool original = false)
    {
        object? Value(string field) => original ? entry.Property(field).OriginalValue : entry.Property(field).CurrentValue;
        var state = Fields[entry.Metadata.ClrType].ToDictionary(field => field, Value);
        if (entry.Entity is ActivityDataRecord)
        {
            using var formula = JsonDocument.Parse((string)Value("FormulaInputsJson")!);
            var inputs = new Dictionary<string, object?>();
            foreach (var field in new[] { "distanceKm", "weightKg", "lifetime", "frequency", "consumptionPerUse" })
                if (formula.RootElement.TryGetProperty(field, out var number)) inputs[field] = number.GetDecimal();
            state["FormulaInputs"] = inputs;
            state["EvidenceAttached"] = Value("EvidenceSha256") is not null;
        }
        return state;
    }
}
