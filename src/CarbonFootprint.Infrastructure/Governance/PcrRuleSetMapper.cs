using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Infrastructure.Persistence;

namespace CarbonFootprint.Infrastructure.Governance;

/// <summary>Maps persisted PCR rows and activity rows into the domain rule-engine inputs.</summary>
public static class PcrRuleSetMapper
{
    public static PcrRuleSetVersion ToRuleSet(PcrVersionRecord record, IEnumerable<PcrStageRuleRecord> stageRules)
    {
        var sourceVerified = !string.IsNullOrWhiteSpace(record.OriginalDocumentObjectKey)
            && string.Equals(record.OriginalDocumentScanStatus, "Clean", StringComparison.Ordinal)
            && SourceDocumentIntegrity.TryNormalizeSha256(record.OriginalDocumentSha256, out _);
        var publicationStatus = sourceVerified
            ? Enum.Parse<PcrPublicationStatus>(record.PublicationStatus)
            : PcrPublicationStatus.Draft;
        return new PcrRuleSetVersion(
            record.Id,
            record.RuleSetId,
            record.RegistrationNumber,
            record.VersionNumber,
            record.ProductCategoryPatterns,
            record.ApprovalDate,
            record.ValidFrom,
            record.ValidTo,
            publicationStatus,
            Enum.Parse<PcrReviewStatus>(record.ReviewStatus),
            record.FunctionalUnitPattern,
            record.DeclaredUnitCode,
            record.SystemBoundaryCode,
            SplitCsv(record.PermittedAllocationMethodsCsv).ToHashSet(StringComparer.OrdinalIgnoreCase),
            record.CutoffThresholdPercent,
            record.FormulaRuleSetVersion,
            record.RoundingDecimalPlaces,
            record.ReportingRequirements,
            record.IsCustomRule,
            record.CustomRuleJustification,
            Enum.Parse<PcrCustomApprovalStatus>(record.CustomApprovalStatus),
            record.DeprecatedAt,
            record.SupersedesVersionId,
            stageRules.Select(rule => new PcrLifecycleStageRule(
                    (LifecycleStage)rule.LifecycleStage,
                    Enum.Parse<PcrStageRequirement>(rule.Requirement),
                    SplitCsv(rule.PermittedActivityKindsCsv).Select(value => Enum.Parse<ActivityDataKind>(value)).ToHashSet(),
                    SplitCsv(rule.RequiredFieldsCsv).ToHashSet(StringComparer.Ordinal)))
                .OrderBy(rule => rule.Stage)
                .ToArray());
    }

    public static IReadOnlySet<string> PopulatedActivityFields(ActivityDataRecord activity)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(ActivityDataRecord.Name),
            nameof(ActivityDataRecord.RawValue),
            nameof(ActivityDataRecord.RawUnitCode),
            nameof(ActivityDataRecord.PeriodStart),
            nameof(ActivityDataRecord.PeriodEnd),
            nameof(ActivityDataRecord.FactorVersionId),
            nameof(ActivityDataRecord.AllocationFactor)
        };
        AddPopulated(nameof(ActivityDataRecord.SupplierOrScenario), activity.SupplierOrScenario);
        AddPopulated(nameof(ActivityDataRecord.EquipmentCategory), activity.EquipmentCategory);
        AddPopulated(nameof(ActivityDataRecord.DataSourceType), activity.DataSourceType);
        AddPopulated(nameof(ActivityDataRecord.DataProvider), activity.DataProvider);
        AddPopulated(nameof(ActivityDataRecord.CollectionMethod), activity.CollectionMethod);
        AddPopulated(nameof(ActivityDataRecord.SourceReference), activity.SourceReference);
        AddPopulated(nameof(ActivityDataRecord.DataQuality), activity.DataQuality);
        AddPopulated(nameof(ActivityDataRecord.EvidenceSha256), activity.EvidenceSha256);
        return fields;

        void AddPopulated(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) fields.Add(field);
        }
    }

    public static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}
