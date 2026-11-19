using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Application.Exports;
using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Organizations;
using CarbonFootprint.Application.Products;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Readiness;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Domain.Modules.Units;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Security;
using CarbonFootprint.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class WorkspaceModel : PageModel
{
    private readonly IPcrGovernance _pcrGovernance;
    private readonly IFactorGovernance _factorGovernance;
    private readonly IInventoryWorkflow _inventoryWorkflow;
    private readonly IInventoryReadiness _readiness;
    private readonly IActivityData _activities;

    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;
    private readonly OrganizationOnboardingService _onboardingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IInventoryCalculation _calculation;
    private readonly IAuthorizationService _authorizationService;
    private readonly MoenvFactorSynchronizationService _moenvFactorSynchronizationService;
    private readonly IOrganizationAdministration _organization;
    private readonly IProductCatalogue _products;
    private readonly IInventoryProjects _inventories;
    private readonly IInventoryExport _export;

    public WorkspaceModel(
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        OrganizationOnboardingService onboardingService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IInventoryCalculation calculation,
        IAuthorizationService authorizationService,
        MoenvFactorSynchronizationService moenvFactorSynchronizationService,
        IPcrGovernance pcrGovernance,
        IFactorGovernance factorGovernance,
        IInventoryWorkflow inventoryWorkflow,
        IInventoryReadiness readiness,
        IActivityData activities,
        IOrganizationAdministration organization,
        IProductCatalogue products,
        IInventoryProjects inventories,
        IInventoryExport export)
    {
        _inventories = inventories;
        _export = export;
        _organization = organization;
        _products = products;
        _activities = activities;
        _inventoryWorkflow = inventoryWorkflow;
        _readiness = readiness;
        _pcrGovernance = pcrGovernance;
        _factorGovernance = factorGovernance;
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _onboardingService = onboardingService;
        _userManager = userManager;
        _signInManager = signInManager;
        _calculation = calculation;
        _authorizationService = authorizationService;
        _moenvFactorSynchronizationService = moenvFactorSynchronizationService;
    }

    public Guid? OrganizationId => _organizationScope.OrganizationId;
    public string OrganizationName { get; private set; } = string.Empty;
    public bool CanEditInventory { get; private set; }
    public bool CanCalculate { get; private set; }
    public bool CanReviewInventory { get; private set; }
    public CanonicalManifest.ReportingRules? LatestReportingRules { get; private set; }
    public bool CanEditData { get; private set; }
    public bool CanManageFactors { get; private set; }
    public bool HasVerifiedMfa { get; private set; }

    public IReadOnlyList<ProductVersionRecord> ProductVersions { get; private set; } = [];

    public IReadOnlyList<FacilityRecord> Facilities { get; private set; } = [];

    public OrganizationMailSettingsRecord? MailSettings { get; private set; }

    public string MailHost => MailSettings?.Host ?? "localhost";

    public int MailPort => MailSettings?.Port ?? 1025;

    public bool MailEnableSsl => MailSettings?.EnableSsl ?? false;

    public string MailUsername => MailSettings?.Username ?? string.Empty;

    public string MailFromAddress => MailSettings?.FromAddress ?? "no-reply@carbon-footprint.local";

    public string MailFromName => MailSettings?.FromName ?? "碳足跡系統";

    public bool MailPasswordConfigured => !string.IsNullOrWhiteSpace(MailSettings?.EncryptedPassword);

    public bool CanManageOrganization { get; private set; }

    public IReadOnlyList<OrganizationMembershipRecord> Memberships { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> MemberNames { get; private set; } = new Dictionary<Guid, string>();

    public IReadOnlyList<OrganizationInvitationRecord> Invitations { get; private set; } = [];

    public IReadOnlyList<InventoryProjectVersionRecord> InventoryProjects { get; private set; } = [];

    public IReadOnlyList<LifecycleStageDeclarationRecord> StageDeclarations { get; private set; } = [];

    public IReadOnlyList<EmissionFactorVersionRecord> Factors { get; private set; } = [];

    public IReadOnlyList<EmissionFactorVersionRecord> SelectableFactors { get; private set; } = [];

    public IReadOnlyList<PcrVersionRecord> PcrVersions { get; private set; } = [];

    public IReadOnlyList<PcrStageRuleRecord> PcrStageRules { get; private set; } = [];

    public IReadOnlyList<PcrVersionRecord> SelectablePcrVersions { get; private set; } = [];

    public ReadinessReport? Readiness { get; private set; }

    public static string StageSlug(LifecycleStage stage) => stage switch
    {
        LifecycleStage.Manufacturing => "manufacturing",
        LifecycleStage.Distribution => "distribution",
        LifecycleStage.Use => "use",
        LifecycleStage.EndOfLife => "end-of-life",
        _ => "raw-material"
    };

    public PcrImpactReason PcrImpact(PcrVersionRecord pcr, InventoryProjectVersionRecord project) =>
        PcrImpactAnalysis.Classify(
            // Raw status: a withdrawn version stays withdrawn even when its source file is no longer verified.
            PcrRuleSetMapper.ToRuleSet(pcr, PcrStageRules.Where(rule => rule.PcrVersionId == pcr.Id))
                with { PublicationStatus = Enum.Parse<PcrPublicationStatus>(pcr.PublicationStatus) },
            project.PeriodEnd);

    public IReadOnlyDictionary<Guid, int> PcrAffectedProjectCounts { get; private set; } =
        new Dictionary<Guid, int>();

    public IReadOnlyDictionary<Guid, IReadOnlyList<InventoryProjectVersionRecord>> PcrAffectedProjects { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<InventoryProjectVersionRecord>>();

    /// <summary>Active activities of the selected project; only the selected stage on the lifecycle section.</summary>
    public IReadOnlyList<ActivityDataRecord> Activities { get; private set; } = [];

    public IReadOnlyDictionary<Guid, EmissionFactorVersionRecord> ReferencedFactors { get; private set; } =
        new Dictionary<Guid, EmissionFactorVersionRecord>();

    public int InventoryProjectTotal { get; private set; }
    public int SavedStageCount { get; private set; }
    public bool HasRun { get; private set; }
    public int LatestLineTotal { get; private set; }
    public int SelectableFactorTotal { get; private set; }
    public int FactorTotal { get; private set; }
    public int FactorPageCount { get; private set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int FactorPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? FactorQuery { get; set; }

    public IReadOnlyList<EvidenceFileRecord> EvidenceFiles { get; private set; } = [];

    public IReadOnlyList<UnitRecord> Units { get; private set; } = [];

    public IReadOnlyList<CalculationRunRecord> Runs { get; private set; } = [];

    public IReadOnlyList<CalculationLineRecord> LatestLines { get; private set; } = [];

    public IReadOnlyList<CalculationWarningRecord> LatestWarnings { get; private set; } = [];

    public CalculationRunDifference? LatestDifference { get; private set; }

    public bool? LatestManifestHashValid { get; private set; }

    public IReadOnlySet<Guid> CurrentRunProjectIds { get; private set; } = new HashSet<Guid>();

    public IReadOnlySet<Guid> PendingFormulaRunProjectIds { get; private set; } = new HashSet<Guid>();

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; } = "governance";

    [BindProperty(SupportsGet = true)]
    public string? Stage { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ProjectVersionId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ActivityId { get; set; }

    public ActivityDataRecord? EditingActivity { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Section = NormalizeSection(Section);
        if (Section == "lifecycle")
        {
            var normalizedStage = NormalizeStageSlug(Stage);
            if (!string.Equals(Stage, normalizedStage, StringComparison.Ordinal))
            {
                return RedirectToPage(new
                {
                    section = "lifecycle",
                    stage = normalizedStage,
                    projectVersionId = ProjectVersionId
                });
            }

            Stage = normalizedStage;
        }
        else if (Section == "settings")
        {
            Stage = "mail";
        }
        else
        {
            Stage = null;
        }

        var requestedProjectId = ProjectVersionId;
        await LoadAsync(cancellationToken);
        if (requestedProjectId.HasValue && InventoryProjects.All(project => project.Id != requestedProjectId))
        {
            return NotFound();
        }
        if (ActivityId.HasValue && (Section != "lifecycle" || EditingActivity is null))
        {
            return NotFound();
        }
        if (EditingActivity is { } activity)
        {
            var values = new Dictionary<string, string?>
            {
                ["activityId"] = activity.Id.ToString(),
                ["activityKind"] = activity.ActivityKind,
                ["activityName"] = "__other__",
                ["activityNameOther"] = activity.Name,
                ["equipmentCategory"] = "__other__",
                ["equipmentCategoryOther"] = activity.EquipmentCategory,
                ["dataSourceType"] = "__other__",
                ["dataSourceTypeOther"] = activity.DataSourceType,
                ["dataProviderType"] = "__other__",
                ["dataProviderOther"] = activity.DataProvider,
                ["collectionMethod"] = "__other__",
                ["collectionMethodOther"] = activity.CollectionMethod,
                ["supplierOrScenario"] = activity.SupplierOrScenario.Split("計算基礎：", 2, StringSplitOptions.None)[0].TrimEnd('｜'),
                ["sourceReference"] = activity.SourceReference,
                ["rawValue"] = activity.RawValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["rawUnitCode"] = activity.RawUnitCode,
                ["canonicalUnitCode"] = activity.CanonicalUnitCode,
                ["factorVersionId"] = activity.FactorVersionId.ToString(),
                ["allocationFactor"] = activity.AllocationFactor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["isEstimated"] = activity.IsEstimated ? "true" : "false",
                ["activityEstimationReason"] = activity.EstimationReason,
                ["dataQuality"] = activity.DataQuality
            };
            using var inputs = JsonDocument.Parse(activity.FormulaInputsJson);
            foreach (var (input, field) in new[] { ("distanceKm", "transportDistanceKm"), ("weightKg", "transportWeightKg"),
                ("lifetime", "useLifetime"), ("frequency", "useFrequency"), ("consumptionPerUse", "useConsumptionPerUse") })
            {
                if (inputs.RootElement.TryGetProperty(input, out var value))
                {
                    values[field] = value.GetRawText();
                }
            }
            foreach (var (key, value) in values)
            {
                ModelState.SetModelValue(key, value, value);
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateOrganizationAsync(string organizationName, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User)
            ?? throw new InvalidOperationException("找不到目前使用者。");
        var stampResult = await _userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "無法更新登入安全狀態，請稍後再試。");
            await LoadAsync(cancellationToken);
            return Page();
        }
        try
        {
            await _onboardingService.CreateAsync(user, organizationName, cancellationToken,
                HttpContext.TraceIdentifier, AuditWriter.RequestSessionReference(HttpContext));
            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "組織已建立。";
            return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await _signInManager.RefreshSignInAsync(user);
            ModelState.AddModelError("organizationName", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSaveMailSettingsAsync(
        string host,
        int port,
        bool enableSsl,
        string? username,
        string? password,
        string fromAddress,
        string fromName,
        uint rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await _organization.SaveMailSettingsAsync(Operation(),
            new MailSettingsInput(host, port, enableSsl, username, password, fromAddress, fromName), rowVersion, cancellationToken);
        return await CompleteMailAsync(result, cancellationToken);
    }

    public async Task<IActionResult> OnPostTestMailAsync(string recipient, CancellationToken cancellationToken) =>
        await CompleteMailAsync(await _organization.SendTestMailAsync(Operation(), recipient, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostCreateFacilityAsync(
        string facilityCode,
        string facilityName,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _products.CreateFacilityAsync(Operation(), facilityCode, facilityName, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostInviteMemberAsync(
        string invitationEmail,
        OrganizationRole invitationRole,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _organization.InviteMemberAsync(Operation(), invitationEmail, invitationRole,
                token => Url.Page("/AcceptInvitation", pageHandler: null, values: new { token }, protocol: Request.Scheme)
                    ?? throw new InvalidOperationException("無法建立邀請連結。"),
                cancellationToken),
            cancellationToken);

    public async Task<IActionResult> OnPostRevokeInvitationAsync(Guid invitationId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _organization.RevokeInvitationAsync(Operation(), invitationId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostRevokeMemberAsync(Guid membershipId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _organization.RevokeMemberAsync(Operation(), membershipId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostCreateProductAsync(
        string productName,
        string categoryCode,
        Guid facilityId,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _products.CreateProductAsync(Operation(), productName, categoryCode, facilityId, cancellationToken), cancellationToken);

    // SMTP forms live on the settings/mail tab; success and validation errors return there.
    private async Task<IActionResult> CompleteMailAsync(OperationResult result, CancellationToken cancellationToken)
    {
        if (result.Status is OperationStatus.Succeeded or OperationStatus.Invalid)
        {
            Section = "settings";
            Stage = "mail";
        }
        return await CompleteAsync(result, cancellationToken, stage: Stage);
    }

    public async Task<IActionResult> OnPostCreateInventoryAsync(
        Guid productVersionId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string functionalUnit,
        string declaredUnit,
        string systemBoundary,
        string allocationMethod,
        string allocationReason,
        string exclusions,
        string assumptions,
        string estimationReason,
        Guid pcrVersionId,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _inventories.CreateAsync(Operation(), new InventoryInput(
                productVersionId, periodStart, periodEnd, functionalUnit, declaredUnit, systemBoundary, allocationMethod,
                allocationReason, exclusions, assumptions, estimationReason, pcrVersionId), cancellationToken),
            cancellationToken);

    public async Task<IActionResult> OnPostSetStageApplicabilityAsync(
        Guid stageDeclarationId,
        bool isApplicable,
        string reason,
        uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _inventories.SetStageApplicabilityAsync(
            Operation(), stageDeclarationId, isApplicable, reason, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostCreatePcrAsync(
        string registrationNumber,
        int versionNumber,
        string title,
        DateOnly? approvalDate,
        DateOnly? validFrom,
        DateOnly? validTo,
        string sourceReference,
        string standardCode,
        string cccClassification,
        string pcrApplicability,
        string ruleRequirements,
        string productCategoryPatterns,
        string functionalUnitPattern,
        string declaredUnitCode,
        string systemBoundaryCode,
        string permittedAllocationMethods,
        decimal cutoffThresholdPercent,
        string formulaRuleSetVersion,
        int roundingDecimalPlaces,
        string reportingRequirements,
        bool isCustomRule,
        string customRuleJustification,
        IFormFile originalDocument,
        string rawMaterialRequirement,
        string rawMaterialKinds,
        string rawMaterialRequiredFields,
        string manufacturingRequirement,
        string manufacturingKinds,
        string manufacturingRequiredFields,
        string distributionRequirement,
        string distributionKinds,
        string distributionRequiredFields,
        string useRequirement,
        string useKinds,
        string useRequiredFields,
        string endOfLifeRequirement,
        string endOfLifeKinds,
        string endOfLifeRequiredFields,
        CancellationToken cancellationToken)
    {
        await using var content = originalDocument is { Length: > 0 } ? originalDocument.OpenReadStream() : null;
        var input = new PcrInput(registrationNumber, versionNumber, title, approvalDate, validFrom, validTo, sourceReference, standardCode,
            cccClassification, pcrApplicability, ruleRequirements, productCategoryPatterns, functionalUnitPattern, declaredUnitCode,
            systemBoundaryCode, permittedAllocationMethods, cutoffThresholdPercent, formulaRuleSetVersion, roundingDecimalPlaces,
            reportingRequirements, isCustomRule, customRuleJustification,
            [
                new PcrStageInput(LifecycleStage.RawMaterial, rawMaterialRequirement, rawMaterialKinds, rawMaterialRequiredFields),
                new PcrStageInput(LifecycleStage.Manufacturing, manufacturingRequirement, manufacturingKinds, manufacturingRequiredFields),
                new PcrStageInput(LifecycleStage.Distribution, distributionRequirement, distributionKinds, distributionRequiredFields),
                new PcrStageInput(LifecycleStage.Use, useRequirement, useKinds, useRequiredFields),
                new PcrStageInput(LifecycleStage.EndOfLife, endOfLifeRequirement, endOfLifeKinds, endOfLifeRequiredFields)
            ]);
        var document = new EvidenceUpload(content, originalDocument?.Length ?? 0, originalDocument?.FileName ?? string.Empty,
            originalDocument?.ContentType ?? string.Empty);
        return await CompleteAsync(await _pcrGovernance.CreateAsync(Operation(), input, document, cancellationToken), cancellationToken);
    }

    public async Task<IActionResult> OnPostReviewPcrAsync(Guid pcrVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _pcrGovernance.ReviewAsync(Operation(), pcrVersionId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostRejectPcrAsync(Guid pcrVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _pcrGovernance.RejectAsync(Operation(), pcrVersionId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostPublishPcrAsync(Guid pcrVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _pcrGovernance.PublishAsync(Operation(), pcrVersionId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostWithdrawPcrAsync(Guid pcrVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _pcrGovernance.WithdrawAsync(Operation(), pcrVersionId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostCreateFactorAsync(
        string factorName,
        decimal? factorValue,
        string denominatorUnitCode,
        string factorSourceType,
        string factorSourceTypeOther,
        string factorGeography,
        string factorGeographyOther,
        DateOnly? factorValidFrom,
        DateOnly? factorValidTo,
        string sourceDatasetVersion,
        string licenseCode,
        string factorSourceName,
        string factorSourceReference,
        string datasetName,
        string factorOriginalDocumentName,
        string factorApplicability,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _factorGovernance.CreateAsync(Operation(), new FactorInput(
                factorName, factorValue, denominatorUnitCode, factorSourceType, factorSourceTypeOther, factorGeography, factorGeographyOther,
                factorValidFrom, factorValidTo, sourceDatasetVersion, licenseCode, factorSourceName, factorSourceReference, datasetName,
                factorOriginalDocumentName, factorApplicability), cancellationToken),
            cancellationToken);

    public async Task<IActionResult> OnPostSyncMoenvFactorsAsync(CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        try
        {
            var organizationId = RequireOrganization();
            Guid? actorId = Guid.TryParse(_userManager.GetUserId(User), out var userId) ? userId : null;
            var result = await _moenvFactorSynchronizationService.SynchronizeOrganizationAsync(
                organizationId,
                actorId,
                HttpContext.TraceIdentifier,
                cancellationToken);
            StatusMessage = $"環境部係數同步完成：新增並發布 {result.CreatedCount} 筆、啟用舊草稿 {result.PublishedExistingCount} 筆、未變更 {result.UnchangedCount} 筆、略過 {result.SkippedCount} 筆無法對應的資料。";
            return RedirectToPage(new { section = "factors", projectVersionId = ProjectVersionId });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ModelState.AddModelError("factor", $"環境部係數同步失敗：{exception.Message}");
            Section = "factors";
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostReviewFactorAsync(Guid factorVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _factorGovernance.ReviewAsync(Operation(), factorVersionId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostPublishFactorAsync(Guid factorVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _factorGovernance.PublishAsync(Operation(), factorVersionId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostWithdrawFactorAsync(Guid factorVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _factorGovernance.WithdrawAsync(Operation(), factorVersionId, rowVersion, cancellationToken), cancellationToken);

    public async Task<IActionResult> OnPostSupersedeFactorAsync(
        Guid factorVersionId,
        decimal? newFactorValue,
        string newSourceDatasetVersion,
        string newFactorSourceReference,
        string newOriginalDocumentName,
        string newApplicability,
        uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(await _factorGovernance.SupersedeAsync(Operation(), factorVersionId, new FactorSupersedeInput(
                newFactorValue, newSourceDatasetVersion, newFactorSourceReference, newOriginalDocumentName, newApplicability),
                rowVersion, cancellationToken),
            cancellationToken);

    public async Task<IActionResult> OnPostAddActivityAsync(
        Guid inventoryProjectVersionId,
        LifecycleStage lifecycleStage,
        ActivityDataKind activityKind,
        string activityName,
        string activityNameOther,
        string supplierOrScenario,
        string equipmentCategory,
        string equipmentCategoryOther,
        string dataSourceType,
        string dataSourceTypeOther,
        string dataProviderType,
        string dataProviderOther,
        string collectionMethod,
        string collectionMethodOther,
        string sourceReference,
        decimal? rawValue,
        decimal? transportDistanceKm,
        decimal? transportWeightKg,
        decimal? useLifetime,
        decimal? useFrequency,
        decimal? useConsumptionPerUse,
        string rawUnitCode,
        string canonicalUnitCode,
        Guid factorVersionId,
        decimal? allocationFactor,
        bool isEstimated,
        string activityEstimationReason,
        string dataQuality,
        CancellationToken cancellationToken,
        Guid? activityId = null)
        => await CompleteAsync(await _activities.SaveAsync(Operation(), new ActivityInput(
                inventoryProjectVersionId, lifecycleStage, activityKind, activityName, activityNameOther, supplierOrScenario,
                equipmentCategory, equipmentCategoryOther, dataSourceType, dataSourceTypeOther, dataProviderType, dataProviderOther,
                collectionMethod, collectionMethodOther, sourceReference, rawValue, transportDistanceKm, transportWeightKg,
                useLifetime, useFrequency, useConsumptionPerUse, rawUnitCode, canonicalUnitCode, factorVersionId, allocationFactor,
                isEstimated, activityEstimationReason, dataQuality), activityId, cancellationToken),
            cancellationToken, stage: Stage);

    public async Task<IActionResult> OnPostRemoveActivityAsync(Guid activityId, CancellationToken cancellationToken)
    {
        var result = await _activities.RetireAsync(Operation(), activityId, cancellationToken);
        // Removal is a single-button post: a lost race answers 409 without re-rendering the page.
        return result.Status == OperationStatus.Conflict
            ? StatusCode(StatusCodes.Status409Conflict)
            : await CompleteAsync(result, cancellationToken, stage: Stage);
    }

    public async Task<IActionResult> OnPostUploadEvidenceAsync(
        Guid activityDataId,
        IFormFile evidenceFile,
        CancellationToken cancellationToken)
    {
        await using var content = evidenceFile is { Length: > 0 } ? evidenceFile.OpenReadStream() : null;
        return await CompleteAsync(await _activities.AttachEvidenceAsync(Operation(), activityDataId,
                new EvidenceUpload(content, evidenceFile?.Length ?? 0, evidenceFile?.FileName ?? string.Empty, evidenceFile?.ContentType ?? string.Empty),
                cancellationToken),
            cancellationToken, stage: Stage);
    }

    public async Task<IActionResult> OnPostSubmitInventoryAsync(Guid inventoryProjectVersionId, uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(
            await _inventoryWorkflow.SubmitAsync(Operation(), inventoryProjectVersionId, rowVersion, ReadAcknowledgements(), cancellationToken),
            cancellationToken, inventoryProjectVersionId);

    public async Task<IActionResult> OnPostReviewInventoryAsync(
        Guid inventoryProjectVersionId,
        InventoryWorkflowStatus decision,
        string? reviewComment,
        uint rowVersion,
        CancellationToken cancellationToken) =>
        await CompleteAsync(
            await _inventoryWorkflow.ReviewAsync(Operation(), inventoryProjectVersionId, decision, reviewComment, rowVersion, cancellationToken),
            cancellationToken, inventoryProjectVersionId);

    public async Task<IActionResult> OnGetExportExcelAsync(
        Guid projectVersionId,
        CancellationToken cancellationToken,
        Guid? runId = null)
    {
        var export = await _export.ExportExcelAsync(Operation(), projectVersionId, runId, cancellationToken);
        return export.Status switch
        {
            OperationStatus.Succeeded => File(export.File!.Content, export.File.ContentType, export.File.FileName),
            OperationStatus.Forbidden => Forbid(),
            OperationStatus.NotFound => NotFound(),
            OperationStatus.Conflict => RunExportValidation.ConflictResult(),
            _ => BadRequest()
        };
    }

    public async Task<IActionResult> OnPostCalculateAsync(Guid inventoryProjectVersionId, CancellationToken cancellationToken) =>
        await CompleteAsync(await _calculation.CalculateAsync(Operation(), inventoryProjectVersionId, cancellationToken),
            cancellationToken, inventoryProjectVersionId);

    private const int MaxAffectedProjects = 500;
    public const int FactorPageSize = WorkspaceQueries.FactorPageSize;
    public const int MaxListedProjects = WorkspaceQueries.MaxListedProjects;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!OrganizationId.HasValue)
        {
            return;
        }

        OrganizationName = await _dbContext.Organizations.AsNoTracking()
            .Select(item => item.Name).SingleAsync(cancellationToken);
        HasVerifiedMfa = await IsMfaEnabledAsync();
        CanEditData = await IsAllowedAsync(OrganizationPermission.EditInventory);
        CanEditInventory = CanEditData;
        CanCalculate = await IsAllowedAsync(OrganizationPermission.CreateCalculationRun) && HasVerifiedMfa;
        CanReviewInventory = await IsAllowedAsync(OrganizationPermission.ReviewInventory);
        CanManageFactors = await IsAllowedAsync(OrganizationPermission.ManageFactors);
        CanManageOrganization = await IsAllowedAsync(OrganizationPermission.ManageOrganization);

        var section = Section;
        var showsPcrRules = section is "setup" or "pcr" or "inventory";
        var showsProductForms = section is "setup" or "product" or "inventory";
        var needsUnits = section is "setup" or "product" or "pcr" or "inventory" or "factors" or "lifecycle";

        if (section == "settings")
        {
            MailSettings = await _dbContext.OrganizationMailSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (section == "governance")
        {
            Memberships = await _dbContext.OrganizationMemberships.AsNoTracking().OrderBy(item => item.CreatedAt).ToArrayAsync(cancellationToken);
            var memberIds = Memberships.Select(item => item.UserId).ToArray();
            MemberNames = await _dbContext.Users.AsNoTracking().Where(item => memberIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id,
                    item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.UserName ?? string.Empty : item.DisplayName,
                    cancellationToken);
            Invitations = await _dbContext.OrganizationInvitations.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        }
        if (section is "setup" or "product")
        {
            Facilities = await _dbContext.Facilities.AsNoTracking().OrderBy(item => item.Code).ToArrayAsync(cancellationToken);
        }

        var picker = await new WorkspaceQueries(_dbContext).ListProjectsAsync(ProjectVersionId, cancellationToken);
        InventoryProjectTotal = picker.Total;
        InventoryProjects = picker.Projects;
        if (!ProjectVersionId.HasValue || InventoryProjects.All(item => item.Id != ProjectVersionId.Value))
        {
            ProjectVersionId = InventoryProjects.FirstOrDefault()?.Id;
        }
        var selectedProject = InventoryProjects.FirstOrDefault(item => item.Id == ProjectVersionId);
        var canEditSelectedProject = selectedProject is not null
            && InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(selectedProject.WorkflowStatus));
        CanEditInventory &= canEditSelectedProject;
        CanCalculate &= canEditSelectedProject;

        if (showsProductForms)
        {
            ProductVersions = await _dbContext.ProductVersions.AsNoTracking().OrderBy(item => item.NameZhTw).ToArrayAsync(cancellationToken);
        }
        else
        {
            var listedProductIds = InventoryProjects.Select(item => item.ProductVersionId).Distinct().ToArray();
            ProductVersions = await _dbContext.ProductVersions.AsNoTracking()
                .Where(item => listedProductIds.Contains(item.Id)).OrderBy(item => item.NameZhTw).ToArrayAsync(cancellationToken);
        }

        if (showsPcrRules)
        {
            PcrVersions = await _dbContext.PcrVersions.AsNoTracking().OrderBy(item => item.RegistrationNumber).ThenByDescending(item => item.VersionNumber).ToArrayAsync(cancellationToken);
            PcrStageRules = await _dbContext.PcrStageRules.AsNoTracking().OrderBy(item => item.LifecycleStage).ToArrayAsync(cancellationToken);
            SelectablePcrVersions = PcrVersions
                .Where(item => PcrRuleSetMapper.ToRuleSet(
                        item,
                        PcrStageRules.Where(rule => rule.PcrVersionId == item.Id))
                    .IsPublishedAndApproved)
                .ToArray();
        }
        else if (selectedProject?.PcrVersionId is { } selectedPcrId)
        {
            PcrVersions = await _dbContext.PcrVersions.AsNoTracking().Where(item => item.Id == selectedPcrId).ToArrayAsync(cancellationToken);
        }
        if (section is "setup" or "pcr")
        {
            PcrAffectedProjectCounts = await _dbContext.InventoryProjectVersions.AsNoTracking()
                .Where(item => item.PcrVersionId != null)
                .GroupBy(item => item.PcrVersionId!.Value)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);
            PcrAffectedProjects = (await _dbContext.InventoryProjectVersions.AsNoTracking()
                    .Where(item => item.PcrVersionId != null)
                    .OrderBy(item => item.PeriodEnd).ThenBy(item => item.Id)
                    .Take(MaxAffectedProjects)
                    .ToArrayAsync(cancellationToken))
                .GroupBy(item => item.PcrVersionId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<InventoryProjectVersionRecord>)group.ToArray());
        }

        if (showsPcrRules && ProjectVersionId.HasValue)
        {
            StageDeclarations = await _dbContext.LifecycleStageDeclarations.AsNoTracking()
                .Where(item => item.InventoryProjectVersionId == ProjectVersionId.Value)
                .OrderBy(item => item.LifecycleStage).ToArrayAsync(cancellationToken);
        }

        if (section == "factors")
        {
            var page = await new WorkspaceQueries(_dbContext).ListFactorsAsync(FactorQuery, FactorPage, cancellationToken);
            (Factors, FactorTotal, FactorPageCount, FactorPage) = (page.Items, page.Total, page.PageCount, page.Page);
        }

        if (section == "lifecycle" && ProjectVersionId.HasValue)
        {
            var stage = new WorkspaceQueries(_dbContext).LoadStageAsync(
                ProjectVersionId.Value, Stage is null ? null : StageFromSlug(Stage), CanEditInventory ? ActivityId : null, cancellationToken);
            var data = await stage;
            Activities = data.Activities;
            EditingActivity = data.EditingActivity;
            EvidenceFiles = data.Evidence;
            ReferencedFactors = data.ReferencedFactors;
            SelectableFactors = data.SelectableFactors;
            SelectableFactorTotal = data.SelectableFactorTotal;
        }

        if (needsUnits)
        {
            var selectedUnitCatalogueVersion = ProjectVersionId.HasValue
                ? GetActivityUnitCatalogueVersion(await _dbContext.ActivityData.AsNoTracking()
                    .Where(item => item.InventoryProjectVersionId == ProjectVersionId.Value && item.RetiredAt == null)
                    .Select(item => item.ConversionRuleVersion).Distinct().ToArrayAsync(cancellationToken))
                : UnitCatalogue.CurrentVersion;
            Units = await _dbContext.Units.AsNoTracking()
                .Where(item => item.CatalogueVersion == selectedUnitCatalogueVersion)
                .OrderBy(item => item.Code)
                .ToArrayAsync(cancellationToken);
        }

        if (section == "settings" || !ProjectVersionId.HasValue)
        {
            return;
        }

        // Final-submit panel (every non-settings section) needs only counts and flags for the selected project.
        var projectId = ProjectVersionId.Value;
        SavedStageCount = await _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == projectId && item.RetiredAt == null)
            .Select(item => item.LifecycleStage).Distinct().CountAsync(cancellationToken);
        var projectRuns = _dbContext.CalculationRuns.AsNoTracking().Where(item => item.ProjectVersionId == projectId);
        HasRun = await projectRuns.AnyAsync(cancellationToken);
        if (HasRun && await projectRuns.AnyAsync(
                item => item.RuleSetVersion == InventoryReadinessService.PendingStageFormulaRuleSetVersion, cancellationToken))
        {
            PendingFormulaRunProjectIds = new HashSet<Guid> { projectId };
        }

        if (section != "calculation")
        {
            return;
        }

        var currentRunProjectIds = new HashSet<Guid>();
        var assessment = await _readiness.AssessAsync(projectId, [], cancellationToken);
        Readiness = assessment.Report;
        if (assessment.CurrentRunError is null)
        {
            currentRunProjectIds.Add(projectId);
        }
        CurrentRunProjectIds = currentRunProjectIds;

        var results = await new WorkspaceQueries(_dbContext).LoadCalculationResultsAsync(projectId, cancellationToken);
        Runs = results.Runs;
        LatestReportingRules = results.ReportingRules;
        LatestManifestHashValid = results.ManifestHashValid;
        LatestLines = results.Lines;
        LatestLineTotal = results.LineTotal;
        LatestWarnings = results.Warnings;
        LatestDifference = results.Difference;
    }

    public async Task<IActionResult> OnGetReadinessAsync(Guid projectVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ViewInventory) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var project = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectVersionId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }
        var report = await _readiness.BuildReportAsync(project.Id, [], cancellationToken);
        return new JsonResult(new
        {
            canSubmit = report.CanSubmit,
            blockingCount = report.BlockingCount,
            asOf = report.AsOf,
            sha256 = report.Sha256(),
            findings = report.Findings
        });
    }

    private IReadOnlyList<ReadinessAcknowledgement> ReadAcknowledgements()
    {
        const int maxJustificationLength = 2000;
        if (!Request.HasFormContentType)
        {
            return [];
        }
        return Request.Form["acknowledge"]
            .Select(value => value?.Split('|', 2))
            .Where(parts => parts is { Length: 2 })
            .Select(parts =>
            {
                var justification = Request.Form[$"justification_{parts![0]}|{parts[1]}"].ToString().Trim();
                return new ReadinessAcknowledgement(
                    parts[0],
                    parts[1],
                    justification.Length > maxJustificationLength ? justification[..maxJustificationLength] : justification);
            })
            .ToArray();
    }

    private Guid RequireOrganization() => OrganizationId
        ?? throw new InvalidOperationException("請先建立組織。");

    private static LifecycleStage StageFromSlug(string slug) => slug switch
    {
        "manufacturing" => LifecycleStage.Manufacturing,
        "distribution" => LifecycleStage.Distribution,
        "use" => LifecycleStage.Use,
        "end-of-life" => LifecycleStage.EndOfLife,
        _ => LifecycleStage.RawMaterial
    };

    private static string NormalizeStageSlug(string? stage) => stage?.Trim().ToLowerInvariant() switch
    {
        "raw-material" => "raw-material",
        "manufacturing" => "manufacturing",
        "distribution" => "distribution",
        "use" => "use",
        "end-of-life" => "end-of-life",
        _ => "raw-material"
    };

    private static string GetActivityUnitCatalogueVersion(IEnumerable<ActivityDataRecord> activities) =>
        GetActivityUnitCatalogueVersion(activities.Select(item => item.ConversionRuleVersion));

    private static string GetActivityUnitCatalogueVersion(IEnumerable<string> versions) =>
        UnitCatalogue.ResolveVersion(versions);

    private static string NormalizeSection(string? section) => section?.Trim().ToLowerInvariant() switch
    {
        "governance" => "governance",
        "setup" => "setup",
        "product" => "product",
        "pcr" => "pcr",
        "inventory" => "inventory",
        "factors" => "factors",
        "lifecycle" => "lifecycle",
        "calculation" => "calculation",
        "settings" => "settings",
        _ => "governance"
    };

    private async Task<bool> IsAllowedAsync(OrganizationPermission permission)
    {
        var result = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            new OrganizationPermissionRequirement(permission));
        return result.Succeeded;
    }

    private async Task<bool> IsMfaEnabledAsync()
    {
        var result = await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            new MfaEnabledRequirement());
        return result.Succeeded;
    }

    private OperationContext Operation() => new(
        User,
        Guid.TryParse(_userManager.GetUserId(User), out var actorId) ? actorId : null,
        HttpContext.TraceIdentifier,
        AuditWriter.RequestSessionReference(HttpContext));

    private async Task<IActionResult> ShowConflictAsync(CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        ModelState.AddModelError("concurrency", "此資料已被其他人變更，您的操作未套用。請重新載入並核對最新內容後再試。");
        Response.StatusCode = StatusCodes.Status409Conflict;
        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task<IActionResult> CompleteAsync(
        OperationResult result, CancellationToken cancellationToken, Guid? projectVersionId = null, string? stage = null)
    {
        switch (result.Status)
        {
            case OperationStatus.Succeeded:
                if (!string.IsNullOrEmpty(result.Message))
                {
                    StatusMessage = result.Message;
                }
                return RedirectToPage(new { section = Section, stage, projectVersionId = result.ProjectVersionId ?? projectVersionId ?? ProjectVersionId });
            case OperationStatus.Forbidden:
                return Forbid();
            case OperationStatus.NotFound:
                return NotFound();
            case OperationStatus.Invalid:
                foreach (var detail in result.Details ?? [result.Message])
                {
                    ModelState.AddModelError(result.Key, detail);
                }
                await LoadAsync(cancellationToken);
                return Page();
            case OperationStatus.Conflict:
                return await ShowConflictAsync(cancellationToken);
            default:
                return BadRequest();
        }
    }
}
