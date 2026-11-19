using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Application.Exports;
using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Inventories;
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
using CarbonFootprint.Infrastructure.Evidence;
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
using Npgsql;

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class WorkspaceModel : PageModel
{
    private readonly IPcrGovernance _pcrGovernance;
    private readonly IFactorGovernance _factorGovernance;
    private readonly IInventoryWorkflow _inventoryWorkflow;
    private readonly IInventoryReadiness _readiness;
    private readonly IActivityData _activities;
    private const string CurrentUnitCatalogueVersion = "units-p0-v2";

    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;
    private readonly OrganizationOnboardingService _onboardingService;
    private readonly OrganizationInvitationService _invitationService;
    private readonly SmtpEmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IInventoryCalculation _calculation;
    private readonly IInventorySnapshotReader _snapshotReader;
    private readonly IAuthorizationService _authorizationService;
    private readonly EvidenceStorageService _evidenceStorageService;
    private readonly MoenvFactorSynchronizationService _moenvFactorSynchronizationService;
    private readonly IDataProtector _mailPasswordProtector;
    private readonly CalculationBuildProvenance _buildProvenance;

    public WorkspaceModel(
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        OrganizationOnboardingService onboardingService,
        OrganizationInvitationService invitationService,
        SmtpEmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IInventoryCalculation calculation,
        IInventorySnapshotReader snapshotReader,
        IAuthorizationService authorizationService,
        EvidenceStorageService evidenceStorageService,
        MoenvFactorSynchronizationService moenvFactorSynchronizationService,
        IDataProtectionProvider dataProtectionProvider,
        CalculationBuildProvenance buildProvenance,
        IPcrGovernance pcrGovernance,
        IFactorGovernance factorGovernance,
        IInventoryWorkflow inventoryWorkflow,
        IInventoryReadiness readiness,
        IActivityData activities)
    {
        _activities = activities;
        _inventoryWorkflow = inventoryWorkflow;
        _readiness = readiness;
        _pcrGovernance = pcrGovernance;
        _factorGovernance = factorGovernance;
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _onboardingService = onboardingService;
        _invitationService = invitationService;
        _emailSender = emailSender;
        _userManager = userManager;
        _signInManager = signInManager;
        _calculation = calculation;
        _snapshotReader = snapshotReader;
        _authorizationService = authorizationService;
        _evidenceStorageService = evidenceStorageService;
        _moenvFactorSynchronizationService = moenvFactorSynchronizationService;
        _mailPasswordProtector = dataProtectionProvider.CreateProtector("CarbonFootprint.OrganizationMailSettings.v1");
        _buildProvenance = buildProvenance;
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
        if (!await IsAllowedAsync(OrganizationPermission.ManageOrganization) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var normalizedHost = host?.Trim() ?? string.Empty;
        var normalizedUsername = username?.Trim() ?? string.Empty;
        var normalizedFromAddress = fromAddress?.Trim() ?? string.Empty;
        var normalizedFromName = fromName?.Trim() ?? string.Empty;
        var normalizedPassword = password ?? string.Empty;
        bool validFromAddress;
        try
        {
            validFromAddress = new System.Net.Mail.MailAddress(normalizedFromAddress).Address.Equals(normalizedFromAddress, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            validFromAddress = false;
        }
        if (string.IsNullOrWhiteSpace(normalizedHost)
            || normalizedHost.Length > 300
            || port is < 1 or > 65535
            || !validFromAddress
            || normalizedFromAddress.Length > 320
            || string.IsNullOrWhiteSpace(normalizedFromName)
            || normalizedFromName.Length > 200
            || normalizedUsername.Length > 320
            || normalizedPassword.Length > 1000)
        {
            ModelState.AddModelError("mail", "請填寫有效的 SMTP 主機、連接埠、寄件地址與寄件人名稱。");
            Section = "settings";
            Stage = "mail";
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _emailSender.ValidateSettingsAsync(normalizedHost, port, enableSsl, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            ModelState.AddModelError("mail", exception.Message);
            Section = "settings";
            Stage = "mail";
            await LoadAsync(cancellationToken);
            return Page();
        }

        var organizationId = RequireOrganization();
        var settings = await _dbContext.OrganizationMailSettings.SingleOrDefaultAsync(cancellationToken);
        var userId = Guid.TryParse(_userManager.GetUserId(User), out var parsedUserId) ? parsedUserId : (Guid?)null;
        if (settings is null)
        {
            settings = new OrganizationMailSettingsRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Host = normalizedHost,
                Port = port,
                EnableSsl = enableSsl,
                Username = normalizedUsername,
                EncryptedPassword = string.IsNullOrWhiteSpace(normalizedPassword) ? string.Empty : _mailPasswordProtector.Protect(normalizedPassword),
                FromAddress = normalizedFromAddress,
                FromName = normalizedFromName,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = userId
            };
            _dbContext.OrganizationMailSettings.Add(settings);
        }
        else
        {
            if (rowVersion != settings.RowVersion)
            {
                return await RejectStaleAsync("SaveMailSettings", "OrganizationMailSettings", settings.Id, cancellationToken);
            }
            settings.Host = normalizedHost;
            settings.Port = port;
            settings.EnableSsl = enableSsl;
            settings.Username = normalizedUsername;
            if (!string.IsNullOrWhiteSpace(normalizedPassword))
            {
                settings.EncryptedPassword = _mailPasswordProtector.Protect(normalizedPassword);
            }
            settings.FromAddress = normalizedFromAddress;
            settings.FromName = normalizedFromName;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            settings.UpdatedBy = userId;
        }

        AddAudit("organization.mail_settings.updated", "OrganizationMailSettings", settings.Id,
            metadata: new Dictionary<string, object?> { ["credentialsChanged"] = !string.IsNullOrWhiteSpace(normalizedPassword) });
        if (await SaveOrRejectStaleAsync("SaveMailSettings", "OrganizationMailSettings", settings.Id, cancellationToken) is { } conflict)
        {
            return conflict;
        }
        StatusMessage = "SMTP 設定已儲存。密碼以資料保護機制加密保存。";
        return RedirectToPage(new { section = "settings", stage = "mail", projectVersionId = ProjectVersionId });
    }

    public async Task<IActionResult> OnPostTestMailAsync(string recipient, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageOrganization) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var normalizedRecipient = recipient?.Trim() ?? string.Empty;
        var validRecipient = false;
        try
        {
            validRecipient = new System.Net.Mail.MailAddress(normalizedRecipient).Address.Equals(
                normalizedRecipient,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            validRecipient = false;
        }
        if (!validRecipient || normalizedRecipient.Length > 320)
        {
            ModelState.AddModelError("recipient", "請輸入測試收件地址。");
            Section = "settings";
            Stage = "mail";
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _emailSender.SendTestMessageAsync(normalizedRecipient, cancellationToken);
            var organizationId = RequireOrganization();
            var mailSettingsId = await _dbContext.OrganizationMailSettings
                .AsNoTracking()
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(cancellationToken);
            AddAudit(
                "organization.mail_settings.tested",
                mailSettingsId.HasValue ? "OrganizationMailSettings" : "Organization",
                mailSettingsId ?? organizationId);
            await _dbContext.SaveChangesAsync(cancellationToken);
            StatusMessage = "測試信已送出，請確認收件匣或 SMTP 服務紀錄。";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Net.Mail.SmtpException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            ModelState.AddModelError("mail", $"SMTP 測試失敗：{exception.Message}");
            Section = "settings";
            Stage = "mail";
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage(new { section = "settings", stage = "mail", projectVersionId = ProjectVersionId });
    }

    public async Task<IActionResult> OnPostCreateFacilityAsync(
        string facilityCode,
        string facilityName,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageOrganization))
        {
            return Forbid();
        }
        if (string.IsNullOrWhiteSpace(facilityCode) || string.IsNullOrWhiteSpace(facilityName))
        {
            ModelState.AddModelError("facility", "廠場代碼與名稱皆為必填。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var organizationId = RequireOrganization();
        var normalizedCode = facilityCode.Trim().ToUpperInvariant();
        if (await _dbContext.Facilities.AnyAsync(item => item.Code == normalizedCode, cancellationToken))
        {
            ModelState.AddModelError("facilityCode", "廠場代碼不可重複。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var facilityId = Guid.NewGuid();
        _dbContext.Facilities.Add(new FacilityRecord
        {
            Id = facilityId,
            OrganizationId = organizationId,
            Code = normalizedCode,
            Name = facilityName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddAudit("facility.created", "Facility", facilityId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "廠場已建立。";
        return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
    }

    public async Task<IActionResult> OnPostInviteMemberAsync(
        string invitationEmail,
        OrganizationRole invitationRole,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageOrganization) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        if (!Guid.TryParse(_userManager.GetUserId(User), out var invitedBy))
        {
            return Forbid();
        }

        try
        {
            var token = await _invitationService.CreateAsync(
                RequireOrganization(),
                invitedBy,
                invitationEmail,
                invitationRole,
                cancellationToken, HttpContext.TraceIdentifier, AuditWriter.RequestSessionReference(HttpContext));
            var link = Url.Page("/AcceptInvitation", pageHandler: null, values: new { token }, protocol: Request.Scheme)
                ?? throw new InvalidOperationException("無法建立邀請連結。");
            await _emailSender.SendOrganizationInvitationAsync(invitationEmail.Trim(), link, cancellationToken);
            StatusMessage = "組織邀請已寄出。";
            return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError("invitation", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRevokeInvitationAsync(Guid invitationId, uint rowVersion,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageOrganization) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var invitation = await _dbContext.OrganizationInvitations.SingleOrDefaultAsync(item => item.Id == invitationId, cancellationToken);
        if (invitation is null)
        {
            return NotFound();
        }
        if (rowVersion != invitation.RowVersion)
        {
            return await RejectStaleAsync("RevokeInvitation", "OrganizationInvitation", invitation.Id, cancellationToken);
        }
        if (!invitation.AcceptedAt.HasValue)
        {
            invitation.RevokedAt = DateTimeOffset.UtcNow;
            AddAudit("organization.invitation.revoked", "OrganizationInvitation", invitation.Id);
            if (await SaveOrRejectStaleAsync("RevokeInvitation", "OrganizationInvitation", invitation.Id, cancellationToken) is { } conflict)
            {
                return conflict;
            }
        }
        return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
    }

    public async Task<IActionResult> OnPostRevokeMemberAsync(Guid membershipId, uint rowVersion,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageOrganization) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var membership = await _dbContext.OrganizationMemberships.SingleOrDefaultAsync(item => item.Id == membershipId, cancellationToken);
        if (membership is null)
        {
            return NotFound();
        }
        if (rowVersion != membership.RowVersion)
        {
            return await RejectStaleAsync("RevokeMember", "OrganizationMembership", membership.Id, cancellationToken);
        }
        if (membership.Role == OrganizationRole.Owner.ToString())
        {
            ModelState.AddModelError("membership", "不可撤銷組織擁有者。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var revokedUser = await _userManager.FindByIdAsync(membership.UserId.ToString());
        if (revokedUser is null)
        {
            return NotFound();
        }
        var stampResult = await _userManager.UpdateSecurityStampAsync(revokedUser);
        if (!stampResult.Succeeded)
        {
            ModelState.AddModelError("membership", "無法更新成員登入安全狀態，請稍後再試。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        membership.RevokedAt = DateTimeOffset.UtcNow;
        AddAudit("organization.membership.revoked", "OrganizationMembership", membership.Id);
        if (await SaveOrRejectStaleAsync("RevokeMember", "OrganizationMembership", membership.Id, cancellationToken) is { } conflict)
        {
            return conflict;
        }
        return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
    }

    public async Task<IActionResult> OnPostCreateProductAsync(
        string productName,
        string categoryCode,
        Guid facilityId,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        if (string.IsNullOrWhiteSpace(productName) || string.IsNullOrWhiteSpace(categoryCode))
        {
            ModelState.AddModelError("productName", "產品名稱不可為空。");
            await LoadAsync(cancellationToken);
            return Page();
        }
        if (facilityId == Guid.Empty)
        {
            ModelState.AddModelError("facilityId", "請先建立並選擇所屬廠場。");
            await LoadAsync(cancellationToken);
            return Page();
        }
        if (!await _dbContext.Facilities.AnyAsync(
                item => item.Id == facilityId && item.OrganizationId == organizationId,
                cancellationToken))
        {
            ModelState.AddModelError("facilityId", "所選廠場不存在或不屬於目前組織。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var productId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        _dbContext.Products.Add(new ProductRecord
        {
            Id = productId,
            OrganizationId = organizationId,
            Name = productName.Trim(),
            CategoryCode = categoryCode.Trim().ToUpperInvariant(),
            FacilityId = facilityId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.ProductVersions.Add(new ProductVersionRecord
        {
            Id = versionId,
            OrganizationId = organizationId,
            ProductId = productId,
            VersionNumber = 1,
            NameZhTw = productName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddAudit("product.version.created", "ProductVersion", versionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "產品與第 1 版已建立。";
        return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
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
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        if (periodStart > periodEnd
            || string.IsNullOrWhiteSpace(functionalUnit)
            || string.IsNullOrWhiteSpace(declaredUnit)
            || string.IsNullOrWhiteSpace(systemBoundary)
            || string.IsNullOrWhiteSpace(allocationMethod)
            || string.IsNullOrWhiteSpace(allocationReason))
        {
            ModelState.AddModelError("inventory", "請提供有效期間、功能單位與 PCR 版本識別。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (productVersionId == Guid.Empty || pcrVersionId == Guid.Empty)
        {
            ModelState.AddModelError("inventory", "請先建立並選擇產品版本與已發布 PCR 版本。");
            await LoadAsync(cancellationToken);
            return Page();
        }
        var productContext = await (
                from version in _dbContext.ProductVersions.AsNoTracking()
                join product in _dbContext.Products.AsNoTracking() on version.ProductId equals product.Id
                where version.Id == productVersionId && version.OrganizationId == organizationId
                select new { version.Id, product.CategoryCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (productContext is null)
        {
            ModelState.AddModelError("productVersionId", "所選產品版本不存在或不屬於目前組織。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var pcr = await _dbContext.PcrVersions.SingleOrDefaultAsync(
            item => item.Id == pcrVersionId && item.OrganizationId == organizationId,
            cancellationToken);
        if (pcr is null)
        {
            ModelState.AddModelError("pcrVersionId", "所選 PCR 版本不存在或不屬於目前組織。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var pcrStageRules = await _dbContext.PcrStageRules
            .AsNoTracking()
            .Where(item => item.PcrVersionId == pcr.Id)
            .OrderBy(item => item.LifecycleStage)
            .ToArrayAsync(cancellationToken);
        var pcrRuleSet = PcrRuleSetMapper.ToRuleSet(pcr, pcrStageRules);
        var defaultStageApplicability = Enum.GetValues<LifecycleStage>()
            .ToDictionary(
                stage => stage,
                stage => pcrRuleSet.StageRules
                    .SingleOrDefault(item => item.Stage == stage)?.Requirement != PcrStageRequirement.Prohibited);
        var pcrViolations = PcrRuleEngine.Validate(
            pcrRuleSet,
            new PcrProjectContext(
                Guid.Empty,
                productContext.CategoryCode,
                periodEnd,
                functionalUnit.Trim(),
                declaredUnit.Trim(),
                systemBoundary.Trim(),
                allocationMethod.Trim(),
                defaultStageApplicability,
                [],
                exclusions?.Trim() ?? string.Empty),
            requireCompleteInventory: false);
        if (pcrViolations.Count > 0)
        {
            foreach (var violation in pcrViolations)
            {
                ModelState.AddModelError("inventory", $"{violation.Code}：{violation.Message}");
            }
            await LoadAsync(cancellationToken);
            return Page();
        }

        var nextVersion = (await _dbContext.InventoryProjectVersions
            .Where(item => item.ProductVersionId == productVersionId)
            .MaxAsync(item => (int?)item.VersionNumber, cancellationToken) ?? 0) + 1;
        var projectId = Guid.NewGuid();
        _dbContext.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId,
            OrganizationId = organizationId,
            ProductVersionId = productVersionId,
            VersionNumber = nextVersion,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            FunctionalUnit = functionalUnit.Trim(),
            DeclaredUnit = declaredUnit.Trim(),
            SystemBoundary = systemBoundary.Trim(),
            AllocationMethod = allocationMethod.Trim(),
            AllocationReason = allocationReason.Trim(),
            Exclusions = exclusions?.Trim() ?? string.Empty,
            Assumptions = assumptions?.Trim() ?? string.Empty,
            EstimationReason = estimationReason?.Trim() ?? string.Empty,
            PcrVersionId = pcr.Id,
            PcrVersion = $"{pcr.RegistrationNumber}-v{pcr.VersionNumber}",
            WorkflowStatus = InventoryWorkflowStatus.Draft.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.LifecycleStageDeclarations.AddRange(Enum.GetValues<LifecycleStage>().Select(stage =>
            new LifecycleStageDeclarationRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                InventoryProjectVersionId = projectId,
                LifecycleStage = (int)stage,
                IsApplicable = defaultStageApplicability[stage],
                Reason = defaultStageApplicability[stage] ? string.Empty : "PCR 規則禁止納入此階段。"
            }));
        AddAudit("inventory.version.created", "InventoryProjectVersion", projectId);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_inventory_project_versions_product_version_id_version_number"
        })
        {
            _dbContext.ChangeTracker.Clear();
            ModelState.AddModelError("inventory", "另一位使用者剛建立了此產品的盤查版本，請重新儲存以建立下一版。");
            await LoadAsync(cancellationToken);
            return Page();
        }
        StatusMessage = $"盤查專案第 {nextVersion} 版已建立。";
        return RedirectToPage(new { section = Section, projectVersionId = projectId });
    }

    public async Task<IActionResult> OnPostSetStageApplicabilityAsync(
        Guid stageDeclarationId,
        bool isApplicable,
        string reason,
        uint rowVersion,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }
        var declaration = await _dbContext.LifecycleStageDeclarations.SingleOrDefaultAsync(
            item => item.Id == stageDeclarationId,
            cancellationToken);
        if (declaration is null)
        {
            return NotFound();
        }
        if (rowVersion != declaration.RowVersion)
        {
            return await RejectStaleAsync("SetStageApplicability", "LifecycleStageDeclaration", declaration.Id, cancellationToken);
        }
        var project = await _dbContext.InventoryProjectVersions.SingleAsync(
            item => item.Id == declaration.InventoryProjectVersionId,
            cancellationToken);
        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus)))
        {
            return BadRequest();
        }
        var pcrStageRule = project.PcrVersionId.HasValue
            ? await _dbContext.PcrStageRules.AsNoTracking().SingleOrDefaultAsync(
                item => item.PcrVersionId == project.PcrVersionId.Value
                    && item.LifecycleStage == declaration.LifecycleStage,
                cancellationToken)
            : null;
        if (pcrStageRule is not null
            && Enum.TryParse<PcrStageRequirement>(pcrStageRule.Requirement, out var stageRequirement)
            && ((stageRequirement == PcrStageRequirement.Mandatory && !isApplicable)
                || (stageRequirement == PcrStageRequirement.Prohibited && isApplicable)))
        {
            ModelState.AddModelError("stage", "階段適用性不可違反已選 PCR 的必要或禁止規則。");
            await LoadAsync(cancellationToken);
            return Page();
        }
        if (!isApplicable && string.IsNullOrWhiteSpace(reason))
        {
            ModelState.AddModelError("stage", "不適用階段必須填寫原因。");
            await LoadAsync(cancellationToken);
            return Page();
        }
        if (!isApplicable && await _dbContext.ActivityData.AnyAsync(
                item => item.InventoryProjectVersionId == project.Id && item.LifecycleStage == declaration.LifecycleStage && item.RetiredAt == null,
                cancellationToken))
        {
            ModelState.AddModelError("stage", "已有活動數據的階段不可標記為不適用。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        declaration.IsApplicable = isApplicable;
        declaration.Reason = isApplicable ? string.Empty : reason.Trim();
        AddAudit("inventory.stage.applicability.changed", "LifecycleStageDeclaration", declaration.Id);
        if (await SaveOrRejectStaleAsync("SetStageApplicability", "LifecycleStageDeclaration", declaration.Id, cancellationToken) is { } conflict)
        {
            return conflict;
        }
        return RedirectToPage(new { section = Section, projectVersionId = project.Id });
    }

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
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(registrationNumber)
            || versionNumber < 1
            || string.IsNullOrWhiteSpace(title)
            || !IsHttpSourceUrl(sourceReference)
            || string.IsNullOrWhiteSpace(standardCode)
            || string.IsNullOrWhiteSpace(cccClassification)
            || string.IsNullOrWhiteSpace(pcrApplicability)
            || string.IsNullOrWhiteSpace(ruleRequirements)
            || string.IsNullOrWhiteSpace(productCategoryPatterns)
            || string.IsNullOrWhiteSpace(functionalUnitPattern)
            || string.IsNullOrWhiteSpace(declaredUnitCode)
            || string.IsNullOrWhiteSpace(systemBoundaryCode)
            || string.IsNullOrWhiteSpace(permittedAllocationMethods)
            || string.IsNullOrWhiteSpace(formulaRuleSetVersion)
            || string.IsNullOrWhiteSpace(reportingRequirements)
            || cutoffThresholdPercent is < 0 or > 100
            || roundingDecimalPlaces is < 0 or > 12
            || (isCustomRule && string.IsNullOrWhiteSpace(customRuleJustification))
            || originalDocument is null
            || originalDocument.Length <= 0
            || approvalDate is null
            || validFrom is null
            || validTo is null
            || validFrom > validTo)
        {
            ModelState.AddModelError("pcr", "PCR 識別、適用條件、計算規則、原始文件與有效期間必須完整且有效。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var organizationId = RequireOrganization();
        if (!await _dbContext.Units.AnyAsync(
                item => item.CatalogueVersion == CurrentUnitCatalogueVersion
                    && item.Code == declaredUnitCode.Trim(),
                cancellationToken))
        {
            ModelState.AddModelError("pcr", "PCR 標示單位必須使用目前受控單位目錄中的代碼。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!string.Equals(
                formulaRuleSetVersion.Trim(),
                ActivityEmissionFormula.PcrFormulaRuleSetV1,
                StringComparison.Ordinal))
        {
            ModelState.AddModelError("pcr", "PCR 公式規則版本目前僅支援 pcr-formulas-v1。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var pcrVersionId = Guid.NewGuid();
        if (!TryBuildPcrStageRules(
                organizationId,
                pcrVersionId,
                [
                    (LifecycleStage.RawMaterial, rawMaterialRequirement, rawMaterialKinds, rawMaterialRequiredFields),
                    (LifecycleStage.Manufacturing, manufacturingRequirement, manufacturingKinds, manufacturingRequiredFields),
                    (LifecycleStage.Distribution, distributionRequirement, distributionKinds, distributionRequiredFields),
                    (LifecycleStage.Use, useRequirement, useKinds, useRequiredFields),
                    (LifecycleStage.EndOfLife, endOfLifeRequirement, endOfLifeKinds, endOfLifeRequiredFields)
                ],
                out var stageRules,
                out var stageRuleError))
        {
            ModelState.AddModelError("pcr", stageRuleError);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var existingVersions = await _dbContext.PcrVersions
            .AsNoTracking()
            .Where(item => item.RegistrationNumber == registrationNumber.Trim())
            .OrderByDescending(item => item.VersionNumber)
            .ToArrayAsync(cancellationToken);
        if (existingVersions.Any(item => item.VersionNumber == versionNumber))
        {
            ModelState.AddModelError("pcr", "同一 PCR 登錄編號不可重複建立相同版本號。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        StoredEvidence storedDocument;
        try
        {
            await using var content = originalDocument.OpenReadStream();
            storedDocument = await _evidenceStorageService.StoreAsync(
                organizationId,
                content,
                originalDocument.FileName,
                originalDocument.ContentType,
                cancellationToken);
        }
        catch (EvidenceUploadException exception)
        {
            ModelState.AddModelError("pcr", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var previousVersion = existingVersions.FirstOrDefault(item => item.VersionNumber < versionNumber);
        _dbContext.PcrVersions.Add(new PcrVersionRecord
        {
            Id = pcrVersionId,
            OrganizationId = organizationId,
            RuleSetId = existingVersions.FirstOrDefault()?.RuleSetId ?? Guid.NewGuid(),
            RegistrationNumber = registrationNumber.Trim(),
            VersionNumber = versionNumber,
            Title = title.Trim(),
            ApprovalDate = approvalDate,
            ValidFrom = validFrom,
            ValidTo = validTo,
            PublicationStatus = PcrPublicationStatus.Draft.ToString(),
            SourceReference = sourceReference.Trim(),
            StandardCode = standardCode.Trim(),
            CccClassification = cccClassification.Trim(),
            Applicability = pcrApplicability.Trim(),
            RuleRequirements = ruleRequirements.Trim(),
            OriginalDocumentName = storedDocument.OriginalFileName,
            OriginalDocumentObjectKey = storedDocument.ObjectKey,
            OriginalDocumentContentType = storedDocument.ContentType,
            OriginalDocumentSizeBytes = storedDocument.SizeBytes,
            OriginalDocumentSha256 = storedDocument.Sha256,
            OriginalDocumentScanStatus = storedDocument.ScanStatus.ToString(),
            ProductCategoryPatterns = productCategoryPatterns.Trim(),
            FunctionalUnitPattern = functionalUnitPattern.Trim(),
            DeclaredUnitCode = declaredUnitCode.Trim(),
            SystemBoundaryCode = systemBoundaryCode.Trim(),
            PermittedAllocationMethodsCsv = NormalizeCsv(permittedAllocationMethods),
            CutoffThresholdPercent = cutoffThresholdPercent,
            FormulaRuleSetVersion = formulaRuleSetVersion.Trim(),
            RoundingDecimalPlaces = roundingDecimalPlaces,
            ReportingRequirements = reportingRequirements.Trim(),
            IsCustomRule = isCustomRule,
            CustomRuleJustification = customRuleJustification?.Trim() ?? string.Empty,
            CustomApprovalStatus = isCustomRule
                ? PcrCustomApprovalStatus.Pending.ToString()
                : PcrCustomApprovalStatus.NotRequired.ToString(),
            SupersedesVersionId = previousVersion?.Id,
            ReviewStatus = PcrReviewStatus.Pending.ToString(),
            CreatedBy = Guid.TryParse(_userManager.GetUserId(User), out var creatorId) ? creatorId : null,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.PcrStageRules.AddRange(stageRules);
        AddAudit("pcr.version.created", "PcrVersion", pcrVersionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = $"PCR 草稿已建立；原始文件 SHA-256：{storedDocument.Sha256}。";
        return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
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
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        var hasSourceType = TryResolveControlledValue(factorSourceType, factorSourceTypeOther, out var sourceType);
        var hasGeography = TryResolveControlledValue(factorGeography, factorGeographyOther, out var geography);
        if (string.IsNullOrWhiteSpace(factorName)
            || factorValue is null or < 0m
            || !hasSourceType
            || !hasGeography
            || factorValidFrom > factorValidTo
            || string.IsNullOrWhiteSpace(sourceDatasetVersion)
            || string.IsNullOrWhiteSpace(licenseCode)
            || string.IsNullOrWhiteSpace(factorSourceName)
            || string.IsNullOrWhiteSpace(factorSourceReference)
            || string.IsNullOrWhiteSpace(datasetName)
            || string.IsNullOrWhiteSpace(factorOriginalDocumentName)
            || string.IsNullOrWhiteSpace(factorApplicability))
        {
            ModelState.AddModelError("factor", "來源類型、地域、有效期間與原始文件皆為必填。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!await _dbContext.Units.AnyAsync(
                item => item.CatalogueVersion == CurrentUnitCatalogueVersion && item.Code == denominatorUnitCode,
                cancellationToken))
        {
            ModelState.AddModelError("factor", "係數分母必須使用受控單位。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var factorVersionId = Guid.NewGuid();
        _dbContext.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
        {
            Id = factorVersionId,
            OrganizationId = organizationId,
            FactorId = Guid.NewGuid(),
            VersionNumber = 1,
            Name = factorName.Trim(),
            Value = factorValue.Value,
            NumeratorUnitCode = "kgCO2e",
            DenominatorUnitCode = denominatorUnitCode,
            Geography = geography,
            ValidFrom = factorValidFrom,
            ValidTo = factorValidTo,
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = sourceDatasetVersion.Trim(),
            LicenseCode = licenseCode.Trim(),
            SourceType = sourceType,
            SourceName = factorSourceName.Trim(),
            SourceReference = factorSourceReference.Trim(),
            DatasetName = datasetName.Trim(),
            OriginalDocumentName = factorOriginalDocumentName.Trim(),
            OriginalDocumentSha256 = string.Empty,
            Applicability = factorApplicability.Trim(),
            ReviewStatus = FactorReviewStatus.Pending.ToString()
        });
        AddAudit("factor.version.created", "EmissionFactorVersion", factorVersionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "係數草稿已建立；發布後才可用於新計算。";
        return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
    }

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
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var current = await _dbContext.EmissionFactorVersions.SingleOrDefaultAsync(
            item => item.Id == factorVersionId,
            cancellationToken);
        if (current is null)
        {
            return NotFound();
        }
        if (rowVersion != current.RowVersion)
        {
            return await RejectStaleAsync("SupersedeFactor", "EmissionFactorVersion", current.Id, cancellationToken);
        }
        if (current.PublicationStatus != FactorPublicationStatus.Published.ToString()
            || newFactorValue is null or < 0m
            || string.IsNullOrWhiteSpace(newSourceDatasetVersion)
            || string.IsNullOrWhiteSpace(newFactorSourceReference)
            || string.IsNullOrWhiteSpace(newOriginalDocumentName)
            || string.IsNullOrWhiteSpace(newApplicability))
        {
            ModelState.AddModelError("factor", "更新已發布係數時，數值、來源版本、來源網址、原始文件與適用性皆須有效。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var newVersionId = Guid.NewGuid();
        _dbContext.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
        {
            Id = newVersionId,
            OrganizationId = current.OrganizationId,
            FactorId = current.FactorId,
            VersionNumber = current.VersionNumber + 1,
            Name = current.Name,
            Value = newFactorValue.Value,
            NumeratorUnitCode = current.NumeratorUnitCode,
            DenominatorUnitCode = current.DenominatorUnitCode,
            Geography = current.Geography,
            ValidFrom = current.ValidFrom,
            ValidTo = current.ValidTo,
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = newSourceDatasetVersion.Trim(),
            LicenseCode = current.LicenseCode,
            SourceType = current.SourceType,
            SourceName = current.SourceName,
            SourceReference = newFactorSourceReference.Trim(),
            DatasetName = current.DatasetName,
            OriginalDocumentName = newOriginalDocumentName.Trim(),
            OriginalDocumentSha256 = string.Empty,
            Applicability = newApplicability.Trim(),
            ReviewStatus = FactorReviewStatus.Pending.ToString(),
            SupersedesVersionId = current.Id
        });
        AddAudit("factor.version.superseded", "EmissionFactorVersion", newVersionId);
        if (await SaveOrRejectStaleAsync("SupersedeFactor", "EmissionFactorVersion", current.Id, cancellationToken) is { } conflict)
        {
            return conflict;
        }
        StatusMessage = "更新草稿已建立；現行係數會保留至新版本審查發布，歷史計算不受影響。";
        return RedirectToPage(new { section = Section, projectVersionId = ProjectVersionId });
    }

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
        var runQuery = _dbContext.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == project.Id);
        if (runId.HasValue)
        {
            runQuery = runQuery.Where(item => item.Id == runId.Value);
        }
        var run = await runQuery.OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (runId.HasValue && run is null)
        {
            return NotFound();
        }
        if (run is not null && !RunExportValidation.TryReadRules(run, out _))
        {
            return RunExportValidation.ConflictResult();
        }
        var manifest = run is not null
            ? (Json: run.CanonicalInputManifest, Sha256: run.InputSha256)
            : CanonicalManifest.Create(await _snapshotReader.ReadAsync(project.Id, cancellationToken), _buildProvenance);
        var references = CanonicalManifest.ReadEvidenceReferences(manifest.Json, manifest.Sha256);
        using var document = JsonDocument.Parse(manifest.Json);
        var root = document.RootElement;
        var activities = root.GetProperty("activities").EnumerateArray().ToArray();
        var factorIds = activities.Select(activity => activity.GetProperty("factorVersionId").GetGuid()).Distinct().ToArray();
        var factorMetadata = await _dbContext.EmissionFactorVersions.AsNoTracking()
            .Where(factor => factorIds.Contains(factor.Id)).ToDictionaryAsync(factor => factor.Id, cancellationToken);
        var lines = run is null ? [] : await _dbContext.CalculationLineItems.AsNoTracking()
            .Where(item => item.CalculationRunId == run.Id)
            .OrderBy(item => item.LifecycleStage).ThenBy(item => item.ActivityId).ToArrayAsync(cancellationToken);
        var evidenceActivityIds = references.Keys.ToArray();
        var evidenceFiles = await _dbContext.EvidenceFiles.AsNoTracking()
            .Where(item => evidenceActivityIds.Contains(item.ActivityDataId))
            .OrderBy(item => item.ActivityDataId).ToArrayAsync(cancellationToken);
        var summaryRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "欄位", "內容" },
            new object?[] { "功能單位", root.GetProperty("functionalUnit").GetString() },
            new object?[] { "宣告單位", root.GetProperty("declaredUnit").GetString() },
            new object?[] { "盤查期間", $"{root.GetProperty("periodStart").GetString()}～{root.GetProperty("periodEnd").GetString()}" },
            new object?[] { "系統邊界", root.GetProperty("systemBoundary").GetString() },
            new object?[] { "分配方法", root.GetProperty("allocationMethod").GetString() },
            new object?[] { "PCR 版本", root.GetProperty("pcrVersion").GetString() },
            new object?[] { "資料範圍", run is null ? "尚未建立計算版本；匯出目前活動資料" : $"計算版本 {run.Id:N} 的不可變輸入與結果" },
            new object?[] { "輸入 SHA-256", manifest.Sha256 },
            new object?[] { "係數來源欄位", "來源與名稱參照所引用的係數版本目錄；活動量、係數值、單位與分配比例取自本次匯出快照。" },
            new object?[] { "計算結果", run?.ProductTotal },
            new object?[] { "結果單位", run is null ? string.Empty : "kgCO2e" }
        };
        var activityRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "階段", "活動名稱", "活動類型", "原始活動量", "原始單位", "標準活動量", "標準單位",
                "係數名稱", "係數版本", "係數值", "係數單位", "分配比例", "資料品質", "來源", "活動 ID" }
        };
        activityRows.AddRange(activities.Select(activity =>
        {
            factorMetadata.TryGetValue(activity.GetProperty("factorVersionId").GetGuid(), out var metadata);
            return (IReadOnlyList<object?>)new object?[]
            {
                LifecycleStageDisplayName(Enum.Parse<LifecycleStage>(activity.GetProperty("stage").GetString()!)),
                activity.GetProperty("name").GetString(),
                ActivityKindDisplayName(Enum.Parse<ActivityDataKind>(activity.GetProperty("kind").GetString()!)),
                activity.GetProperty("rawValue").GetDecimal(), activity.GetProperty("rawUnitCode").GetString(),
                activity.GetProperty("canonicalValue").GetDecimal(), activity.GetProperty("canonicalUnitCode").GetString(),
                metadata?.Name ?? "係數版本目錄資料遺失", metadata?.VersionNumber,
                activity.GetProperty("factorValue").GetDecimal(),
                $"{activity.GetProperty("factorNumeratorUnit").GetString()}/{activity.GetProperty("factorDenominatorUnit").GetString()}",
                activity.GetProperty("allocationFactor").GetDecimal(), activity.GetProperty("dataQuality").GetString(),
                activity.GetProperty("sourceReference").GetString(), activity.GetProperty("id").GetGuid()
            };
        }));
        var factorRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "係數名稱", "版本", "係數值", "單位", "地域", "來源機構", "來源資料集",
                "公告版本", "來源網址", "原始資料 SHA-256", "係數版本 ID" }
        };
        factorRows.AddRange(activities.DistinctBy(activity => activity.GetProperty("factorVersionId").GetGuid())
            .Select(activity =>
            {
                var id = activity.GetProperty("factorVersionId").GetGuid();
                factorMetadata.TryGetValue(id, out var metadata);
                return (IReadOnlyList<object?>)new object?[]
                {
                    metadata?.Name ?? "係數版本目錄資料遺失", metadata?.VersionNumber,
                    activity.GetProperty("factorValue").GetDecimal(),
                    $"{activity.GetProperty("factorNumeratorUnit").GetString()}/{activity.GetProperty("factorDenominatorUnit").GetString()}",
                    metadata is null ? null : GeographyDisplayName(metadata.Geography), metadata?.SourceName,
                    metadata?.DatasetName, metadata?.SourceDatasetVersion, metadata?.SourceReference,
                    metadata?.OriginalDocumentSha256, id
                };
            }));
        var resultRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "階段", "活動 ID", "標準活動量", "單位", "係數值", "係數單位", "排放量", "結果單位" }
        };
        resultRows.AddRange(lines.Select(line => (IReadOnlyList<object?>)new object?[]
        {
            LifecycleStageDisplayName((LifecycleStage)line.LifecycleStage), line.ActivityId,
            line.CanonicalActivityValue, line.ActivityUnitCode, line.FactorValue, line.FactorUnit,
            line.Emissions, line.EmissionsUnitCode
        }));
        var evidenceRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "活動 ID", "檔案名稱", "SHA-256", "掃描狀態" }
        };
        evidenceRows.AddRange(evidenceFiles.Where(item => string.Equals(references[item.ActivityDataId], item.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(item => (IReadOnlyList<object?>)new object?[] { item.ActivityDataId, item.OriginalFileName, item.Sha256, item.ScanStatus }));
        return File(ExcelWorkbook.Create(
        [
            new ExcelSheet("盤查摘要", summaryRows), new ExcelSheet("五階段活動", activityRows),
            new ExcelSheet("使用係數", factorRows), new ExcelSheet("計算結果", resultRows),
            new ExcelSheet("佐證索引", evidenceRows)
        ]), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"碳足跡盤查-{(run?.Id ?? project.Id):N}.xlsx");
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
                : CurrentUnitCatalogueVersion;
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

    private static string LifecycleStageDisplayName(LifecycleStage stage) => stage switch
    {
        LifecycleStage.RawMaterial => "原料取得階段",
        LifecycleStage.Manufacturing => "製造階段",
        LifecycleStage.Distribution => "配送與銷售階段",
        LifecycleStage.Use => "使用階段",
        LifecycleStage.EndOfLife => "廢棄處理階段",
        _ => "未定義階段"
    };

    private static string ActivityKindDisplayName(ActivityDataKind kind) => kind switch
    {
        ActivityDataKind.Material => "原物料投入",
        ActivityDataKind.MaterialTransport => "原物料運輸",
        ActivityDataKind.Energy => "能資源使用",
        ActivityDataKind.ManufacturingWaste => "製造廢棄物",
        ActivityDataKind.OutsourcedTreatmentTransport => "委外處理運輸",
        ActivityDataKind.DistributionTransport => "配送銷售運輸",
        ActivityDataKind.UseEnergy => "使用能源",
        ActivityDataKind.UseConsumable => "使用耗材",
        ActivityDataKind.EndOfLifeTreatment => "廢棄處理",
        ActivityDataKind.EndOfLifeTransport => "廢棄處理運輸",
        _ => "其他活動"
    };

    private static string GeographyDisplayName(string geography) => geography switch
    {
        "TW" => "台灣",
        "Global" => "全球",
        "East Asia" => "東亞",
        "EU" => "歐盟",
        "US" => "美國",
        "CN" => "中國",
        "JP" => "日本",
        _ => geography
    };

    private static string GetActivityUnitCatalogueVersion(IEnumerable<ActivityDataRecord> activities) =>
        GetActivityUnitCatalogueVersion(activities.Select(item => item.ConversionRuleVersion));

    private static string GetActivityUnitCatalogueVersion(IEnumerable<string> versions) =>
        UnitCatalogue.ResolveVersion(versions);

    private static bool TryResolveControlledValue(string? selected, string? other, out string value)
    {
        value = selected?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        if (!string.Equals(value, "__other__", StringComparison.Ordinal))
        {
            return true;
        }
        value = other?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

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

    private static bool TryBuildPcrStageRules(
        Guid organizationId,
        Guid pcrVersionId,
        IReadOnlyList<(LifecycleStage Stage, string Requirement, string Kinds, string RequiredFields)> inputs,
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
                error = $"{LifecycleStageDisplayName(input.Stage)}的必要性設定無效。";
                return false;
            }

            var kindValues = PcrRuleSetMapper.SplitCsv(input.Kinds);
            if (kindValues.Count == 0
                || kindValues.Any(value => !Enum.TryParse<ActivityDataKind>(value, true, out _)))
            {
                rules = [];
                error = $"{LifecycleStageDisplayName(input.Stage)}的活動類型清單無效。";
                return false;
            }

            var requiredFields = PcrRuleSetMapper.SplitCsv(input.RequiredFields);
            if (requiredFields.Any(field => !allowedFields.Contains(field)))
            {
                rules = [];
                error = $"{LifecycleStageDisplayName(input.Stage)}含有未受控的必填欄位。";
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

    private static string NormalizeCsv(string? value) => string.Join(",", PcrRuleSetMapper.SplitCsv(value));

    private static bool IsHttpSourceUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private OperationContext Operation() => new(
        User,
        Guid.TryParse(_userManager.GetUserId(User), out var actorId) ? actorId : null,
        HttpContext.TraceIdentifier,
        AuditWriter.RequestSessionReference(HttpContext));

    private async Task<IActionResult> RejectStaleAsync(string operation, string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        await OperationAudit.RecordStaleRejectionAsync(
            _dbContext, Operation(), RequireOrganization(), operation, resourceType, resourceId, cancellationToken);
        return await ShowConflictAsync(cancellationToken);
    }

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

    private async Task<IActionResult?> SaveOrRejectStaleAsync(string operation, string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RejectStaleAsync(operation, resourceType, resourceId, cancellationToken);
        }
    }

    private void AddAudit(string action, string resourceType, Guid resourceId,
        string? reason = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var organizationId = RequireOrganization();
        var actorId = _userManager.GetUserId(User) is { } value && Guid.TryParse(value, out var id) ? id : (Guid?)null;
        var correlation = HttpContext.TraceIdentifier;
        var session = AuditWriter.RequestSessionReference(HttpContext);
        var writer = new AuditWriter(_dbContext);
        if (action == "organization.mail_settings.tested")
        {
            writer.Append(action, resourceType, resourceId, organizationId, actorId, null, null,
                "SMTP test; no resource state mutation", correlation, session, "attempt");
            return;
        }
        OperationAudit.AppendTracked(_dbContext, Operation(), organizationId, action, resourceType, resourceId, reason, metadata);
    }
}
