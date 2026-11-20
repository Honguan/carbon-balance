using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Exports;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Domain.Modules.Units;
using CarbonFootprint.Infrastructure.Evidence;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Infrastructure.Governance;

public sealed class PcrGovernanceService(
    CarbonFootprintDbContext db, IOperationAuthorizer authorizer, IOrganizationScope scope, EvidenceStorageService documentStorage) : IPcrGovernance
{
    private const string ResourceType = "PcrVersion";

    public async Task<OperationResult> CreateAsync(
        OperationContext context, PcrInput input, EvidenceUpload originalDocument, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: false)) return OperationResult.Forbidden;
        if (string.IsNullOrWhiteSpace(input.RegistrationNumber)
            || input.VersionNumber < 1
            || string.IsNullOrWhiteSpace(input.Title)
            || !IsHttpSourceUrl(input.SourceReference)
            || string.IsNullOrWhiteSpace(input.StandardCode)
            || string.IsNullOrWhiteSpace(input.CccClassification)
            || string.IsNullOrWhiteSpace(input.Applicability)
            || string.IsNullOrWhiteSpace(input.RuleRequirements)
            || string.IsNullOrWhiteSpace(input.ProductCategoryPatterns)
            || string.IsNullOrWhiteSpace(input.FunctionalUnitPattern)
            || string.IsNullOrWhiteSpace(input.DeclaredUnitCode)
            || string.IsNullOrWhiteSpace(input.SystemBoundaryCode)
            || string.IsNullOrWhiteSpace(input.PermittedAllocationMethods)
            || string.IsNullOrWhiteSpace(input.FormulaRuleSetVersion)
            || string.IsNullOrWhiteSpace(input.ReportingRequirements)
            || input.CutoffThresholdPercent is < 0 or > 100
            || input.RoundingDecimalPlaces is < 0 or > 12
            || (input.IsCustomRule && string.IsNullOrWhiteSpace(input.CustomRuleJustification))
            || originalDocument.Content is null
            || originalDocument.Length <= 0
            || input.ApprovalDate is null
            || input.ValidFrom is null
            || input.ValidTo is null
            || input.ValidFrom > input.ValidTo)
        {
            return Invalid("PCR 識別、適用條件、計算規則、原始文件與有效期間必須完整且有效。");
        }

        var organizationId = OrganizationId;
        var declaredUnitCode = input.DeclaredUnitCode.Trim();
        if (!await db.Units.AnyAsync(item => item.CatalogueVersion == UnitCatalogue.CurrentVersion && item.Code == declaredUnitCode, cancellationToken))
        {
            return Invalid("PCR 標示單位必須使用目前受控單位目錄中的代碼。");
        }
        if (!string.Equals(input.FormulaRuleSetVersion.Trim(), ActivityEmissionFormula.PcrFormulaRuleSetV1, StringComparison.Ordinal))
        {
            return Invalid("PCR 公式規則版本目前僅支援 pcr-formulas-v1。");
        }

        var pcrVersionId = Guid.NewGuid();
        if (!TryBuildStageRules(organizationId, pcrVersionId, input.Stages, out var stageRules, out var stageRuleError))
        {
            return Invalid(stageRuleError);
        }

        var registrationNumber = input.RegistrationNumber.Trim();
        var existingVersions = await db.PcrVersions.AsNoTracking()
            .Where(item => item.RegistrationNumber == registrationNumber)
            .OrderByDescending(item => item.VersionNumber)
            .ToArrayAsync(cancellationToken);
        if (existingVersions.Any(item => item.VersionNumber == input.VersionNumber))
        {
            return Invalid("同一 PCR 登錄編號不可重複建立相同版本號。");
        }

        StoredEvidence storedDocument;
        try
        {
            storedDocument = await documentStorage.StoreAsync(
                organizationId, originalDocument.Content, originalDocument.FileName, originalDocument.ContentType, cancellationToken);
        }
        catch (EvidenceUploadException exception)
        {
            return Invalid(exception.Message);
        }

        var previousVersion = existingVersions.FirstOrDefault(item => item.VersionNumber < input.VersionNumber);
        db.PcrVersions.Add(new PcrVersionRecord
        {
            Id = pcrVersionId,
            OrganizationId = organizationId,
            RuleSetId = existingVersions.FirstOrDefault()?.RuleSetId ?? Guid.NewGuid(),
            RegistrationNumber = registrationNumber,
            VersionNumber = input.VersionNumber,
            Title = input.Title.Trim(),
            ApprovalDate = input.ApprovalDate,
            ValidFrom = input.ValidFrom,
            ValidTo = input.ValidTo,
            PublicationStatus = PcrPublicationStatus.Draft.ToString(),
            SourceReference = input.SourceReference!.Trim(),
            StandardCode = input.StandardCode.Trim(),
            CccClassification = input.CccClassification.Trim(),
            Applicability = input.Applicability.Trim(),
            RuleRequirements = input.RuleRequirements.Trim(),
            OriginalDocumentName = storedDocument.OriginalFileName,
            OriginalDocumentObjectKey = storedDocument.ObjectKey,
            OriginalDocumentContentType = storedDocument.ContentType,
            OriginalDocumentSizeBytes = storedDocument.SizeBytes,
            OriginalDocumentSha256 = storedDocument.Sha256,
            OriginalDocumentScanStatus = storedDocument.ScanStatus.ToString(),
            ProductCategoryPatterns = input.ProductCategoryPatterns.Trim(),
            FunctionalUnitPattern = input.FunctionalUnitPattern.Trim(),
            DeclaredUnitCode = declaredUnitCode,
            SystemBoundaryCode = input.SystemBoundaryCode.Trim(),
            PermittedAllocationMethodsCsv = string.Join(",", PcrRuleSetMapper.SplitCsv(input.PermittedAllocationMethods)),
            CutoffThresholdPercent = input.CutoffThresholdPercent,
            FormulaRuleSetVersion = input.FormulaRuleSetVersion.Trim(),
            RoundingDecimalPlaces = input.RoundingDecimalPlaces,
            ReportingRequirements = input.ReportingRequirements.Trim(),
            IsCustomRule = input.IsCustomRule,
            CustomRuleJustification = input.CustomRuleJustification?.Trim() ?? string.Empty,
            CustomApprovalStatus = input.IsCustomRule ? PcrCustomApprovalStatus.Pending.ToString() : PcrCustomApprovalStatus.NotRequired.ToString(),
            SupersedesVersionId = previousVersion?.Id,
            ReviewStatus = PcrReviewStatus.Pending.ToString(),
            CreatedBy = context.ActorId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.PcrStageRules.AddRange(stageRules);
        Audit(context, "pcr.version.created", pcrVersionId);
        await db.SaveChangesAsync(cancellationToken);
        return OperationResult.Success($"PCR 草稿已建立；原始文件 SHA-256：{storedDocument.Sha256}。");
    }

    private static OperationResult Invalid(string message) => OperationResult.Invalid("pcr", message);

    private static bool IsHttpSourceUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    public Task<OperationResult> ReviewAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken) =>
        DecideAsync(context, pcrVersionId, rowVersion, approve: true, cancellationToken);

    public Task<OperationResult> RejectAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken) =>
        DecideAsync(context, pcrVersionId, rowVersion, approve: false, cancellationToken);

    private async Task<OperationResult> DecideAsync(
        OperationContext context, Guid pcrVersionId, uint rowVersion, bool approve, CancellationToken cancellationToken)
    {
        var operation = approve ? "ReviewPcr" : "RejectPcr";
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return OperationResult.Forbidden;
        var pcr = await db.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null) return OperationResult.NotFound;
        if (rowVersion != pcr.RowVersion) return await StaleAsync(context, operation, pcr.Id, cancellationToken);
        if (pcr.PublicationStatus != PcrPublicationStatus.Draft.ToString()
            || pcr.ReviewStatus != PcrReviewStatus.Pending.ToString()) return OperationResult.BadRequest;
        if (pcr.CreatedBy.HasValue && pcr.CreatedBy == context.ActorId)
        {
            return OperationResult.Invalid("pcr", approve
                ? "PCR 建立者不可核准自己的規則版本，請由另一位具權限且已啟用 MFA 的人員審查。"
                : "PCR 建立者不可審查自己建立的版本。");
        }

        pcr.ReviewStatus = (approve ? PcrReviewStatus.Approved : PcrReviewStatus.Rejected).ToString();
        if (pcr.IsCustomRule)
        {
            pcr.CustomApprovalStatus = (approve ? PcrCustomApprovalStatus.Approved : PcrCustomApprovalStatus.Rejected).ToString();
        }
        pcr.ReviewedAt = DateTimeOffset.UtcNow;
        pcr.ReviewedBy = context.ActorId;
        Audit(context, approve ? "pcr.version.reviewed" : "pcr.version.rejected", pcr.Id);
        return await SaveAsync(context, operation, pcr.Id, OperationResult.Success(), cancellationToken);
    }

    public async Task<OperationResult> PublishAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return OperationResult.Forbidden;
        var pcr = await db.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null) return OperationResult.NotFound;
        if (rowVersion != pcr.RowVersion) return await StaleAsync(context, "PublishPcr", pcr.Id, cancellationToken);

        var hasControlledDeclaredUnit = await db.Units.AnyAsync(
            item => item.CatalogueVersion == UnitCatalogue.CurrentVersion && item.Code == pcr.DeclaredUnitCode, cancellationToken);
        var hasLaterReleasedVersion = await db.PcrVersions.AnyAsync(
            item => item.RuleSetId == pcr.RuleSetId && item.VersionNumber > pcr.VersionNumber
                && item.PublicationStatus != PcrPublicationStatus.Draft.ToString(), cancellationToken);
        if (!string.Equals(pcr.PublicationStatus, PcrPublicationStatus.Draft.ToString(), StringComparison.Ordinal)
            || pcr.ReviewStatus != PcrReviewStatus.Approved.ToString()
            || hasLaterReleasedVersion
            || (pcr.IsCustomRule && pcr.CustomApprovalStatus != PcrCustomApprovalStatus.Approved.ToString())
            || !SourceDocumentIntegrity.TryNormalizeSha256(pcr.OriginalDocumentSha256, out _)
            || string.IsNullOrWhiteSpace(pcr.OriginalDocumentObjectKey)
            || !string.Equals(pcr.OriginalDocumentScanStatus, "Clean", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pcr.ProductCategoryPatterns)
            || string.IsNullOrWhiteSpace(pcr.FunctionalUnitPattern)
            || string.IsNullOrWhiteSpace(pcr.DeclaredUnitCode)
            || !hasControlledDeclaredUnit
            || string.IsNullOrWhiteSpace(pcr.SystemBoundaryCode)
            || !string.Equals(pcr.FormulaRuleSetVersion, ActivityEmissionFormula.PcrFormulaRuleSetV1, StringComparison.Ordinal)
            || await db.PcrStageRules.CountAsync(item => item.PcrVersionId == pcr.Id, cancellationToken) != Enum.GetValues<LifecycleStage>().Length)
        {
            return OperationResult.Invalid("pcr", "只有已核准、原始文件驗證成功且規則完整的 PCR 草稿可發布。");
        }

        pcr.PublicationStatus = PcrPublicationStatus.Published.ToString();
        pcr.PublishedAt = DateTimeOffset.UtcNow;
        var publishedPredecessors = await db.PcrVersions
            .Where(item => item.RuleSetId == pcr.RuleSetId && item.Id != pcr.Id && item.VersionNumber < pcr.VersionNumber
                && item.PublicationStatus == PcrPublicationStatus.Published.ToString())
            .ToArrayAsync(cancellationToken);
        foreach (var predecessor in publishedPredecessors)
        {
            predecessor.DeprecatedAt = DateTimeOffset.UtcNow;
            predecessor.DeprecationReason = $"由 {pcr.RegistrationNumber} 第 {pcr.VersionNumber} 版取代。";
            Audit(context, "pcr.version.superseded", predecessor.Id);
        }
        Audit(context, "pcr.version.published", pcr.Id);
        return await SaveAsync(context, "PublishPcr", pcr.Id, OperationResult.Success("PCR 版本已發布。"), cancellationToken);
    }

    public async Task<OperationResult> WithdrawAsync(OperationContext context, Guid pcrVersionId, uint rowVersion, CancellationToken cancellationToken)
    {
        if (!await authorizer.IsAllowedAsync(context, OrganizationPermission.ManageFactors, requireMfa: true)) return OperationResult.Forbidden;
        var pcr = await db.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null) return OperationResult.NotFound;
        if (rowVersion != pcr.RowVersion) return await StaleAsync(context, "WithdrawPcr", pcr.Id, cancellationToken);
        if (!string.Equals(pcr.PublicationStatus, PcrPublicationStatus.Published.ToString(), StringComparison.Ordinal))
        {
            return OperationResult.Invalid("pcr", "只有已發布 PCR 版本可撤回。");
        }

        pcr.PublicationStatus = PcrPublicationStatus.Withdrawn.ToString();
        pcr.WithdrawnAt = DateTimeOffset.UtcNow;
        Audit(context, "pcr.version.withdrawn", pcr.Id);
        return await SaveAsync(context, "WithdrawPcr", pcr.Id, OperationResult.Success("PCR 版本已撤回；歷史計算不受影響。"), cancellationToken);
    }

    private Guid OrganizationId => scope.OrganizationId ?? throw new InvalidOperationException("缺少目前組織範圍。");

    private void Audit(OperationContext context, string action, Guid id) =>
        OperationAudit.AppendTracked(db, context, OrganizationId, action, ResourceType, id);

    private async Task<OperationResult> StaleAsync(OperationContext context, string operation, Guid id, CancellationToken cancellationToken)
    {
        await OperationAudit.RecordStaleRejectionAsync(db, context, OrganizationId, operation, ResourceType, id, cancellationToken);
        return OperationResult.Conflict;
    }

    private async Task<OperationResult> SaveAsync(
        OperationContext context, string operation, Guid id, OperationResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await StaleAsync(context, operation, id, cancellationToken);
        }
    }

    private static bool TryBuildStageRules(
        Guid organizationId,
        Guid pcrVersionId,
        IReadOnlyList<PcrStageInput> inputs,
        out PcrStageRuleRecord[] rules,
        out string error)
    {
        var allowedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(ActivityDataRecord.Name),
            nameof(ActivityDataRecord.SupplierOrScenario),
            nameof(ActivityDataRecord.EquipmentCategory),
            nameof(ActivityDataRecord.DataSourceType),
            nameof(ActivityDataRecord.DataProvider),
            nameof(ActivityDataRecord.CollectionMethod),
            nameof(ActivityDataRecord.SourceReference),
            nameof(ActivityDataRecord.RawValue),
            nameof(ActivityDataRecord.RawUnitCode),
            nameof(ActivityDataRecord.PeriodStart),
            nameof(ActivityDataRecord.PeriodEnd),
            nameof(ActivityDataRecord.FactorVersionId),
            nameof(ActivityDataRecord.AllocationFactor),
            nameof(ActivityDataRecord.DataQuality),
            nameof(ActivityDataRecord.EvidenceSha256)
        };
        var result = new List<PcrStageRuleRecord>();
        foreach (var input in inputs)
        {
            if (!Enum.TryParse<PcrStageRequirement>(input.Requirement, true, out var requirement))
            {
                rules = [];
                error = $"{InventoryDisplayNames.Stage(input.Stage)}的必要性設定無效。";
                return false;
            }

            var kindValues = PcrRuleSetMapper.SplitCsv(input.Kinds);
            if (kindValues.Count == 0
                || kindValues.Any(value => !Enum.TryParse<ActivityDataKind>(value, true, out _)))
            {
                rules = [];
                error = $"{InventoryDisplayNames.Stage(input.Stage)}的活動類型清單無效。";
                return false;
            }

            var requiredFields = PcrRuleSetMapper.SplitCsv(input.RequiredFields);
            if (requiredFields.Any(field => !allowedFields.Contains(field)))
            {
                rules = [];
                error = $"{InventoryDisplayNames.Stage(input.Stage)}含有未受控的必填欄位。";
                return false;
            }

            result.Add(new PcrStageRuleRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                PcrVersionId = pcrVersionId,
                LifecycleStage = (int)input.Stage,
                Requirement = requirement.ToString(),
                PermittedActivityKindsCsv = string.Join(
                    ",",
                    kindValues.Select(value => Enum.Parse<ActivityDataKind>(value, true).ToString())),
                RequiredFieldsCsv = string.Join(",", requiredFields)
            });
        }

        rules = result.ToArray();
        error = string.Empty;
        return true;
    }
}
