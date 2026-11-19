using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Application.Governance;

/// <summary>Per-stage PCR rule as entered: requirement name and comma-separated activity kinds / required activity fields.</summary>
public sealed record PcrStageInput(LifecycleStage Stage, string? Requirement, string? Kinds, string? RequiredFields);

public sealed record PcrInput(
    string? RegistrationNumber,
    int VersionNumber,
    string? Title,
    DateOnly? ApprovalDate,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string? SourceReference,
    string? StandardCode,
    string? CccClassification,
    string? Applicability,
    string? RuleRequirements,
    string? ProductCategoryPatterns,
    string? FunctionalUnitPattern,
    string? DeclaredUnitCode,
    string? SystemBoundaryCode,
    string? PermittedAllocationMethods,
    decimal CutoffThresholdPercent,
    string? FormulaRuleSetVersion,
    int RoundingDecimalPlaces,
    string? ReportingRequirements,
    bool IsCustomRule,
    string? CustomRuleJustification,
    IReadOnlyList<PcrStageInput> Stages);

/// <summary>"__other__" selections carry their free-text value in the matching *Other field.</summary>
public sealed record FactorInput(
    string? Name,
    decimal? Value,
    string? DenominatorUnitCode,
    string? SourceType,
    string? SourceTypeOther,
    string? Geography,
    string? GeographyOther,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string? SourceDatasetVersion,
    string? LicenseCode,
    string? SourceName,
    string? SourceReference,
    string? DatasetName,
    string? OriginalDocumentName,
    string? Applicability);

public sealed record FactorSupersedeInput(
    decimal? Value, string? SourceDatasetVersion, string? SourceReference, string? OriginalDocumentName, string? Applicability);

/// <summary>PCR version review and publication transitions. Every call authorizes, validates, audits and saves atomically.</summary>
public interface IPcrGovernance
{
    /// <summary>Stores the original PCR document (malware-scanned) and creates a draft version with its stage rules.</summary>
    Task<OperationResult> CreateAsync(OperationContext context, PcrInput input, EvidenceUpload originalDocument, CancellationToken cancellationToken);
    Task<OperationResult> ReviewAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> RejectAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> PublishAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> WithdrawAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken);
}

/// <summary>Emission factor version review and publication transitions.</summary>
public interface IFactorGovernance
{
    Task<OperationResult> CreateAsync(OperationContext context, FactorInput input, CancellationToken cancellationToken);
    /// <summary>Creates the next draft version of a published factor; the current version stays published until the draft is published.</summary>
    Task<OperationResult> SupersedeAsync(
        OperationContext context, Guid factorVersionId, FactorSupersedeInput input, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> ReviewAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> PublishAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken);
    Task<OperationResult> WithdrawAsync(OperationContext context, Guid factorVersionId, uint rowVersion, CancellationToken cancellationToken);
}
