using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarbonFootprint.Application.Exports;
using CarbonFootprint.Domain.Modules.Allocations;
using CarbonFootprint.Domain.Modules.DataQuality;
using CarbonFootprint.Domain.Modules.Evidence;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Formulas;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Readiness;
using CarbonFootprint.Domain.Modules.Transport;
using CarbonFootprint.Domain.Modules.Verification;
using CarbonFootprint.Infrastructure.Evidence;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed record GovernanceOverview(
    IReadOnlyList<GovernanceDefinitionRecord> Definitions,
    IReadOnlyList<OrganizationDefinitionActivationRecord> Activations,
    IReadOnlyList<ProjectGovernanceRecord> ProjectRecords,
    IReadOnlyList<EvidenceDocumentRecord> EvidenceDocuments,
    IReadOnlyList<EvidenceDocumentVersionRecord> EvidenceVersions,
    IReadOnlyList<EvidenceLinkRecord> EvidenceLinks,
    IReadOnlyList<GovernanceEventRecord> Events,
    IReadOnlyList<VerificationArchiveRecord> Archives,
    IReadOnlyList<ProjectImpactRecord> Impacts);

public sealed record GlobalFactorDefinitionPayload(
    GlobalFactor Factor,
    GlobalFactorVersion Version,
    IReadOnlyList<GlobalFactorAlias> Aliases);

public sealed record DataQualityAssessmentPayload(
    DataQualityAssessmentVersion Assessment,
    decimal OverallScore,
    string AssessmentSha256,
    UncertaintyAnalysisResult? Uncertainty);

public sealed record AllocationGovernancePayload(
    AllocationPoolVersion Pool,
    AllocationResult Result);

public sealed record TransportGovernancePayload(
    TransportChainVersion Chain,
    TransportChainResult Result);

public sealed record ProjectComparisonPayload(
    Guid PreviousRunId,
    Guid CurrentRunId,
    ProjectVersionComparison Comparison);

public sealed class GovernanceWorkspaceService
{
    public const string ExportSchemaVersion = "verification-archive-v1";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly EvidenceStorageService _evidenceStorage;
    private readonly IOrganizationScope _organizationScope;

    public GovernanceWorkspaceService(
        CarbonFootprintDbContext dbContext,
        EvidenceStorageService evidenceStorage,
        IOrganizationScope organizationScope)
    {
        _dbContext = dbContext;
        _evidenceStorage = evidenceStorage;
        _organizationScope = organizationScope;
    }

    public async Task<GovernanceOverview> GetOverviewAsync(
        Guid? projectVersionId,
        CancellationToken cancellationToken)
    {
        var recordsQuery = _dbContext.ProjectGovernanceRecords.AsNoTracking();
        var eventsQuery = _dbContext.GovernanceEvents.AsNoTracking();
        var archivesQuery = _dbContext.VerificationArchives.AsNoTracking();
        var impactsQuery = _dbContext.ProjectImpacts.AsNoTracking();
        if (projectVersionId.HasValue)
        {
            recordsQuery = recordsQuery.Where(item => item.ProjectVersionId == projectVersionId.Value);
            eventsQuery = eventsQuery.Where(item => item.ProjectVersionId == projectVersionId.Value);
            archivesQuery = archivesQuery.Where(item => item.ProjectVersionId == projectVersionId.Value);
            impactsQuery = impactsQuery.Where(item => item.ProjectVersionId == projectVersionId.Value);
        }

        return new(
            await _dbContext.GovernanceDefinitions.AsNoTracking()
                .Where(item => item.OrganizationId == null || item.OrganizationId == CurrentOrganizationId())
                .OrderBy(item => item.DefinitionType)
                .ThenBy(item => item.StableKey)
                .ThenByDescending(item => item.VersionNumber)
                .ToArrayAsync(cancellationToken),
            await _dbContext.OrganizationDefinitionActivations.AsNoTracking()
                .OrderBy(item => item.DefinitionVersionId)
                .ToArrayAsync(cancellationToken),
            await recordsQuery.OrderBy(item => item.RecordType)
                .ThenBy(item => item.StableKey)
                .ThenByDescending(item => item.VersionNumber)
                .ToArrayAsync(cancellationToken),
            await _dbContext.EvidenceDocuments.AsNoTracking()
                .OrderByDescending(item => item.CreatedAt)
                .ToArrayAsync(cancellationToken),
            await _dbContext.EvidenceDocumentVersions.AsNoTracking()
                .OrderByDescending(item => item.UploadedAt)
                .ToArrayAsync(cancellationToken),
            await _dbContext.EvidenceLinks.AsNoTracking()
                .OrderByDescending(item => item.LinkedAt)
                .ToArrayAsync(cancellationToken),
            await eventsQuery.OrderByDescending(item => item.OccurredAt)
                .Take(250)
                .ToArrayAsync(cancellationToken),
            await archivesQuery.OrderByDescending(item => item.GeneratedAt)
                .ToArrayAsync(cancellationToken),
            await impactsQuery.OrderByDescending(item => item.DetectedAt)
                .ToArrayAsync(cancellationToken));
    }

    public async Task<GovernanceDefinitionRecord> SaveDefinitionAsync(
        string definitionType,
        string stableKey,
        string name,
        string payloadJson,
        Guid? organizationId,
        string sourceStableId,
        string sourceName,
        string sourceUrl,
        string sourceDatasetVersion,
        string licenseCode,
        DateOnly? validFrom,
        DateOnly? validTo,
        Guid? sourceEvidenceDocumentVersionId,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        definitionType = Required(definitionType, nameof(definitionType), 100);
        stableKey = Required(stableKey, nameof(stableKey), 300);
        name = Required(name, nameof(name), 500);
        var canonicalPayload = CanonicalizeJson(payloadJson);
        ValidateDefinitionPayload(definitionType, canonicalPayload);
        if (validFrom is not null && validTo is not null && validFrom > validTo)
        {
            throw new InvalidOperationException("Definition validity start cannot be later than the end date.");
        }

        if (organizationId.HasValue && organizationId != CurrentOrganizationId())
        {
            throw new InvalidOperationException("Definition organization does not match the current organization.");
        }

        var latest = await _dbContext.GovernanceDefinitions
            .Where(item => item.DefinitionType == definitionType
                && item.StableKey == stableKey
                && item.OrganizationId == organizationId)
            .OrderByDescending(item => item.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var definition = new GovernanceDefinitionRecord
        {
            Id = Guid.NewGuid(),
            DefinitionId = latest?.DefinitionId ?? Guid.NewGuid(),
            OrganizationId = organizationId,
            DefinitionType = definitionType,
            StableKey = stableKey,
            VersionNumber = (latest?.VersionNumber ?? 0) + 1,
            Name = name,
            PublicationStatus = "Draft",
            PayloadJson = canonicalPayload,
            CanonicalSha256 = Sha256(canonicalPayload),
            SourceStableId = sourceStableId?.Trim() ?? string.Empty,
            SourceName = sourceName?.Trim() ?? string.Empty,
            SourceUrl = sourceUrl?.Trim() ?? string.Empty,
            SourceDatasetVersion = sourceDatasetVersion?.Trim() ?? string.Empty,
            LicenseCode = licenseCode?.Trim() ?? string.Empty,
            ValidFrom = validFrom,
            ValidTo = validTo,
            SourceEvidenceDocumentVersionId = sourceEvidenceDocumentVersionId,
            SupersedesVersionId = latest?.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = actorId
        };
        _dbContext.GovernanceDefinitions.Add(definition);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return definition;
    }

    public async Task<GovernanceDefinitionRecord> PublishDefinitionAsync(
        Guid definitionVersionId,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var definition = await _dbContext.GovernanceDefinitions
            .SingleAsync(item => item.Id == definitionVersionId, cancellationToken);
        if (!string.Equals(definition.PublicationStatus, "Draft", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only draft definition versions can be published.");
        }

        ValidateDefinitionPayload(definition.DefinitionType, definition.PayloadJson);
        if (definition.DefinitionType == GovernanceDefinitionTypes.GlobalEmissionFactor)
        {
            if (definition.SourceEvidenceDocumentVersionId is null)
            {
                throw new InvalidOperationException("Manual or official factor publication requires an original source evidence document.");
            }

            await RequireUsableEvidenceVersionAsync(definition.SourceEvidenceDocumentVersionId.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(definition.SourceUrl)
                || string.IsNullOrWhiteSpace(definition.SourceDatasetVersion)
                || string.IsNullOrWhiteSpace(definition.LicenseCode))
            {
                throw new InvalidOperationException("Factor publication requires source URL, dataset version and license.");
            }
        }

        definition.PublicationStatus = "Published";
        definition.PublishedAt = DateTimeOffset.UtcNow;
        if (definition.SupersedesVersionId is not null)
        {
            var previous = await _dbContext.GovernanceDefinitions
                .SingleOrDefaultAsync(item => item.Id == definition.SupersedesVersionId.Value, cancellationToken);
            if (previous is not null && string.Equals(previous.PublicationStatus, "Published", StringComparison.Ordinal))
            {
                previous.PublicationStatus = "Superseded";
                previous.WithdrawnAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return definition;
    }

    public async Task WithdrawDefinitionAsync(
        Guid definitionVersionId,
        string reason,
        CancellationToken cancellationToken)
    {
        _ = Required(reason, nameof(reason), 2000);
        var definition = await _dbContext.GovernanceDefinitions
            .SingleAsync(item => item.Id == definitionVersionId, cancellationToken);
        if (string.Equals(definition.PublicationStatus, "Draft", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Draft definitions should be deleted or replaced instead of withdrawn.");
        }

        definition.PublicationStatus = "Withdrawn";
        definition.WithdrawnAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await DetectDefinitionImpactsAsync(definition, reason, cancellationToken);
    }

    public async Task<GlobalFactorSyncResult> SynchronizeGlobalFactorsAsync(
        IReadOnlyList<OfficialFactorSourceRecord> sourceRecords,
        string batchSha256,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.GovernanceDefinitions
            .Where(item => item.DefinitionType == GovernanceDefinitionTypes.GlobalEmissionFactor)
            .OrderBy(item => item.StableKey)
            .ThenBy(item => item.VersionNumber)
            .ToArrayAsync(cancellationToken);
        var factors = new List<GlobalFactor>();
        var versions = new List<GlobalFactorVersion>();
        var aliases = new List<GlobalFactorAlias>();
        foreach (var item in existing)
        {
            var payload = Deserialize<GlobalFactorDefinitionPayload>(item.PayloadJson);
            if (factors.All(factor => factor.Id != payload.Factor.Id))
            {
                factors.Add(payload.Factor);
            }
            versions.Add(payload.Version);
            aliases.AddRange(payload.Aliases.Where(alias => aliases.All(existingAlias => existingAlias.Id != alias.Id)));
        }

        var batches = await _dbContext.GovernanceDefinitions
            .Where(item => item.DefinitionType == "GlobalFactorImportBatch")
            .Select(item => item.PayloadJson)
            .ToArrayAsync(cancellationToken);
        var snapshot = new GlobalFactorCatalogSnapshot(
            factors,
            versions,
            aliases,
            batches.Select(Deserialize<GlobalFactorImportBatch>).ToArray());
        var result = GlobalFactorCatalogService.Synchronize(snapshot, sourceRecords, batchSha256, DateTimeOffset.UtcNow);

        foreach (var versionId in result.AddedVersionIds)
        {
            var version = result.Catalog.Versions.Single(item => item.Id == versionId);
            var factor = result.Catalog.Factors.Single(item => item.Id == version.GlobalFactorId);
            var factorAliases = result.Catalog.Aliases.Where(item => item.GlobalFactorId == factor.Id).ToArray();
            var payload = SerializeCanonical(new GlobalFactorDefinitionPayload(factor, version, factorAliases));
            _dbContext.GovernanceDefinitions.Add(new GovernanceDefinitionRecord
            {
                Id = version.Id,
                DefinitionId = factor.Id,
                OrganizationId = null,
                DefinitionType = GovernanceDefinitionTypes.GlobalEmissionFactor,
                StableKey = factor.StableSourceKey,
                VersionNumber = version.VersionNumber,
                Name = factor.OriginalName,
                PublicationStatus = version.Status.ToString(),
                PayloadJson = payload,
                CanonicalSha256 = Sha256(payload),
                SourceStableId = factor.StableSourceKey,
                SourceName = factor.SourceOrganization,
                SourceUrl = version.SourceUrl,
                SourceDatasetVersion = version.DatasetVersion,
                LicenseCode = version.License,
                ValidFrom = version.ValidFrom,
                ValidTo = version.ValidTo,
                SupersedesVersionId = version.SupersedesVersionId,
                CreatedAt = version.ImportedAt,
                CreatedBy = actorId,
                PublishedAt = version.Status == GlobalFactorVersionStatus.Published ? version.ImportedAt : null,
                WithdrawnAt = version.Status is GlobalFactorVersionStatus.Withdrawn or GlobalFactorVersionStatus.RemovedFromSource
                    ? version.ImportedAt
                    : null
            });
        }

        foreach (var versionId in result.SupersededVersionIds.Concat(result.WithdrawnVersionIds).Concat(result.RemovedVersionIds))
        {
            var stored = await _dbContext.GovernanceDefinitions.SingleAsync(item => item.Id == versionId, cancellationToken);
            var currentVersion = result.Catalog.Versions.Single(item => item.Id == versionId);
            stored.PublicationStatus = currentVersion.Status.ToString();
            stored.WithdrawnAt = DateTimeOffset.UtcNow;
        }

        var batchPayload = SerializeCanonical(result.Batch);
        var batchExists = await _dbContext.GovernanceDefinitions.AsNoTracking()
            .AnyAsync(item => item.DefinitionType == "GlobalFactorImportBatch"
                && item.StableKey == result.Batch.BatchSha256, cancellationToken);
        if (!batchExists)
        {
            _dbContext.GovernanceDefinitions.Add(new GovernanceDefinitionRecord
            {
                Id = result.Batch.Id,
                DefinitionId = result.Batch.Id,
                OrganizationId = null,
                DefinitionType = "GlobalFactorImportBatch",
                StableKey = result.Batch.BatchSha256,
                VersionNumber = 1,
                Name = $"{result.Batch.SourceOrganization} {result.Batch.DatasetVersion}",
                PublicationStatus = "Published",
                PayloadJson = batchPayload,
                CanonicalSha256 = Sha256(batchPayload),
                SourceStableId = result.Batch.BatchSha256,
                SourceName = result.Batch.SourceOrganization,
                SourceDatasetVersion = result.Batch.DatasetVersion,
                CreatedAt = result.Batch.ImportedAt,
                CreatedBy = actorId,
                PublishedAt = result.Batch.ImportedAt
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task ActivateDefinitionAsync(
        Guid definitionVersionId,
        bool enabled,
        bool prohibited,
        string displayAlias,
        string internalCategory,
        string applicabilityNote,
        string overridePayloadJson,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var definition = await _dbContext.GovernanceDefinitions.AsNoTracking()
            .SingleAsync(item => item.Id == definitionVersionId, cancellationToken);
        if (!string.Equals(definition.PublicationStatus, "Published", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only published definition versions can be activated.");
        }

        var activation = await _dbContext.OrganizationDefinitionActivations
            .SingleOrDefaultAsync(item => item.DefinitionVersionId == definitionVersionId, cancellationToken);
        if (activation is null)
        {
            activation = new OrganizationDefinitionActivationRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = CurrentOrganizationId(),
                DefinitionVersionId = definitionVersionId
            };
            _dbContext.OrganizationDefinitionActivations.Add(activation);
        }

        activation.IsEnabled = enabled;
        activation.IsProhibited = prohibited;
        activation.DisplayAlias = displayAlias?.Trim() ?? string.Empty;
        activation.InternalCategory = internalCategory?.Trim() ?? string.Empty;
        activation.ApplicabilityNote = applicabilityNote?.Trim() ?? string.Empty;
        activation.OverridePayloadJson = CanonicalizeJson(overridePayloadJson);
        activation.UpdatedAt = DateTimeOffset.UtcNow;
        activation.UpdatedBy = actorId;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectGovernanceRecord> SaveDataQualityAssessmentAsync(
        Guid projectVersionId,
        Guid activityId,
        Guid ruleSetDefinitionVersionId,
        string assessmentJson,
        string uncertaintyInputsJson,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        await RequireProjectAndActivityAsync(projectVersionId, activityId, cancellationToken);
        var definition = await RequirePublishedDefinitionAsync(
            ruleSetDefinitionVersionId,
            GovernanceDefinitionTypes.DataQualityRuleSet,
            cancellationToken);
        var rules = Deserialize<DataQualityRuleSetVersion>(definition.PayloadJson);
        var assessment = Deserialize<DataQualityAssessmentVersion>(assessmentJson);
        if (assessment.ActivityId != activityId || assessment.RuleSetVersionId != rules.Id)
        {
            throw new InvalidOperationException("Data-quality assessment activity or rule-set version does not match the selected records.");
        }

        var overallScore = assessment.CalculateOverallScore(rules);
        UncertaintyAnalysisResult? uncertainty = null;
        if (!string.IsNullOrWhiteSpace(uncertaintyInputsJson)
            && JsonDocument.Parse(uncertaintyInputsJson).RootElement.ValueKind != JsonValueKind.Null)
        {
            var inputs = Deserialize<IReadOnlyList<UncertaintyInput>>(uncertaintyInputsJson);
            uncertainty = UncertaintyAnalysisService.Analyze(inputs);
        }

        var payload = new DataQualityAssessmentPayload(
            assessment,
            overallScore,
            assessment.CreateCanonicalHash(overallScore),
            uncertainty);
        var record = await SaveProjectRecordAsync(
            projectVersionId,
            GovernanceRecordTypes.DataQualityAssessment,
            activityId.ToString("D"),
            activityId,
            "Approved",
            SerializeCanonical(payload),
            true,
            actorId,
            cancellationToken);
        var activity = await _dbContext.ActivityData.SingleAsync(item => item.Id == activityId, cancellationToken);
        activity.DataQualityGovernanceRecordId = record.Id;
        activity.DataQuality = assessment.SourceCategory.ToString();
        activity.GovernanceTraceJson = MergeTrace(activity.GovernanceTraceJson, "dataQuality", record.Id, record.CanonicalSha256);
        await InvalidateProjectCalculationAsync(projectVersionId, actorId, "data-quality.changed", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<ProjectGovernanceRecord> SaveAllocationPoolAsync(
        Guid projectVersionId,
        string poolJson,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var pool = Deserialize<AllocationPoolVersion>(poolJson);
        if (pool.OrganizationId != CurrentOrganizationId())
        {
            throw new InvalidOperationException("Allocation pool organization does not match the current organization.");
        }

        await RequireProjectAndActivityAsync(projectVersionId, pool.SourceActivityId, cancellationToken);
        var result = AllocationPoolCalculator.Calculate(pool, pool.CreatedAt);
        var payload = new AllocationGovernancePayload(pool, result);
        var record = await SaveProjectRecordAsync(
            projectVersionId,
            GovernanceRecordTypes.AllocationPool,
            pool.PoolId.ToString("D"),
            pool.SourceActivityId,
            pool.Status.ToString(),
            SerializeCanonical(payload),
            pool.IsImmutable,
            actorId,
            cancellationToken);
        var activity = await _dbContext.ActivityData.SingleAsync(item => item.Id == pool.SourceActivityId, cancellationToken);
        var share = result.Shares.SingleOrDefault(item => item.ProductVersionId ==
            _dbContext.InventoryProjectVersions.Where(project => project.Id == projectVersionId)
                .Select(project => project.ProductVersionId).Single());
        if (share is not null)
        {
            activity.AllocationFactor = share.Share;
        }
        activity.AllocationGovernanceRecordId = record.Id;
        activity.GovernanceTraceJson = MergeTrace(activity.GovernanceTraceJson, "allocation", record.Id, record.CanonicalSha256);
        await InvalidateProjectCalculationAsync(projectVersionId, actorId, "allocation.changed", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<ProjectGovernanceRecord> SaveTransportChainAsync(
        Guid projectVersionId,
        string chainJson,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var chain = Deserialize<TransportChainVersion>(chainJson);
        if (chain.OrganizationId != CurrentOrganizationId() || chain.ProjectVersionId != projectVersionId)
        {
            throw new InvalidOperationException("Transport chain scope does not match the selected project.");
        }

        await RequireProjectAndActivityAsync(projectVersionId, chain.ActivityId, cancellationToken);
        var result = TransportChainCalculator.Calculate(chain);
        var payload = new TransportGovernancePayload(chain, result);
        var record = await SaveProjectRecordAsync(
            projectVersionId,
            GovernanceRecordTypes.TransportChain,
            chain.ChainId.ToString("D"),
            chain.ActivityId,
            "Approved",
            SerializeCanonical(payload),
            true,
            actorId,
            cancellationToken);
        var activity = await _dbContext.ActivityData.SingleAsync(item => item.Id == chain.ActivityId, cancellationToken);
        activity.TransportGovernanceRecordId = record.Id;
        activity.GovernanceTraceJson = MergeTrace(activity.GovernanceTraceJson, "transport", record.Id, record.CanonicalSha256);
        await InvalidateProjectCalculationAsync(projectVersionId, actorId, "transport.changed", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task BindFormulaAsync(
        Guid projectVersionId,
        Guid activityId,
        Guid formulaDefinitionVersionId,
        string formulaValuesJson,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        await RequireProjectAndActivityAsync(projectVersionId, activityId, cancellationToken);
        var definition = await RequirePublishedDefinitionAsync(
            formulaDefinitionVersionId,
            GovernanceDefinitionTypes.ActivityFormula,
            cancellationToken);
        var formula = Deserialize<ActivityFormulaDefinitionVersion>(definition.PayloadJson);
        if (!formula.IsSelectable)
        {
            throw new InvalidOperationException("Unsupported or withdrawn formula versions cannot be selected.");
        }

        _ = CanonicalizeJson(formulaValuesJson);
        var activity = await _dbContext.ActivityData.SingleAsync(item => item.Id == activityId, cancellationToken);
        activity.FormulaDefinitionVersionId = formulaDefinitionVersionId;
        activity.FormulaInputsJson = CanonicalizeJson(formulaValuesJson);
        activity.GovernanceTraceJson = MergeTrace(activity.GovernanceTraceJson, "formula", definition.Id, definition.CanonicalSha256);
        await InvalidateProjectCalculationAsync(projectVersionId, actorId, "formula.changed", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BindGlobalFactorAsync(
        Guid projectVersionId,
        Guid activityId,
        Guid globalFactorDefinitionVersionId,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        await RequireProjectAndActivityAsync(projectVersionId, activityId, cancellationToken);
        var definition = await RequirePublishedDefinitionAsync(
            globalFactorDefinitionVersionId,
            GovernanceDefinitionTypes.GlobalEmissionFactor,
            cancellationToken);
        var activation = await _dbContext.OrganizationDefinitionActivations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DefinitionVersionId == definition.Id, cancellationToken);
        if (activation is null || !activation.IsEnabled || activation.IsProhibited)
        {
            throw new InvalidOperationException("Global factor is not enabled for the current organization.");
        }

        var activity = await _dbContext.ActivityData.SingleAsync(item => item.Id == activityId, cancellationToken);
        activity.FactorVersionId = null;
        activity.GlobalFactorDefinitionVersionId = definition.Id;
        activity.GovernanceTraceJson = MergeTrace(activity.GovernanceTraceJson, "globalFactor", definition.Id, definition.CanonicalSha256);
        await InvalidateProjectCalculationAsync(projectVersionId, actorId, "factor.changed", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(EvidenceDocumentRecord Document, EvidenceDocumentVersionRecord Version)> RegisterEvidenceAsync(
        Guid? existingDocumentId,
        string title,
        EvidenceCategory category,
        DateOnly? coverageStart,
        DateOnly? coverageEnd,
        bool isSensitive,
        StoredEvidence stored,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        if (coverageStart is not null && coverageEnd is not null && coverageStart > coverageEnd)
        {
            throw new InvalidOperationException("Evidence coverage start cannot be later than the end date.");
        }
        if (stored.ScanStatus != MalwareScanStatus.Clean || stored.Sha256.Length != 64)
        {
            throw new InvalidOperationException("Evidence must have a server-computed SHA-256 and a clean malware scan.");
        }

        EvidenceDocumentRecord document;
        EvidenceDocumentVersionRecord? previous = null;
        if (existingDocumentId.HasValue)
        {
            document = await _dbContext.EvidenceDocuments
                .SingleAsync(item => item.Id == existingDocumentId.Value, cancellationToken);
            previous = await _dbContext.EvidenceDocumentVersions
                .Where(item => item.DocumentId == document.Id)
                .OrderByDescending(item => item.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);
            var locked = previous is not null && await _dbContext.EvidenceRetentionLocks
                .AnyAsync(item => item.DocumentVersionId == previous.Id, cancellationToken);
            if (locked)
            {
                throw new InvalidOperationException("Retention-locked evidence cannot be replaced. Create a separate logical document.");
            }
        }
        else
        {
            document = new EvidenceDocumentRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = CurrentOrganizationId(),
                Title = Required(title, nameof(title), 500),
                Category = category.ToString(),
                CoverageStart = coverageStart,
                CoverageEnd = coverageEnd,
                IsSensitive = isSensitive,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = actorId
            };
            _dbContext.EvidenceDocuments.Add(document);
        }

        var duplicate = await _dbContext.EvidenceDocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == stored.Sha256
                && item.SizeBytes == stored.SizeBytes
                && (item.StorageStatus == "Available" || item.StorageStatus == "RetentionLocked"), cancellationToken);
        if (duplicate is not null && !string.Equals(duplicate.ObjectKey, stored.ObjectKey, StringComparison.Ordinal))
        {
            await _evidenceStorage.DeleteAsync(stored.ObjectKey, cancellationToken);
        }

        var version = new EvidenceDocumentVersionRecord
        {
            Id = stored.Id,
            OrganizationId = CurrentOrganizationId(),
            DocumentId = document.Id,
            VersionNumber = (previous?.VersionNumber ?? 0) + 1,
            OriginalFileName = stored.OriginalFileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            ObjectKey = duplicate?.ObjectKey ?? stored.ObjectKey,
            ObjectStorageVersion = duplicate?.ObjectStorageVersion ?? string.Empty,
            Sha256 = stored.Sha256,
            ScanStatus = "Clean",
            ScanEngine = "ClamAV",
            ScanEngineVersion = "configured-service",
            ScanSignatureVersion = "runtime",
            ScanDetails = "Server-side scan passed before object availability.",
            StorageStatus = "Available",
            ReplacesVersionId = previous?.Id,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedBy = actorId
        };
        _dbContext.EvidenceDocumentVersions.Add(version);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (document, version);
    }

    public async Task LinkEvidenceAsync(
        Guid documentVersionId,
        EvidenceLinkTargetType targetType,
        Guid targetId,
        string purpose,
        bool isRequired,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        await RequireUsableEvidenceVersionAsync(documentVersionId, cancellationToken);
        var exists = await _dbContext.EvidenceLinks.AnyAsync(item =>
            item.DocumentVersionId == documentVersionId
            && item.TargetType == targetType.ToString()
            && item.TargetId == targetId,
            cancellationToken);
        if (!exists)
        {
            _dbContext.EvidenceLinks.Add(new EvidenceLinkRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = CurrentOrganizationId(),
                DocumentVersionId = documentVersionId,
                TargetType = targetType.ToString(),
                TargetId = targetId,
                Purpose = purpose?.Trim() ?? string.Empty,
                IsRequired = isRequired,
                LinkedAt = DateTimeOffset.UtcNow,
                LinkedBy = actorId
            });
        }
        _dbContext.EvidenceAccessLogs.Add(new EvidenceAccessLogRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = CurrentOrganizationId(),
            DocumentVersionId = documentVersionId,
            Action = EvidenceAccessAction.Link.ToString(),
            ActorId = actorId,
            OccurredAt = DateTimeOffset.UtcNow,
            Reason = purpose?.Trim() ?? string.Empty
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<byte[]> DownloadEvidenceAsync(
        Guid documentVersionId,
        Guid? actorId,
        string ipAddressHash,
        CancellationToken cancellationToken)
    {
        var version = await RequireUsableEvidenceVersionAsync(documentVersionId, cancellationToken);
        var bytes = await _evidenceStorage.ReadAsync(version.ObjectKey, cancellationToken);
        if (!string.Equals(Sha256(bytes), version.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Evidence bytes no longer match the recorded SHA-256.");
        }
        _dbContext.EvidenceAccessLogs.Add(new EvidenceAccessLogRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = CurrentOrganizationId(),
            DocumentVersionId = version.Id,
            Action = EvidenceAccessAction.Download.ToString(),
            ActorId = actorId,
            OccurredAt = DateTimeOffset.UtcNow,
            IpAddressHash = ipAddressHash,
            Reason = "authorized-download"
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return bytes;
    }

    public async Task<InventoryReadinessReport> BuildReadinessReportAsync(
        Guid projectVersionId,
        IReadOnlySet<string> acknowledgements,
        Guid? actorId,
        bool persist,
        CancellationToken cancellationToken)
    {
        var project = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .SingleAsync(item => item.Id == projectVersionId, cancellationToken);
        var pcr = project.PcrVersionId is null
            ? null
            : await _dbContext.PcrVersions.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == project.PcrVersionId.Value, cancellationToken);
        var stages = await _dbContext.LifecycleStageDeclarations.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == projectVersionId)
            .ToArrayAsync(cancellationToken);
        var activities = await _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == projectVersionId)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var latestRun = await _dbContext.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var lines = latestRun is null
            ? []
            : await _dbContext.CalculationLineItems.AsNoTracking()
                .Where(item => item.CalculationRunId == latestRun.Id)
                .ToArrayAsync(cancellationToken);
        var evidenceLinks = await _dbContext.EvidenceLinks.AsNoTracking()
            .Where(item => item.TargetType == EvidenceLinkTargetType.Activity.ToString()
                && activities.Select(activity => activity.Id).Contains(item.TargetId))
            .ToArrayAsync(cancellationToken);
        var evidenceIds = evidenceLinks.Select(item => item.DocumentVersionId).Distinct().ToArray();
        var evidenceVersions = await _dbContext.EvidenceDocumentVersions.AsNoTracking()
            .Where(item => evidenceIds.Contains(item.Id))
            .ToArrayAsync(cancellationToken);
        var retainedIds = await _dbContext.EvidenceRetentionLocks.AsNoTracking()
            .Where(item => evidenceIds.Contains(item.DocumentVersionId))
            .Select(item => item.DocumentVersionId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var localFactorIds = activities.Where(item => item.FactorVersionId.HasValue)
            .Select(item => item.FactorVersionId!.Value).Distinct().ToArray();
        var localFactors = await _dbContext.EmissionFactorVersions.AsNoTracking()
            .Where(item => localFactorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var globalFactorIds = activities.Where(item => item.GlobalFactorDefinitionVersionId.HasValue)
            .Select(item => item.GlobalFactorDefinitionVersionId!.Value).Distinct().ToArray();
        var globalFactors = await _dbContext.GovernanceDefinitions.AsNoTracking()
            .Where(item => globalFactorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var projectRecords = await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .ToArrayAsync(cancellationToken);
        var allocations = projectRecords
            .Where(item => item.RecordType == GovernanceRecordTypes.AllocationPool)
            .Select(item => Deserialize<AllocationGovernancePayload>(item.PayloadJson))
            .Select(item => new ReadinessAllocationContext(
                item.Pool.Id,
                item.Result.ShareTotal,
                0.000001m,
                item.Result.Denominator > 0m && !string.IsNullOrWhiteSpace(item.Pool.CalculationBasis),
                item.Pool.Outputs.SelectMany(output => output.EvidenceDocumentVersionIds).Any()))
            .ToArray();
        var formulaDefinitions = activities.Where(item => item.FormulaDefinitionVersionId.HasValue)
            .Select(item => item.FormulaDefinitionVersionId!.Value).Distinct().ToArray();
        var formulaStatus = formulaDefinitions.Length == 0 || await _dbContext.GovernanceDefinitions.AsNoTracking()
            .Where(item => formulaDefinitions.Contains(item.Id))
            .AllAsync(item => item.PublicationStatus == "Published", cancellationToken);

        var readinessActivities = new List<ReadinessActivityContext>();
        foreach (var activity in activities)
        {
            var factor = await BuildFactorReadinessAsync(activity, localFactors, globalFactors, cancellationToken);
            var activityEvidence = evidenceLinks.Where(link => link.TargetId == activity.Id)
                .Select(link =>
                {
                    var version = evidenceVersions.Single(item => item.Id == link.DocumentVersionId);
                    return new ReadinessEvidenceContext(
                        version.Id,
                        link.IsRequired,
                        version.Sha256.Length == 64,
                        version.ScanStatus == "Clean",
                        retainedIds.Contains(version.Id) || version.StorageStatus is "Available" or "RetentionLocked");
                })
                .ToArray();
            var line = lines.SingleOrDefault(item => item.ActivityId == activity.Id);
            readinessActivities.Add(new(
                activity.Id,
                (LifecycleStage)activity.LifecycleStage,
                activity.DataProvider,
                activity.PeriodStart,
                activity.PeriodEnd,
                line?.Emissions ?? 0m,
                activity.IsEstimated,
                activity.EstimationReason,
                false,
                string.Empty,
                factor,
                activityEvidence));
        }

        var requiredStages = stages.Where(item => item.IsApplicable)
            .Select(item => (LifecycleStage)item.LifecycleStage)
            .ToHashSet();
        var pcrAvailable = pcr is not null
            && pcr.PublicationStatus == "Published"
            && pcr.ReviewStatus == "Approved"
            && pcr.WithdrawnAt is null
            && (pcr.ValidFrom is null || pcr.ValidFrom <= project.PeriodEnd)
            && (pcr.ValidTo is null || pcr.ValidTo >= project.PeriodEnd);
        var pcrCompatible = pcr is not null
            && MatchesControlled(pcr.DeclaredUnitCode, project.DeclaredUnit)
            && MatchesControlled(pcr.SystemBoundaryCode, project.SystemBoundary)
            && CsvContainsOrEmpty(pcr.PermittedAllocationMethodsCsv, project.AllocationMethod);
        var context = new InventoryReadinessContext(
            projectVersionId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            project.PeriodStart,
            project.PeriodEnd,
            5 * 365,
            pcrAvailable,
            pcrCompatible,
            requiredStages,
            readinessActivities,
            allocations,
            0.20m,
            project.Assumptions,
            project.Exclusions,
            latestRun is not null,
            latestRun is not null && string.Equals(
                latestRun.InputSha256,
                await BuildCurrentManifestHashAsync(projectVersionId, cancellationToken),
                StringComparison.Ordinal),
            formulaStatus,
            acknowledgements);
        var report = InventoryReadinessValidator.Validate(context);
        if (persist)
        {
            var payload = SerializeCanonical(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                acknowledgements = acknowledgements.OrderBy(item => item, StringComparer.Ordinal),
                canSubmit = report.CanSubmit(acknowledgements),
                results = report.Results
            });
            await SaveProjectRecordAsync(
                projectVersionId,
                GovernanceRecordTypes.ReadinessReport,
                "latest",
                projectVersionId,
                report.CanSubmit(acknowledgements) ? "Passed" : "Failed",
                payload,
                true,
                actorId,
                cancellationToken);
        }
        return report;
    }

    public async Task AcknowledgeReadinessRuleAsync(
        Guid projectVersionId,
        string ruleCode,
        string explanation,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        ruleCode = Required(ruleCode, nameof(ruleCode), 100);
        explanation = Required(explanation, nameof(explanation), 4000);
        await SaveProjectRecordAsync(
            projectVersionId,
            GovernanceRecordTypes.ReadinessAcknowledgement,
            ruleCode,
            projectVersionId,
            "Acknowledged",
            SerializeCanonical(new { ruleCode, explanation, acknowledgedAt = DateTimeOffset.UtcNow, actorId }),
            true,
            actorId,
            cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetAcknowledgedRuleCodesAsync(
        Guid projectVersionId,
        CancellationToken cancellationToken) =>
        (await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId
                && item.RecordType == GovernanceRecordTypes.ReadinessAcknowledgement
                && item.Status == "Acknowledged")
            .Select(item => item.StableKey)
            .ToArrayAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    public async Task<WorkflowTransitionResult> TransitionAsync(
        Guid projectVersionId,
        VerificationWorkflowState targetState,
        Guid actorId,
        IReadOnlySet<string> actorRoles,
        bool hasMfa,
        string reason,
        CancellationToken cancellationToken)
    {
        var project = await _dbContext.InventoryProjectVersions
            .SingleAsync(item => item.Id == projectVersionId, cancellationToken);
        var acknowledgements = await GetAcknowledgedRuleCodesAsync(projectVersionId, cancellationToken);
        var report = await BuildReadinessReportAsync(projectVersionId, acknowledgements, actorId, true, cancellationToken);
        var findings = await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId
                && item.RecordType == GovernanceRecordTypes.ReviewFinding)
            .Select(item => item.PayloadJson)
            .ToArrayAsync(cancellationToken);
        var parsedFindings = findings.Select(Deserialize<ReviewFinding>).ToArray();
        var openBlocking = VerificationWorkflowService.ValidateFindingsForApproval(parsedFindings).Count > 0;
        var verificationPayload = await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId
                && item.RecordType == GovernanceRecordTypes.VerificationRecord)
            .OrderByDescending(item => item.VersionNumber)
            .Select(item => item.PayloadJson)
            .FirstOrDefaultAsync(cancellationToken);
        var verification = verificationPayload is null ? null : Deserialize<VerificationRecord>(verificationPayload);
        var materialEditors = await _dbContext.GovernanceEvents.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId && item.ActorId.HasValue)
            .Select(item => item.ActorId!.Value.ToString("D"))
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var currentState = ParseWorkflowState(project.WorkflowStatus);
        var inputChanged = project.ReviewedAt.HasValue && await _dbContext.GovernanceEvents.AsNoTracking()
            .AnyAsync(item => item.ProjectVersionId == projectVersionId
                && item.OccurredAt > project.ReviewedAt.Value
                && (item.EventType.EndsWith(".changed", StringComparison.Ordinal)
                    || item.EventType == "governance.record.created"), cancellationToken);
        var actor = new WorkflowActor(
            actorId.ToString("D"),
            CurrentOrganizationId(),
            actorRoles,
            hasMfa,
            materialEditors.Contains(actorId.ToString("D"), StringComparer.Ordinal)
                ? new HashSet<Guid> { projectVersionId }
                : new HashSet<Guid>());
        var transition = VerificationWorkflowService.Transition(new WorkflowTransitionRequest(
            projectVersionId,
            currentState,
            targetState,
            actor,
            project.CreatedBy?.ToString("D") ?? string.Empty,
            report.CanSubmit(acknowledgements),
            openBlocking,
            verification is not null,
            verification?.HasValidSignedStatement ?? false,
            inputChanged,
            Required(reason, nameof(reason), 4000),
            DateTimeOffset.UtcNow));

        project.WorkflowStatus = targetState.ToString();
        if (targetState is VerificationWorkflowState.Submitted or VerificationWorkflowState.Resubmitted)
        {
            project.SubmittedAt = transition.OccurredAt;
            await LockProjectEvidenceAsync(projectVersionId, actorId, targetState.ToString(), cancellationToken);
        }
        if (targetState is VerificationWorkflowState.InternallyApproved or VerificationWorkflowState.Verified)
        {
            project.ReviewedAt = transition.OccurredAt;
            project.ReviewedBy = actorId;
            await LockProjectEvidenceAsync(projectVersionId, actorId, targetState.ToString(), cancellationToken);
        }

        await AppendEventAsync(
            projectVersionId,
            transition.AuditCode,
            "InventoryProjectVersion",
            projectVersionId,
            SerializeCanonical(transition),
            actorId,
            cancellationToken);
        await AppendEventAsync(
            projectVersionId,
            "notification.created",
            "InventoryProjectVersion",
            projectVersionId,
            SerializeCanonical(new
            {
                targetState = targetState.ToString(),
                message = $"Inventory workflow changed to {targetState}.",
                occurredAt = transition.OccurredAt
            }),
            actorId,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return transition;
    }

    public async Task<ProjectGovernanceRecord> SaveReviewFindingAsync(
        Guid projectVersionId,
        string findingJson,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var finding = Deserialize<ReviewFinding>(findingJson);
        if (finding.ProjectVersionId != projectVersionId)
        {
            throw new InvalidOperationException("Review finding project does not match the selected project.");
        }
        if (string.IsNullOrWhiteSpace(finding.Description) || string.IsNullOrWhiteSpace(finding.Category))
        {
            throw new InvalidOperationException("Review finding category and description are required.");
        }
        foreach (var evidenceId in finding.EvidenceDocumentVersionIds)
        {
            await RequireUsableEvidenceVersionAsync(evidenceId, cancellationToken);
        }
        return await SaveProjectRecordAsync(
            projectVersionId,
            GovernanceRecordTypes.ReviewFinding,
            finding.Id.ToString("D"),
            finding.AffectedEntityId,
            finding.Status.ToString(),
            SerializeCanonical(finding),
            finding.Status is ReviewFindingStatus.Resolved or ReviewFindingStatus.AcceptedRisk,
            actorId,
            cancellationToken);
    }

    public async Task<ProjectGovernanceRecord> SaveVerificationRecordAsync(
        Guid projectVersionId,
        string verificationJson,
        WorkflowActor verifier,
        CancellationToken cancellationToken)
    {
        var project = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .SingleAsync(item => item.Id == projectVersionId, cancellationToken);
        var verification = Deserialize<VerificationRecord>(verificationJson);
        if (verification.ProjectVersionId != projectVersionId)
        {
            throw new InvalidOperationException("Verification record project does not match the selected project.");
        }
        var editors = await _dbContext.GovernanceEvents.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId && item.ActorId.HasValue)
            .Select(item => item.ActorId!.Value.ToString("D"))
            .ToHashSetAsync(cancellationToken);
        verification = VerificationWorkflowService.CompleteVerification(
            verification,
            verifier,
            project.CreatedBy?.ToString("D") ?? string.Empty,
            editors);
        return await SaveProjectRecordAsync(
            projectVersionId,
            GovernanceRecordTypes.VerificationRecord,
            verification.Id.ToString("D"),
            verification.Id,
            "Completed",
            SerializeCanonical(verification),
            true,
            Guid.TryParse(verifier.UserId, out var actorId) ? actorId : null,
            cancellationToken);
    }

    public async Task<string> BuildGovernanceSnapshotJsonAsync(
        Guid projectVersionId,
        CancellationToken cancellationToken)
    {
        var records = await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .OrderBy(item => item.RecordType)
            .ThenBy(item => item.StableKey)
            .ThenBy(item => item.VersionNumber)
            .Select(item => new
            {
                item.Id,
                item.RecordType,
                item.StableKey,
                item.VersionNumber,
                item.Status,
                item.CanonicalSha256,
                item.IsImmutable,
                item.PayloadJson
            })
            .ToArrayAsync(cancellationToken);
        var activityIds = await _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == projectVersionId)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var links = await _dbContext.EvidenceLinks.AsNoTracking()
            .Where(item => item.TargetType == EvidenceLinkTargetType.Activity.ToString()
                && activityIds.Contains(item.TargetId))
            .OrderBy(item => item.TargetId)
            .ThenBy(item => item.DocumentVersionId)
            .ToArrayAsync(cancellationToken);
        var versionIds = links.Select(item => item.DocumentVersionId).Distinct().ToArray();
        var versions = await _dbContext.EvidenceDocumentVersions.AsNoTracking()
            .Where(item => versionIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.DocumentId,
                item.VersionNumber,
                item.OriginalFileName,
                item.ContentType,
                item.SizeBytes,
                item.Sha256,
                item.ScanStatus,
                item.StorageStatus,
                item.ReplacesVersionId
            })
            .ToArrayAsync(cancellationToken);
        return SerializeCanonical(new
        {
            schemaVersion = "governance-snapshot-v1",
            projectVersionId,
            records = records.Select(item => new
            {
                item.Id,
                item.RecordType,
                item.StableKey,
                item.VersionNumber,
                item.Status,
                item.CanonicalSha256,
                item.IsImmutable,
                payload = JsonDocument.Parse(item.PayloadJson).RootElement
            }),
            evidenceLinks = links,
            evidenceVersions = versions
        });
    }

    public async Task<EmissionFactorVersion> ResolveFactorAsync(
        ActivityDataRecord activity,
        CancellationToken cancellationToken)
    {
        if (activity.GlobalFactorDefinitionVersionId.HasValue)
        {
            var definition = await RequirePublishedDefinitionAsync(
                activity.GlobalFactorDefinitionVersionId.Value,
                GovernanceDefinitionTypes.GlobalEmissionFactor,
                cancellationToken);
            var payload = Deserialize<GlobalFactorDefinitionPayload>(definition.PayloadJson);
            var version = payload.Version;
            return new(
                version.Id,
                version.GlobalFactorId,
                version.VersionNumber,
                payload.Factor.OriginalName,
                version.Value,
                version.NumeratorUnit,
                version.DenominatorUnit,
                version.Geography,
                version.ValidFrom,
                version.ValidTo,
                version.Status == GlobalFactorVersionStatus.Published
                    ? FactorPublicationStatus.Published
                    : FactorPublicationStatus.Withdrawn,
                version.DatasetVersion,
                version.License,
                FactorReviewStatus.NotRequired,
                string.IsNullOrWhiteSpace(version.Technology) ? "global" : version.Technology);
        }

        if (!activity.FactorVersionId.HasValue)
        {
            throw new InvalidOperationException($"Activity {activity.Id} has no local or global factor version.");
        }
        var factor = await _dbContext.EmissionFactorVersions.AsNoTracking()
            .SingleAsync(item => item.Id == activity.FactorVersionId.Value, cancellationToken);
        return new(
            factor.Id,
            factor.FactorId,
            factor.VersionNumber,
            factor.Name,
            factor.Value,
            factor.NumeratorUnitCode,
            factor.DenominatorUnitCode,
            factor.Geography,
            factor.ValidFrom,
            factor.ValidTo,
            Enum.Parse<FactorPublicationStatus>(factor.PublicationStatus),
            factor.SourceDatasetVersion,
            factor.LicenseCode,
            Enum.Parse<FactorReviewStatus>(factor.ReviewStatus),
            factor.Applicability);
    }

    public async Task<ActivityFormulaDefinitionVersion?> ResolveFormulaAsync(
        ActivityDataRecord activity,
        CancellationToken cancellationToken)
    {
        if (!activity.FormulaDefinitionVersionId.HasValue)
        {
            return null;
        }
        var definition = await RequirePublishedDefinitionAsync(
            activity.FormulaDefinitionVersionId.Value,
            GovernanceDefinitionTypes.ActivityFormula,
            cancellationToken);
        return Deserialize<ActivityFormulaDefinitionVersion>(definition.PayloadJson);
    }

    public async Task<string> BuildActivityEvidenceIndexJsonAsync(
        Guid activityId,
        CancellationToken cancellationToken)
    {
        var links = await _dbContext.EvidenceLinks.AsNoTracking()
            .Where(item => item.TargetType == EvidenceLinkTargetType.Activity.ToString()
                && item.TargetId == activityId)
            .OrderBy(item => item.DocumentVersionId)
            .ToArrayAsync(cancellationToken);
        var ids = links.Select(item => item.DocumentVersionId).ToArray();
        var versions = await _dbContext.EvidenceDocumentVersions.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return SerializeCanonical(links.Select(link => new
        {
            link.Id,
            link.DocumentVersionId,
            link.Purpose,
            link.IsRequired,
            version = new
            {
                versions[link.DocumentVersionId].DocumentId,
                versions[link.DocumentVersionId].VersionNumber,
                versions[link.DocumentVersionId].OriginalFileName,
                versions[link.DocumentVersionId].ContentType,
                versions[link.DocumentVersionId].SizeBytes,
                versions[link.DocumentVersionId].Sha256,
                versions[link.DocumentVersionId].ScanStatus,
                versions[link.DocumentVersionId].StorageStatus
            }
        }));
    }

    public async Task<string> GetProjectRecordPayloadAsync(
        Guid? recordId,
        string emptyJson,
        CancellationToken cancellationToken)
    {
        if (!recordId.HasValue)
        {
            return emptyJson;
        }
        return await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.Id == recordId.Value)
            .Select(item => item.PayloadJson)
            .SingleAsync(cancellationToken);
    }

    public async Task<VerificationArchiveRecord> GenerateVerificationArchiveAsync(
        Guid projectVersionId,
        Guid calculationRunId,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var project = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .SingleAsync(item => item.Id == projectVersionId, cancellationToken);
        var run = await _dbContext.CalculationRuns.AsNoTracking()
            .SingleAsync(item => item.Id == calculationRunId && item.ProjectVersionId == projectVersionId, cancellationToken);
        var lines = await _dbContext.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.ActivityId)
            .ToArrayAsync(cancellationToken);
        var summaries = await _dbContext.CalculationStageSummaries.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.LifecycleStage)
            .ToArrayAsync(cancellationToken);
        var records = await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .OrderBy(item => item.RecordType)
            .ThenBy(item => item.StableKey)
            .ThenBy(item => item.VersionNumber)
            .ToArrayAsync(cancellationToken);
        var activities = await _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == projectVersionId)
            .ToArrayAsync(cancellationToken);
        var activityIds = activities.Select(item => item.Id).ToArray();
        var links = await _dbContext.EvidenceLinks.AsNoTracking()
            .Where(item => item.TargetType == EvidenceLinkTargetType.Activity.ToString()
                && activityIds.Contains(item.TargetId))
            .OrderBy(item => item.TargetId)
            .ThenBy(item => item.DocumentVersionId)
            .ToArrayAsync(cancellationToken);
        var evidenceIds = links.Select(item => item.DocumentVersionId).Distinct().ToArray();
        var evidence = await _dbContext.EvidenceDocumentVersions.AsNoTracking()
            .Where(item => evidenceIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var audit = await _dbContext.AuditEvents.AsNoTracking()
            .Where(item => item.ResourceId == projectVersionId || item.ResourceId == calculationRunId)
            .OrderBy(item => item.Timestamp)
            .ToArrayAsync(cancellationToken);
        var workflowEvents = await _dbContext.GovernanceEvents.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .OrderBy(item => item.OccurredAt)
            .ToArrayAsync(cancellationToken);
        var readiness = records.LastOrDefault(item => item.RecordType == GovernanceRecordTypes.ReadinessReport)?.PayloadJson ?? "{}";
        var findings = records.Where(item => item.RecordType == GovernanceRecordTypes.ReviewFinding)
            .Select(item => JsonDocument.Parse(item.PayloadJson).RootElement).ToArray();
        var verifications = records.Where(item => item.RecordType == GovernanceRecordTypes.VerificationRecord)
            .Select(item => JsonDocument.Parse(item.PayloadJson).RootElement).ToArray();
        var allocationRows = records.Where(item => item.RecordType == GovernanceRecordTypes.AllocationPool)
            .Select(item => new object?[] { item.StableKey, item.VersionNumber, item.Status, item.CanonicalSha256, item.PayloadJson })
            .ToArray();
        var transportRows = records.Where(item => item.RecordType == GovernanceRecordTypes.TransportChain)
            .Select(item => new object?[] { item.StableKey, item.VersionNumber, item.Status, item.CanonicalSha256, item.PayloadJson })
            .ToArray();
        var qualityRows = records.Where(item => item.RecordType == GovernanceRecordTypes.DataQualityAssessment)
            .Select(item => new object?[] { item.StableKey, item.VersionNumber, item.Status, item.CanonicalSha256, item.PayloadJson })
            .ToArray();

        var workbook = ExcelWorkbook.Create([
            new("Summary", Rows(
                ["Field", "Value"],
                ["Project version", projectVersionId],
                ["Calculation run", run.Id],
                ["Manifest SHA-256", run.InputSha256],
                ["Product total", run.ProductTotal],
                ["PCR", run.PcrVersion],
                ["Rule set", run.RuleSetVersion])),
            new("Activities", Rows(
                ["Activity", "Stage", "Formula", "Factor", "Amount", "Emissions", "Formula trace"],
                lines.Select(line => new object?[]
                {
                    line.ActivityId, line.LifecycleStage, line.FormulaId, line.FactorVersionId,
                    line.CanonicalActivityValue, line.Emissions, line.FormulaTraceJson
                }).ToArray())),
            new("Data quality", Rows(["Key", "Version", "Status", "SHA-256", "Payload"], qualityRows)),
            new("Allocations", Rows(["Key", "Version", "Status", "SHA-256", "Payload"], allocationRows)),
            new("Transport", Rows(["Key", "Version", "Status", "SHA-256", "Payload"], transportRows)),
            new("Evidence", Rows(
                ["Version", "Document", "File", "SHA-256", "Scan", "Storage"],
                evidence.Select(item => new object?[]
                {
                    item.Id, item.DocumentId, item.OriginalFileName, item.Sha256, item.ScanStatus, item.StorageStatus
                }).ToArray()))
        ]);

        var html = $"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Inventory verification report</title><body><h1>Inventory verification report</h1><dl><dt>Project version</dt><dd>{projectVersionId:D}</dd><dt>Calculation run</dt><dd>{run.Id:D}</dd><dt>Manifest SHA-256</dt><dd>{run.InputSha256}</dd><dt>Total</dt><dd>{run.ProductTotal.ToString(CultureInfo.InvariantCulture)}</dd></dl></body></html>";
        var factorRegister = Csv(
            ["factorVersionId", "factorValue", "factorUnit"],
            lines.Select(item => new[]
            {
                item.FactorVersionId.ToString("D"),
                item.FactorValue.ToString(CultureInfo.InvariantCulture),
                item.FactorUnit
            }));
        var metadata = new VerificationArchiveMetadata(
            projectVersionId,
            run.Id,
            run.EngineBuild,
            run.RuleSetVersion,
            run.PcrVersion,
            run.GwpVersion,
            run.UnitCatalogueVersion,
            lines.Select(item => item.FormulaId).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            lines.Select(item => item.FactorVersionId).Distinct().OrderBy(item => item).ToArray(),
            ExportSchemaVersion,
            run.InputSha256,
            run.CreatedAt);
        var archive = VerificationArchiveBuilder.Build(metadata,
        [
            File("report/inventory-report.html", html, "text/html; charset=utf-8"),
            new VerificationArchiveFile("workbook/inventory.xlsx", workbook, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            File("manifest/canonical-manifest.json", run.CanonicalInputManifest, "application/json"),
            File("calculation/line-items.csv", Csv(
                ["activityId", "stage", "formulaId", "formulaVersionId", "activityValue", "activityUnit", "factorVersionId", "factorValue", "factorUnit", "allocation", "emissions", "emissionsUnit", "formulaTrace", "governanceTrace"],
                lines.Select(item => new[]
                {
                    item.ActivityId.ToString("D"), item.LifecycleStage.ToString(CultureInfo.InvariantCulture), item.FormulaId,
                    item.FormulaDefinitionVersionId?.ToString("D") ?? string.Empty,
                    item.CanonicalActivityValue.ToString(CultureInfo.InvariantCulture), item.ActivityUnitCode,
                    item.FactorVersionId.ToString("D"), item.FactorValue.ToString(CultureInfo.InvariantCulture), item.FactorUnit,
                    item.AllocationFactor.ToString(CultureInfo.InvariantCulture), item.Emissions.ToString(CultureInfo.InvariantCulture),
                    item.EmissionsUnitCode, item.FormulaTraceJson, item.GovernanceTraceJson
                })), "text/csv; charset=utf-8"),
            File("calculation/stage-summary.csv", Csv(
                ["stage", "emissions"],
                summaries.Select(item => new[]
                {
                    item.LifecycleStage.ToString(CultureInfo.InvariantCulture),
                    item.Emissions.ToString(CultureInfo.InvariantCulture)
                })), "text/csv; charset=utf-8"),
            File("register/factors.csv", factorRegister, "text/csv; charset=utf-8"),
            File("trace/unit-conversions.csv", Csv(
                ["activityId", "formulaInputs"],
                lines.Select(item => new[] { item.ActivityId.ToString("D"), item.FormulaInputsJson })), "text/csv; charset=utf-8"),
            File("trace/allocations.csv", Csv(
                ["key", "version", "sha256", "payload"],
                records.Where(item => item.RecordType == GovernanceRecordTypes.AllocationPool)
                    .Select(item => new[] { item.StableKey, item.VersionNumber.ToString(CultureInfo.InvariantCulture), item.CanonicalSha256, item.PayloadJson })), "text/csv; charset=utf-8"),
            File("evidence/index.csv", Csv(
                ["documentVersionId", "documentId", "targetId", "required", "filename", "sha256", "scan", "storage"],
                links.Select(link =>
                {
                    var version = evidence.Single(item => item.Id == link.DocumentVersionId);
                    return new[]
                    {
                        version.Id.ToString("D"), version.DocumentId.ToString("D"), link.TargetId.ToString("D"),
                        link.IsRequired.ToString(), version.OriginalFileName, version.Sha256, version.ScanStatus, version.StorageStatus
                    };
                })), "text/csv; charset=utf-8"),
            File("validation/readiness.json", readiness, "application/json"),
            File("review/findings.json", SerializeCanonical(findings), "application/json"),
            File("verification/records.json", SerializeCanonical(verifications), "application/json"),
            File("audit/events.json", SerializeCanonical(new { audit, workflowEvents }), "application/json")
        ]);
        if (!VerificationArchiveBuilder.Verify(archive))
        {
            throw new InvalidOperationException("Generated verification archive failed its own integrity verification.");
        }

        var existing = await _dbContext.VerificationArchives
            .SingleOrDefaultAsync(item => item.ProjectVersionId == projectVersionId
                && item.CalculationRunId == calculationRunId
                && item.ArchiveSha256 == archive.ArchiveSha256, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }
        var stored = new VerificationArchiveRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = CurrentOrganizationId(),
            ProjectVersionId = projectVersionId,
            CalculationRunId = calculationRunId,
            ExportSchemaVersion = archive.ExportSchemaVersion,
            ArchiveSha256 = archive.ArchiveSha256,
            ArchiveBytes = archive.ArchiveBytes,
            FileIndexJson = SerializeCanonical(archive.Files),
            GeneratedAt = archive.GeneratedAt,
            GeneratedBy = actorId
        };
        _dbContext.VerificationArchives.Add(stored);
        _dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actorId,
            OrganizationId = CurrentOrganizationId(),
            Action = "verification.archive.generated",
            ResourceType = "VerificationArchive",
            ResourceId = stored.Id,
            AfterHash = archive.ArchiveSha256,
            CorrelationId = stored.Id.ToString("N"),
            MetadataJson = SerializeCanonical(new { projectVersionId, calculationRunId, archive.ArchiveSha256 })
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return stored;
    }

    public async Task<ProjectVersionComparison> CompareRunsAsync(
        Guid projectVersionId,
        Guid previousRunId,
        Guid currentRunId,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var previous = await LoadEntitySnapshotsAsync(previousRunId, cancellationToken);
        var current = await LoadEntitySnapshotsAsync(currentRunId, cancellationToken);
        var comparison = ProjectVersionComparisonService.Compare(previous, current);
        await SaveProjectRecordAsync(
            projectVersionId,
            GovernanceRecordTypes.ScenarioComparison,
            $"{previousRunId:D}:{currentRunId:D}",
            projectVersionId,
            "Completed",
            SerializeCanonical(new ProjectComparisonPayload(previousRunId, currentRunId, comparison)),
            true,
            actorId,
            cancellationToken);
        return comparison;
    }

    private async Task<IReadOnlyList<ProjectEntitySnapshot>> LoadEntitySnapshotsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var lines = await _dbContext.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == runId)
            .OrderBy(item => item.ActivityId)
            .ToArrayAsync(cancellationToken);
        return lines.Select(item => new ProjectEntitySnapshot(
            "Activity",
            item.ActivityId.ToString("D"),
            Sha256(SerializeCanonical(new
            {
                item.ActivityId,
                item.FormulaId,
                item.FormulaDefinitionVersionId,
                item.CanonicalActivityValue,
                item.ActivityUnitCode,
                item.FactorVersionId,
                item.FactorValue,
                item.AllocationFactor,
                item.FormulaTraceJson,
                item.GovernanceTraceJson
            })),
            item.Emissions,
            ((LifecycleStage)item.LifecycleStage).ToString())).ToArray();
    }

    private async Task<ProjectGovernanceRecord> SaveProjectRecordAsync(
        Guid projectVersionId,
        string recordType,
        string stableKey,
        Guid? targetEntityId,
        string status,
        string payloadJson,
        bool isImmutable,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        _ = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .SingleAsync(item => item.Id == projectVersionId, cancellationToken);
        var canonical = CanonicalizeJson(payloadJson);
        var latestVersion = await _dbContext.ProjectGovernanceRecords.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId
                && item.RecordType == recordType
                && item.StableKey == stableKey)
            .MaxAsync(item => (int?)item.VersionNumber, cancellationToken) ?? 0;
        var record = new ProjectGovernanceRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = CurrentOrganizationId(),
            ProjectVersionId = projectVersionId,
            TargetEntityId = targetEntityId,
            RecordType = recordType,
            StableKey = stableKey,
            VersionNumber = latestVersion + 1,
            Status = status,
            PayloadJson = canonical,
            CanonicalSha256 = Sha256(canonical),
            IsImmutable = isImmutable,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = actorId,
            LockedAt = isImmutable ? DateTimeOffset.UtcNow : null,
            LockReason = isImmutable ? status : string.Empty
        };
        _dbContext.ProjectGovernanceRecords.Add(record);
        await AppendEventAsync(
            projectVersionId,
            "governance.record.created",
            recordType,
            record.Id,
            SerializeCanonical(new { record.Id, record.RecordType, record.StableKey, record.VersionNumber, record.Status, record.CanonicalSha256 }),
            actorId,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    private async Task AppendEventAsync(
        Guid projectVersionId,
        string eventType,
        string entityType,
        Guid entityId,
        string payloadJson,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var canonical = CanonicalizeJson(payloadJson);
        _dbContext.GovernanceEvents.Add(new GovernanceEventRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = CurrentOrganizationId(),
            ProjectVersionId = projectVersionId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = canonical,
            PayloadSha256 = Sha256(canonical),
            ActorId = actorId,
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        await Task.CompletedTask;
    }

    private async Task InvalidateProjectCalculationAsync(
        Guid projectVersionId,
        Guid? actorId,
        string eventType,
        CancellationToken cancellationToken)
    {
        await AppendEventAsync(
            projectVersionId,
            eventType,
            "InventoryProjectVersion",
            projectVersionId,
            SerializeCanonical(new { projectVersionId, invalidatedAt = DateTimeOffset.UtcNow }),
            actorId,
            cancellationToken);
        var project = await _dbContext.InventoryProjectVersions.SingleAsync(item => item.Id == projectVersionId, cancellationToken);
        if (ParseWorkflowState(project.WorkflowStatus) is VerificationWorkflowState.InternallyApproved
            or VerificationWorkflowState.VerificationRequested
            or VerificationWorkflowState.UnderVerification
            or VerificationWorkflowState.Verified
            or VerificationWorkflowState.Published)
        {
            project.WorkflowStatus = VerificationWorkflowState.ReadinessFailed.ToString();
        }
    }

    private async Task LockProjectEvidenceAsync(
        Guid projectVersionId,
        Guid? actorId,
        string trigger,
        CancellationToken cancellationToken)
    {
        var activityIds = await _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == projectVersionId)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var versionIds = await _dbContext.EvidenceLinks.AsNoTracking()
            .Where(item => item.TargetType == EvidenceLinkTargetType.Activity.ToString()
                && activityIds.Contains(item.TargetId))
            .Select(item => item.DocumentVersionId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        foreach (var versionId in versionIds)
        {
            var exists = await _dbContext.EvidenceRetentionLocks.AnyAsync(
                item => item.DocumentVersionId == versionId,
                cancellationToken);
            if (exists)
            {
                continue;
            }
            _dbContext.EvidenceRetentionLocks.Add(new EvidenceRetentionLockRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = CurrentOrganizationId(),
                DocumentVersionId = versionId,
                LockedAt = DateTimeOffset.UtcNow,
                RetainUntil = DateTimeOffset.UtcNow.AddYears(10),
                Trigger = trigger,
                LockedBy = actorId
            });
            var version = await _dbContext.EvidenceDocumentVersions.SingleAsync(item => item.Id == versionId, cancellationToken);
            version.StorageStatus = "RetentionLocked";
        }
    }

    private async Task<ReadinessFactorContext?> BuildFactorReadinessAsync(
        ActivityDataRecord activity,
        IReadOnlyDictionary<Guid, EmissionFactorVersionRecord> localFactors,
        IReadOnlyDictionary<Guid, GovernanceDefinitionRecord> globalFactors,
        CancellationToken cancellationToken)
    {
        if (activity.GlobalFactorDefinitionVersionId.HasValue)
        {
            if (!globalFactors.TryGetValue(activity.GlobalFactorDefinitionVersionId.Value, out var definition))
            {
                return null;
            }
            var payload = Deserialize<GlobalFactorDefinitionPayload>(definition.PayloadJson);
            var activation = await _dbContext.OrganizationDefinitionActivations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.DefinitionVersionId == definition.Id, cancellationToken);
            return new(
                definition.Id,
                definition.PublicationStatus == "Published" && activation?.IsEnabled == true,
                true,
                true,
                payload.Version.IsSelectable(activity.PeriodEnd),
                string.Equals(activity.CanonicalUnitCode, payload.Version.DenominatorUnit, StringComparison.OrdinalIgnoreCase),
                activation?.IsProhibited == true || definition.WithdrawnAt is not null);
        }
        if (!activity.FactorVersionId.HasValue || !localFactors.TryGetValue(activity.FactorVersionId.Value, out var factor))
        {
            return null;
        }
        return new(
            factor.Id,
            factor.PublicationStatus == "Published",
            factor.ReviewStatus is "Approved" or "NotRequired",
            true,
            (factor.ValidFrom is null || factor.ValidFrom <= activity.PeriodEnd)
                && (factor.ValidTo is null || factor.ValidTo >= activity.PeriodEnd),
            string.Equals(activity.CanonicalUnitCode, factor.DenominatorUnitCode, StringComparison.OrdinalIgnoreCase),
            factor.WithdrawnAt is not null || factor.PublicationStatus == "Withdrawn");
    }

    private async Task<string> BuildCurrentManifestHashAsync(
        Guid projectVersionId,
        CancellationToken cancellationToken)
    {
        var latest = await _dbContext.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => item.InputSha256)
            .FirstOrDefaultAsync(cancellationToken);
        var changedAfter = await _dbContext.GovernanceEvents.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId
                && item.EventType.EndsWith(".changed", StringComparison.Ordinal))
            .OrderByDescending(item => item.OccurredAt)
            .Select(item => (DateTimeOffset?)item.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
        var runAt = await _dbContext.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == projectVersionId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => (DateTimeOffset?)item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return changedAfter.HasValue && (!runAt.HasValue || changedAfter > runAt) ? string.Empty : latest ?? string.Empty;
    }

    private async Task DetectDefinitionImpactsAsync(
        GovernanceDefinitionRecord definition,
        string reason,
        CancellationToken cancellationToken)
    {
        var activityQuery = _dbContext.ActivityData.AsNoTracking().Where(item =>
            item.GlobalFactorDefinitionVersionId == definition.Id
            || item.FormulaDefinitionVersionId == definition.Id);
        var activities = await activityQuery.ToArrayAsync(cancellationToken);
        foreach (var activity in activities)
        {
            _dbContext.ProjectImpacts.Add(new ProjectImpactRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = CurrentOrganizationId(),
                ProjectVersionId = activity.InventoryProjectVersionId,
                ChangeType = definition.DefinitionType == GovernanceDefinitionTypes.GlobalEmissionFactor
                    ? GovernedChangeType.FactorWithdrawn.ToString()
                    : GovernedChangeType.FormulaWithdrawn.ToString(),
                DependencyType = definition.DefinitionType,
                DependencyKey = definition.StableKey,
                PreviousVersion = definition.VersionNumber.ToString(CultureInfo.InvariantCulture),
                CurrentVersion = string.Empty,
                LifecycleStage = ((LifecycleStage)activity.LifecycleStage).ToString(),
                Reason = reason,
                DetectedAt = DateTimeOffset.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<GovernanceDefinitionRecord> RequirePublishedDefinitionAsync(
        Guid id,
        string definitionType,
        CancellationToken cancellationToken)
    {
        var definition = await _dbContext.GovernanceDefinitions.AsNoTracking()
            .SingleAsync(item => item.Id == id && item.DefinitionType == definitionType, cancellationToken);
        if (!string.Equals(definition.PublicationStatus, "Published", StringComparison.OrdinalIgnoreCase)
            || definition.WithdrawnAt is not null)
        {
            throw new InvalidOperationException($"Definition {id} is not published and selectable.");
        }
        return definition;
    }

    private async Task<EvidenceDocumentVersionRecord> RequireUsableEvidenceVersionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var version = await _dbContext.EvidenceDocumentVersions.AsNoTracking()
            .SingleAsync(item => item.Id == id, cancellationToken);
        if (version.Sha256.Length != 64
            || version.ScanStatus != "Clean"
            || version.StorageStatus is not ("Available" or "RetentionLocked"))
        {
            throw new InvalidOperationException("Evidence document version is not usable.");
        }
        return version;
    }

    private async Task RequireProjectAndActivityAsync(
        Guid projectVersionId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.ActivityData.AsNoTracking()
            .AnyAsync(item => item.Id == activityId && item.InventoryProjectVersionId == projectVersionId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Activity does not belong to the selected project.");
        }
    }

    private Guid CurrentOrganizationId() =>
        _organizationScope.OrganizationId
        ?? throw new InvalidOperationException("Current organization scope is not available.");

    private static VerificationWorkflowState ParseWorkflowState(string value) => value switch
    {
        "Approved" => VerificationWorkflowState.InternallyApproved,
        _ when Enum.TryParse<VerificationWorkflowState>(value, out var parsed) => parsed,
        _ => VerificationWorkflowState.Draft
    };

    private static void ValidateDefinitionPayload(string definitionType, string json)
    {
        switch (definitionType)
        {
            case GovernanceDefinitionTypes.ActivityFormula:
                {
                    var formula = Deserialize<ActivityFormulaDefinitionVersion>(json);
                    if (formula.Status != FormulaPublicationStatus.Draft
                        && formula.Status != FormulaPublicationStatus.Published)
                    {
                        throw new InvalidOperationException("New formula definitions must be draft or published candidates.");
                    }
                    _ = new ActivityFormulaRegistry(
                        [new DirectAmountFormula(), new FactorBasedFormula(), new MassBalanceFormula(), new EnergyBalanceFormula()],
                        [formula]);
                    break;
                }
            case GovernanceDefinitionTypes.DataQualityRuleSet:
                _ = Deserialize<DataQualityRuleSetVersion>(json).Weights();
                break;
            case GovernanceDefinitionTypes.GlobalEmissionFactor:
                {
                    var factor = Deserialize<GlobalFactorDefinitionPayload>(json);
                    if (factor.Version.Value < 0m || factor.Version.SourceRecordSha256.Length != 64)
                    {
                        throw new InvalidOperationException("Global factor payload has an invalid value or source SHA-256.");
                    }
                    break;
                }
            case GovernanceDefinitionTypes.TransportRouteTemplate:
                {
                    var route = Deserialize<TransportChainVersion>(json);
                    if (!route.IsTemplate)
                    {
                        throw new InvalidOperationException("Route-template definitions must have IsTemplate=true.");
                    }
                    _ = TransportChainCalculator.Validate(route);
                    break;
                }
            case GovernanceDefinitionTypes.EvidenceRetentionPolicy:
                {
                    var policy = Deserialize<EvidenceRetentionPolicy>(json);
                    if (!policy.IsPublished || policy.MinimumRetention <= TimeSpan.Zero)
                    {
                        throw new InvalidOperationException("Retention policy must be published and have a positive retention period.");
                    }
                    break;
                }
            default:
                throw new InvalidOperationException($"Unsupported governance definition type: {definitionType}.");
        }
    }

    private static string MergeTrace(string currentJson, string key, Guid id, string sha256)
    {
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        using var current = JsonDocument.Parse(string.IsNullOrWhiteSpace(currentJson) ? "{}" : currentJson);
        if (current.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in current.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }
        }
        values[key] = new { id, sha256 };
        return SerializeCanonical(values);
    }

    private static bool MatchesControlled(string rule, string value) =>
        string.IsNullOrWhiteSpace(rule)
        || rule == "*"
        || string.Equals(rule.Trim(), value?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool CsvContainsOrEmpty(string csv, string value) =>
        string.IsNullOrWhiteSpace(csv)
        || csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string Required(string? value, string name, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw new InvalidOperationException($"{name} is required and must not exceed {maximumLength} characters.");
        }
        return normalized;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string SerializeCanonical<T>(T value) => CanonicalizeJson(JsonSerializer.Serialize(value, JsonOptions));

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException($"Unable to deserialize {typeof(T).Name} payload.");

    public static string CanonicalizeJson(string? json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, document.RootElement);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));

    private static string Sha256(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private static VerificationArchiveFile File(string path, string content, string mediaType) =>
        new(path, Encoding.UTF8.GetBytes(content), mediaType);

    private static string Csv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
        return string.Join(',', headers.Select(Escape)) + "\n"
            + string.Join("\n", rows.Select(row => string.Join(',', row.Select(item => Escape(item ?? string.Empty)))))
            + "\n";
    }

    private static IReadOnlyList<IReadOnlyList<object?>> Rows(
        IReadOnlyList<object?> header,
        params IReadOnlyList<object?>[] rows) =>
        new[] { header }.Concat(rows).ToArray();
}
