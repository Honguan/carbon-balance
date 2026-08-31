using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Application.Exports;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Domain.Modules.Standards;
using CarbonFootprint.Domain.Modules.Units;
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

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class WorkspaceModel : PageModel
{
    private const string CurrentUnitCatalogueVersion = "units-p0-v2";
    private const string PendingStageFormulaRuleSetVersion = "legacy-stage-formulas-pending-review-v1";

    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;
    private readonly OrganizationOnboardingService _onboardingService;
    private readonly OrganizationInvitationService _invitationService;
    private readonly SmtpEmailSender _emailSender;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly CalculateInventoryHandler _calculateHandler;
    private readonly IAuthorizationService _authorizationService;
    private readonly EvidenceStorageService _evidenceStorageService;
    private readonly MoenvFactorSynchronizationService _moenvFactorSynchronizationService;
    private readonly IDataProtector _mailPasswordProtector;

    public WorkspaceModel(
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        OrganizationOnboardingService onboardingService,
        OrganizationInvitationService invitationService,
        SmtpEmailSender emailSender,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CalculateInventoryHandler calculateHandler,
        IAuthorizationService authorizationService,
        EvidenceStorageService evidenceStorageService,
        MoenvFactorSynchronizationService moenvFactorSynchronizationService,
        IDataProtectionProvider dataProtectionProvider)
    {
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _onboardingService = onboardingService;
        _invitationService = invitationService;
        _emailSender = emailSender;
        _userManager = userManager;
        _signInManager = signInManager;
        _calculateHandler = calculateHandler;
        _authorizationService = authorizationService;
        _evidenceStorageService = evidenceStorageService;
        _moenvFactorSynchronizationService = moenvFactorSynchronizationService;
        _mailPasswordProtector = dataProtectionProvider.CreateProtector("CarbonFootprint.OrganizationMailSettings.v1");
    }

    public Guid? OrganizationId => _organizationScope.OrganizationId;

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

    public IReadOnlyList<OrganizationInvitationRecord> Invitations { get; private set; } = [];

    public IReadOnlyList<InventoryProjectVersionRecord> InventoryProjects { get; private set; } = [];

    public IReadOnlyList<LifecycleStageDeclarationRecord> StageDeclarations { get; private set; } = [];

    public IReadOnlyList<EmissionFactorVersionRecord> Factors { get; private set; } = [];

    public IReadOnlyList<EmissionFactorVersionRecord> SelectableFactors { get; private set; } = [];

    public IReadOnlyList<PcrVersionRecord> PcrVersions { get; private set; } = [];

    public IReadOnlyList<PcrStageRuleRecord> PcrStageRules { get; private set; } = [];

    public IReadOnlyList<PcrVersionRecord> SelectablePcrVersions { get; private set; } = [];

    public IReadOnlyDictionary<Guid, int> PcrAffectedProjectCounts { get; private set; } =
        new Dictionary<Guid, int>();

    public IReadOnlyDictionary<Guid, IReadOnlyList<InventoryProjectVersionRecord>> PcrAffectedProjects { get; private set; } =
        new Dictionary<Guid, IReadOnlyList<InventoryProjectVersionRecord>>();

    public IReadOnlyList<ActivityDataRecord> Activities { get; private set; } = [];

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

        await LoadAsync(cancellationToken);
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
            await _onboardingService.CreateAsync(user, organizationName, cancellationToken);
            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "組織已建立。";
            return RedirectToPage(new { section = Section });
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

        AddAudit("organization.mail_settings.updated", "OrganizationMailSettings", settings.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "SMTP 設定已儲存。密碼以資料保護機制加密保存。";
        return RedirectToPage(new { section = "settings", stage = "mail" });
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
            await _emailSender.SendTestMessageAsync(normalizedRecipient);
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

        return RedirectToPage(new { section = "settings", stage = "mail" });
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
        return RedirectToPage(new { section = Section });
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
                cancellationToken);
            var link = Url.Page("/AcceptInvitation", pageHandler: null, values: new { token }, protocol: Request.Scheme)
                ?? throw new InvalidOperationException("無法建立邀請連結。");
            await _emailSender.SendOrganizationInvitationAsync(invitationEmail.Trim(), link);
            StatusMessage = "組織邀請已寄出。";
            return RedirectToPage(new { section = Section });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError("invitation", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
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
        if (!invitation.AcceptedAt.HasValue)
        {
            invitation.RevokedAt = DateTimeOffset.UtcNow;
            AddAudit("organization.invitation.revoked", "OrganizationInvitation", invitation.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostRevokeMemberAsync(Guid membershipId, CancellationToken cancellationToken)
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
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { section = Section });
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
        return RedirectToPage(new { section = Section });
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
        var pcrRuleSet = ToPcrRuleSet(pcr, pcrStageRules);
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

        var projectId = Guid.NewGuid();
        _dbContext.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId,
            OrganizationId = organizationId,
            ProductVersionId = productVersionId,
            VersionNumber = 1,
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
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "盤查專案第 1 版已建立。";
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostSetStageApplicabilityAsync(
        Guid stageDeclarationId,
        bool isApplicable,
        string reason,
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
                item => item.InventoryProjectVersionId == project.Id && item.LifecycleStage == declaration.LifecycleStage,
                cancellationToken))
        {
            ModelState.AddModelError("stage", "已有活動數據的階段不可標記為不適用。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        declaration.IsApplicable = isApplicable;
        declaration.Reason = isApplicable ? string.Empty : reason.Trim();
        AddAudit("inventory.stage.applicability.changed", "LifecycleStageDeclaration", declaration.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { section = Section });
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
        catch (InvalidOperationException exception)
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
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostReviewPcrAsync(Guid pcrVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var pcr = await _dbContext.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null)
        {
            return NotFound();
        }
        if (pcr.PublicationStatus != PcrPublicationStatus.Draft.ToString())
        {
            return BadRequest();
        }
        if (pcr.ReviewStatus != PcrReviewStatus.Pending.ToString())
        {
            return BadRequest();
        }

        var reviewerId = Guid.TryParse(_userManager.GetUserId(User), out var currentUserId)
            ? currentUserId
            : (Guid?)null;
        if (pcr.CreatedBy.HasValue && pcr.CreatedBy == reviewerId)
        {
            ModelState.AddModelError("pcr", "PCR 建立者不可核准自己的規則版本，請由另一位具權限且已啟用 MFA 的人員審查。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        pcr.ReviewStatus = PcrReviewStatus.Approved.ToString();
        if (pcr.IsCustomRule)
        {
            pcr.CustomApprovalStatus = PcrCustomApprovalStatus.Approved.ToString();
        }
        pcr.ReviewedAt = DateTimeOffset.UtcNow;
        pcr.ReviewedBy = reviewerId;
        AddAudit("pcr.version.reviewed", "PcrVersion", pcr.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostRejectPcrAsync(Guid pcrVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var pcr = await _dbContext.PcrVersions.SingleOrDefaultAsync(
            item => item.Id == pcrVersionId,
            cancellationToken);
        if (pcr is null)
        {
            return NotFound();
        }
        if (pcr.PublicationStatus != PcrPublicationStatus.Draft.ToString()
            || pcr.ReviewStatus != PcrReviewStatus.Pending.ToString())
        {
            return BadRequest();
        }

        var reviewerId = Guid.TryParse(_userManager.GetUserId(User), out var currentUserId)
            ? currentUserId
            : (Guid?)null;
        if (pcr.CreatedBy.HasValue && pcr.CreatedBy == reviewerId)
        {
            ModelState.AddModelError("pcr", "PCR 建立者不可審查自己建立的版本。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        pcr.ReviewStatus = PcrReviewStatus.Rejected.ToString();
        if (pcr.IsCustomRule)
        {
            pcr.CustomApprovalStatus = PcrCustomApprovalStatus.Rejected.ToString();
        }
        pcr.ReviewedAt = DateTimeOffset.UtcNow;
        pcr.ReviewedBy = reviewerId;
        AddAudit("pcr.version.rejected", "PcrVersion", pcr.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostPublishPcrAsync(Guid pcrVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }
        if (!await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var pcr = await _dbContext.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null)
        {
            return NotFound();
        }

        var hasControlledDeclaredUnit = await _dbContext.Units.AnyAsync(
            item => item.CatalogueVersion == CurrentUnitCatalogueVersion
                && item.Code == pcr.DeclaredUnitCode,
            cancellationToken);
        var hasLaterReleasedVersion = await _dbContext.PcrVersions.AnyAsync(
            item => item.RuleSetId == pcr.RuleSetId
                && item.VersionNumber > pcr.VersionNumber
                && item.PublicationStatus != PcrPublicationStatus.Draft.ToString(),
            cancellationToken);
        if (!string.Equals(pcr.PublicationStatus, PcrPublicationStatus.Draft.ToString(), StringComparison.Ordinal)
            || pcr.ReviewStatus != PcrReviewStatus.Approved.ToString()
            || hasLaterReleasedVersion
            || (pcr.IsCustomRule
                && pcr.CustomApprovalStatus != PcrCustomApprovalStatus.Approved.ToString())
            || !SourceDocumentIntegrity.TryNormalizeSha256(pcr.OriginalDocumentSha256, out _)
            || string.IsNullOrWhiteSpace(pcr.OriginalDocumentObjectKey)
            || !string.Equals(pcr.OriginalDocumentScanStatus, "Clean", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(pcr.ProductCategoryPatterns)
            || string.IsNullOrWhiteSpace(pcr.FunctionalUnitPattern)
            || string.IsNullOrWhiteSpace(pcr.DeclaredUnitCode)
            || !hasControlledDeclaredUnit
            || string.IsNullOrWhiteSpace(pcr.SystemBoundaryCode)
            || !string.Equals(
                pcr.FormulaRuleSetVersion,
                ActivityEmissionFormula.PcrFormulaRuleSetV1,
                StringComparison.Ordinal)
            || await _dbContext.PcrStageRules.CountAsync(
                item => item.PcrVersionId == pcr.Id,
                cancellationToken) != Enum.GetValues<LifecycleStage>().Length)
        {
            ModelState.AddModelError("pcr", "只有已核准、原始文件驗證成功且規則完整的 PCR 草稿可發布。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        pcr.PublicationStatus = PcrPublicationStatus.Published.ToString();
        pcr.PublishedAt = DateTimeOffset.UtcNow;
        var publishedPredecessors = await _dbContext.PcrVersions
            .Where(item => item.RuleSetId == pcr.RuleSetId
                && item.Id != pcr.Id
                && item.VersionNumber < pcr.VersionNumber
                && item.PublicationStatus == PcrPublicationStatus.Published.ToString())
            .ToArrayAsync(cancellationToken);
        foreach (var predecessor in publishedPredecessors)
        {
            predecessor.DeprecatedAt = DateTimeOffset.UtcNow;
            predecessor.DeprecationReason = $"由 {pcr.RegistrationNumber} 第 {pcr.VersionNumber} 版取代。";
            AddAudit("pcr.version.superseded", "PcrVersion", predecessor.Id);
        }
        AddAudit("pcr.version.published", "PcrVersion", pcr.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "PCR 版本已發布。";
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostWithdrawPcrAsync(Guid pcrVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }
        if (!await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var pcr = await _dbContext.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null)
        {
            return NotFound();
        }

        if (!string.Equals(pcr.PublicationStatus, PcrPublicationStatus.Published.ToString(), StringComparison.Ordinal))
        {
            ModelState.AddModelError("pcr", "只有已發布 PCR 版本可撤回。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        pcr.PublicationStatus = PcrPublicationStatus.Withdrawn.ToString();
        pcr.WithdrawnAt = DateTimeOffset.UtcNow;
        AddAudit("pcr.version.withdrawn", "PcrVersion", pcr.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "PCR 版本已撤回；歷史計算不受影響。";
        return RedirectToPage(new { section = Section });
    }

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
        return RedirectToPage(new { section = Section });
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
            return RedirectToPage(new { section = "factors" });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            ModelState.AddModelError("factor", $"環境部係數同步失敗：{exception.Message}");
            Section = "factors";
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostReviewFactorAsync(Guid factorVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }
        var factor = await _dbContext.EmissionFactorVersions.SingleOrDefaultAsync(item => item.Id == factorVersionId, cancellationToken);
        if (factor is null)
        {
            return NotFound();
        }
        if (factor.PublicationStatus != FactorPublicationStatus.Draft.ToString())
        {
            return BadRequest();
        }

        factor.ReviewStatus = FactorReviewStatus.Approved.ToString();
        factor.ReviewedAt = DateTimeOffset.UtcNow;
        factor.ReviewedBy = Guid.TryParse(_userManager.GetUserId(User), out var reviewerId) ? reviewerId : null;
        AddAudit("factor.version.reviewed", "EmissionFactorVersion", factor.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostPublishFactorAsync(Guid factorVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }
        if (!await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var factor = await _dbContext.EmissionFactorVersions.SingleOrDefaultAsync(
            item => item.Id == factorVersionId,
            cancellationToken);
        if (factor is null)
        {
            return NotFound();
        }

        if (!string.Equals(factor.PublicationStatus, FactorPublicationStatus.Draft.ToString(), StringComparison.Ordinal)
            || factor.ReviewStatus != FactorReviewStatus.Approved.ToString())
        {
            ModelState.AddModelError("factor", "只有草稿係數版本可發布。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var publishedPredecessors = await _dbContext.EmissionFactorVersions
            .Where(item =>
                item.FactorId == factor.FactorId
                && item.Id != factor.Id
                && item.PublicationStatus == FactorPublicationStatus.Published.ToString())
            .ToArrayAsync(cancellationToken);
        foreach (var predecessor in publishedPredecessors)
        {
            predecessor.PublicationStatus = FactorPublicationStatus.Withdrawn.ToString();
            predecessor.WithdrawnAt = DateTimeOffset.UtcNow;
            AddAudit("factor.version.withdrawn", "EmissionFactorVersion", predecessor.Id);
        }

        factor.PublicationStatus = FactorPublicationStatus.Published.ToString();
        factor.PublishedAt = DateTimeOffset.UtcNow;
        AddAudit("factor.version.published", "EmissionFactorVersion", factor.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "係數版本已發布。";
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostWithdrawFactorAsync(Guid factorVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }
        if (!await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var factor = await _dbContext.EmissionFactorVersions.SingleOrDefaultAsync(
            item => item.Id == factorVersionId,
            cancellationToken);
        if (factor is null)
        {
            return NotFound();
        }

        if (!string.Equals(factor.PublicationStatus, FactorPublicationStatus.Published.ToString(), StringComparison.Ordinal))
        {
            ModelState.AddModelError("factor", "只有已發布係數版本可撤回。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        factor.PublicationStatus = FactorPublicationStatus.Withdrawn.ToString();
        factor.WithdrawnAt = DateTimeOffset.UtcNow;
        AddAudit("factor.version.withdrawn", "EmissionFactorVersion", factor.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "係數版本已撤回；歷史計算不受影響。";
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostSupersedeFactorAsync(
        Guid factorVersionId,
        decimal? newFactorValue,
        string newSourceDatasetVersion,
        string newFactorSourceReference,
        string newOriginalDocumentName,
        string newApplicability,
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
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "更新草稿已建立；現行係數會保留至新版本審查發布，歷史計算不受影響。";
        return RedirectToPage(new { section = Section });
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
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        if (inventoryProjectVersionId == Guid.Empty || factorVersionId == Guid.Empty)
        {
            ModelState.AddModelError("activity", "請先建立盤查版本並選擇已發布排放係數。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var project = await _dbContext.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == inventoryProjectVersionId && item.OrganizationId == organizationId,
            cancellationToken);
        var factor = await _dbContext.EmissionFactorVersions.SingleOrDefaultAsync(
            item =>
                item.Id == factorVersionId
                && item.OrganizationId == organizationId
                && item.PublicationStatus == FactorPublicationStatus.Published.ToString(),
            cancellationToken);
        if (project is null || factor is null)
        {
            ModelState.AddModelError("activity", "所選盤查版本或排放係數不存在於目前組織。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus)))
        {
            ModelState.AddModelError("activity", "盤查已送審或核准，不可再新增活動資料。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var stageDeclaration = await _dbContext.LifecycleStageDeclarations.SingleOrDefaultAsync(
            item => item.InventoryProjectVersionId == project.Id && item.LifecycleStage == (int)lifecycleStage,
            cancellationToken);
        if (stageDeclaration is null || !stageDeclaration.IsApplicable)
        {
            ModelState.AddModelError("activity", "不適用階段不可新增活動數據。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var factorVersion = new EmissionFactorVersion(
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
        if (!factorVersion.IsSelectableOn(project.PeriodEnd))
        {
            ModelState.AddModelError("activity", "係數版本未發布、已撤回或不在盤查期間有效範圍。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var hasName = TryResolveControlledValue(activityName, activityNameOther, out var resolvedName);
        var hasEquipment = TryResolveOptionalControlledValue(equipmentCategory, equipmentCategoryOther, out var equipment);
        var hasSource = TryResolveControlledValue(dataSourceType, dataSourceTypeOther, out var sourceType);
        var hasProvider = TryResolveControlledValue(dataProviderType, dataProviderOther, out var provider);
        var hasMethod = TryResolveControlledValue(collectionMethod, collectionMethodOther, out var method);
        if (!hasName
            || !hasEquipment
            || !hasSource
            || !hasProvider
            || !hasMethod
            || string.IsNullOrWhiteSpace(sourceReference)
            || !ActivityKindRules.IsAllowed(lifecycleStage, activityKind)
            || allocationFactor is null or <= 0m or > 1m
            || string.IsNullOrWhiteSpace(dataQuality)
            || (isEstimated && string.IsNullOrWhiteSpace(activityEstimationReason)))
        {
            ModelState.AddModelError("activity", "活動項目、來源類型、提供者、取得方式與來源參照皆為必填。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var unitCatalogueVersion = await GetProjectUnitCatalogueVersionAsync(project.Id, cancellationToken);
            var derivedAmount = ActivityAmountFormula.Derive(
                activityKind,
                rawValue,
                rawUnitCode,
                transportDistanceKm,
                transportWeightKg,
                useLifetime,
                useFrequency,
                useConsumptionPerUse);
            var effectiveCanonicalUnitCode = ActivityAmountFormula.IsTransport(activityKind)
                ? derivedAmount.UnitCode
                : canonicalUnitCode;
            var unitRecords = await _dbContext.Units
                .Where(item => item.CatalogueVersion == unitCatalogueVersion
                    && (item.Code == derivedAmount.UnitCode || item.Code == effectiveCanonicalUnitCode))
                .ToArrayAsync(cancellationToken);
            var catalogue = new UnitCatalogue(
                unitCatalogueVersion,
                unitRecords.Select(item => new UnitDefinition(
                    item.Id,
                    item.Code,
                    item.Dimension,
                    item.ScaleToCanonical,
                    item.OffsetToCanonical,
                    item.CanonicalCode,
                    item.CatalogueVersion,
                    item.AliasesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    string.IsNullOrWhiteSpace(item.CompositeExpression) ? null : item.CompositeExpression)));
            var canonicalValue = catalogue.Convert(
                derivedAmount.Value,
                derivedAmount.UnitCode,
                effectiveCanonicalUnitCode);
            if (!string.Equals(effectiveCanonicalUnitCode, factor.DenominatorUnitCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("活動標準單位必須等於係數分母單位。");
            }

            var activityId = Guid.NewGuid();
            _dbContext.ActivityData.Add(new ActivityDataRecord
            {
                Id = activityId,
                OrganizationId = organizationId,
                InventoryProjectVersionId = project.Id,
                LifecycleStage = (int)lifecycleStage,
                Name = resolvedName,
                ActivityKind = activityKind.ToString(),
                SupplierOrScenario = string.Join(
                    "｜",
                    new[] { supplierOrScenario?.Trim(), $"計算基礎：{derivedAmount.FormulaTrace}" }
                        .Where(item => !string.IsNullOrWhiteSpace(item))),
                EquipmentCategory = equipment,
                DataSourceType = sourceType,
                DataProvider = provider,
                CollectionMethod = method,
                SourceReference = sourceReference.Trim(),
                RawValue = derivedAmount.Value,
                RawUnitCode = derivedAmount.UnitCode,
                CanonicalValue = canonicalValue,
                CanonicalUnitCode = effectiveCanonicalUnitCode,
                ConversionRuleVersion = unitCatalogueVersion,
                AmountFormulaId = derivedAmount.FormulaId,
                FormulaInputsJson = JsonSerializer.Serialize(derivedAmount.Inputs),
                PeriodStart = project.PeriodStart,
                PeriodEnd = project.PeriodEnd,
                FactorVersionId = factor.Id,
                AllocationFactor = allocationFactor.Value,
                IsEstimated = isEstimated,
                EstimationReason = activityEstimationReason?.Trim() ?? string.Empty,
                DataQuality = dataQuality.Trim(),
                EvidenceSha256 = null
            });
            AddAudit("activity.version.created", "ActivityDataVersion", activityId);
            await _dbContext.SaveChangesAsync(cancellationToken);
            StatusMessage = "活動數據已保存。";
            return RedirectToPage(new { section = Section, stage = Stage, projectVersionId = project.Id });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError("activity", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadEvidenceAsync(
        Guid activityDataId,
        IFormFile evidenceFile,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }

        var activity = await _dbContext.ActivityData.SingleOrDefaultAsync(
            item => item.Id == activityDataId,
            cancellationToken);
        if (activity is null)
        {
            return NotFound();
        }

        var evidenceProject = await _dbContext.InventoryProjectVersions.SingleAsync(
            item => item.Id == activity.InventoryProjectVersionId,
            cancellationToken);
        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(evidenceProject.WorkflowStatus)))
        {
            ModelState.AddModelError("evidence", "盤查已送審或核准，不可再變更佐證檔案。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (evidenceFile is null || evidenceFile.Length <= 0)
        {
            ModelState.AddModelError("evidence", "請選擇非空白佐證檔案。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (await _dbContext.EvidenceFiles.AnyAsync(item => item.ActivityDataId == activity.Id, cancellationToken))
        {
            ModelState.AddModelError("evidence", "每筆活動僅允許一份佐證檔案；請建立活動資料新版本以更換。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await using var content = evidenceFile.OpenReadStream();
            var stored = await _evidenceStorageService.StoreAsync(
                RequireOrganization(),
                content,
                evidenceFile.FileName,
                evidenceFile.ContentType,
                cancellationToken);
            _dbContext.EvidenceFiles.Add(new EvidenceFileRecord
            {
                Id = stored.Id,
                OrganizationId = RequireOrganization(),
                ActivityDataId = activity.Id,
                ObjectKey = stored.ObjectKey,
                OriginalFileName = stored.OriginalFileName,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                Sha256 = stored.Sha256,
                ScanStatus = stored.ScanStatus.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            });
            activity.EvidenceSha256 = stored.Sha256;
            AddAudit("evidence.uploaded", "EvidenceFile", stored.Id);
            await _dbContext.SaveChangesAsync(cancellationToken);
            StatusMessage = "佐證檔案已通過惡意程式掃描、寫入物件儲存並綁定活動。";
            return RedirectToPage(new
            {
                section = Section,
                stage = Stage,
                projectVersionId = activity.InventoryProjectVersionId
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ModelState.AddModelError("evidence", $"佐證檔案未保存：{exception.Message}");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSubmitInventoryAsync(Guid inventoryProjectVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var project = await _dbContext.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == inventoryProjectVersionId,
            cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var latestRun = await _dbContext.CalculationRuns
            .Where(item => item.ProjectVersionId == project.Id)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestRun is null)
        {
            ModelState.AddModelError("review", "盤查至少需要一個不可變計算版本才能送審。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (string.Equals(latestRun.RuleSetVersion, PendingStageFormulaRuleSetVersion, StringComparison.Ordinal))
        {
            ModelState.AddModelError("review", "階段計算公式尚待領域審查，目前不可統一提交。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var currentSnapshot = await BuildSnapshotAsync(project, cancellationToken);
        if (!CanonicalManifest.Matches(currentSnapshot, latestRun.EngineBuild, latestRun.InputSha256))
        {
            ModelState.AddModelError("review", "盤查資料已在最近一次計算後變更，請重新計算再提交。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var current = Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus);
        try
        {
            InventoryWorkflow.EnsureTransition(current, InventoryWorkflowStatus.Submitted);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError("review", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }

        project.WorkflowStatus = InventoryWorkflowStatus.Submitted.ToString();
        project.SubmittedAt = DateTimeOffset.UtcNow;
        AddAudit("inventory.submitted", "InventoryProjectVersion", project.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "盤查版本已送審。";
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnPostReviewInventoryAsync(
        Guid inventoryProjectVersionId,
        InventoryWorkflowStatus decision,
        string? reviewComment,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ReviewInventory))
        {
            return Forbid();
        }
        if (!await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        if (!Guid.TryParse(_userManager.GetUserId(User), out var reviewerId))
        {
            return Forbid();
        }

        if (decision is not InventoryWorkflowStatus.Approved and not InventoryWorkflowStatus.ChangesRequested)
        {
            return BadRequest();
        }

        if (decision == InventoryWorkflowStatus.ChangesRequested && string.IsNullOrWhiteSpace(reviewComment))
        {
            ModelState.AddModelError("review", "要求補正時必須提供審查意見。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var project = await _dbContext.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == inventoryProjectVersionId,
            cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var current = Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus);
        try
        {
            InventoryWorkflow.EnsureTransition(current, decision);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError("review", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }

        project.WorkflowStatus = decision.ToString();
        project.ReviewedAt = DateTimeOffset.UtcNow;
        project.ReviewedBy = reviewerId;
        project.ReviewComment = reviewComment?.Trim();
        AddAudit(
            decision == InventoryWorkflowStatus.Approved ? "inventory.approved" : "inventory.changes-requested",
            "InventoryProjectVersion",
            project.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = decision == InventoryWorkflowStatus.Approved ? "盤查版本已核准。" : "盤查版本已退回補正。";
        return RedirectToPage(new { section = Section });
    }

    public async Task<IActionResult> OnGetExportExcelAsync(
        Guid projectVersionId,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ViewInventory) || !await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        var project = await _dbContext.InventoryProjectVersions.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == projectVersionId && item.OrganizationId == organizationId,
                cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var latestRun = await _dbContext.CalculationRuns.AsNoTracking()
            .Where(item => item.ProjectVersionId == project.Id)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var lines = latestRun is null
            ? []
            : await _dbContext.CalculationLineItems.AsNoTracking()
                .Where(item => item.CalculationRunId == latestRun.Id)
                .OrderBy(item => item.LifecycleStage)
                .ThenBy(item => item.ActivityId)
                .ToArrayAsync(cancellationToken);
        var activityIds = lines.Select(item => item.ActivityId).Distinct().ToArray();
        var activityQuery = _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == project.Id);
        if (latestRun is not null)
        {
            activityQuery = activityQuery.Where(item => activityIds.Contains(item.Id));
        }

        var activities = await activityQuery
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.Name)
            .ToArrayAsync(cancellationToken);
        var factorIds = activities.Select(item => item.FactorVersionId).Distinct().ToArray();
        var factors = await _dbContext.EmissionFactorVersions.AsNoTracking()
            .Where(item => factorIds.Contains(item.Id))
            .OrderBy(item => item.Name)
            .ToArrayAsync(cancellationToken);
        var factorById = factors.ToDictionary(item => item.Id);

        var summaryRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "欄位", "內容" },
            new object?[] { "功能單位", project.FunctionalUnit },
            new object?[] { "宣告單位", project.DeclaredUnit },
            new object?[] { "盤查期間", $"{project.PeriodStart:yyyy-MM-dd}～{project.PeriodEnd:yyyy-MM-dd}" },
            new object?[] { "系統邊界", project.SystemBoundary },
            new object?[] { "分配方法", project.AllocationMethod },
            new object?[] { "PCR 版本", project.PcrVersion },
            new object?[]
            {
                "資料範圍",
                latestRun is null
                    ? "尚未建立計算版本；匯出目前活動資料"
                    : $"計算版本 {latestRun.Id:N} 的不可變輸入與結果"
            },
            new object?[] { "計算結果", latestRun?.ProductTotal },
            new object?[] { "結果單位", latestRun is null ? string.Empty : "kgCO2e" }
        };
        var activityRows = new List<IReadOnlyList<object?>>
        {
            new object?[]
            {
                "階段", "活動名稱", "活動類型", "原始活動量", "原始單位", "標準活動量", "標準單位",
                "係數名稱", "係數版本", "係數值", "係數單位", "分配比例", "資料品質", "來源"
            }
        };
        activityRows.AddRange(activities.Select(activity =>
        {
            factorById.TryGetValue(activity.FactorVersionId, out var factor);
            return (IReadOnlyList<object?>)new object?[]
            {
                LifecycleStageDisplayName((LifecycleStage)activity.LifecycleStage),
                activity.Name,
                ActivityKindDisplayName(Enum.Parse<ActivityDataKind>(activity.ActivityKind)),
                activity.RawValue,
                activity.RawUnitCode,
                activity.CanonicalValue,
                activity.CanonicalUnitCode,
                factor?.Name ?? string.Empty,
                factor?.VersionNumber,
                factor?.Value,
                factor is null ? string.Empty : $"{factor.NumeratorUnitCode}/{factor.DenominatorUnitCode}",
                activity.AllocationFactor,
                activity.DataQuality,
                activity.SourceReference
            };
        }));
        var factorRows = new List<IReadOnlyList<object?>>
        {
            new object?[]
            {
                "係數名稱", "版本", "係數值", "單位", "地域", "來源機構", "來源資料集",
                "公告版本", "來源網址", "原始資料 SHA-256"
            }
        };
        factorRows.AddRange(factors.Select(factor => (IReadOnlyList<object?>)new object?[]
        {
            factor.Name,
            factor.VersionNumber,
            factor.Value,
            $"{factor.NumeratorUnitCode}/{factor.DenominatorUnitCode}",
            GeographyDisplayName(factor.Geography),
            factor.SourceName,
            factor.DatasetName,
            factor.SourceDatasetVersion,
            factor.SourceReference,
            factor.OriginalDocumentSha256
        }));
        var resultRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "階段", "活動 ID", "標準活動量", "單位", "係數值", "係數單位", "排放量", "結果單位" }
        };
        resultRows.AddRange(lines.Select(line => (IReadOnlyList<object?>)new object?[]
        {
            LifecycleStageDisplayName((LifecycleStage)line.LifecycleStage),
            line.ActivityId,
            line.CanonicalActivityValue,
            line.ActivityUnitCode,
            line.FactorValue,
            line.FactorUnit,
            line.Emissions,
            line.EmissionsUnitCode
        }));

        var workbook = ExcelWorkbook.Create(
        [
            new ExcelSheet("盤查摘要", summaryRows),
            new ExcelSheet("五階段活動", activityRows),
            new ExcelSheet("使用係數", factorRows),
            new ExcelSheet("計算結果", resultRows)
        ]);
        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"碳足跡盤查-{project.Id:N}.xlsx");
    }

    public async Task<IActionResult> OnPostCalculateAsync(Guid inventoryProjectVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.CreateCalculationRun))
        {
            return Forbid();
        }
        if (!await IsMfaEnabledAsync())
        {
            return Forbid();
        }

        var project = await _dbContext.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == inventoryProjectVersionId,
            cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus)))
        {
            ModelState.AddModelError("calculation", "盤查已送審或核准，不可建立新計算版本。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!project.PcrVersionId.HasValue)
        {
            ModelState.AddModelError("calculation", "盤查版本未綁定受治理的 PCR 版本。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var pcrViolations = await ValidatePcrProjectAsync(
            project,
            requireCompleteInventory: true,
            cancellationToken);
        if (pcrViolations.Count > 0)
        {
            foreach (var violation in pcrViolations)
            {
                ModelState.AddModelError("calculation", $"{violation.Code}：{violation.Message}");
            }
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var snapshot = await BuildSnapshotAsync(project, cancellationToken);

            var engineBuild = typeof(WorkspaceModel).Assembly.GetName().Version?.ToString() ?? "dev";
            var supersedesRunId = await _dbContext.CalculationRuns
                .Where(item => item.ProjectVersionId == project.Id)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            await _calculateHandler.HandleAsync(
                new CalculateInventoryCommand(Guid.NewGuid(), snapshot, engineBuild, supersedesRunId),
                cancellationToken);
            StatusMessage = "不可變計算版本已建立。";
            return RedirectToPage(new { section = Section, projectVersionId = project.Id });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError("calculation", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task<InventoryProjectSnapshot> BuildSnapshotAsync(
        InventoryProjectVersionRecord project,
        CancellationToken cancellationToken)
    {
        var activities = await _dbContext.ActivityData
            .Where(item => item.InventoryProjectVersionId == project.Id)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var stageDeclarations = await _dbContext.LifecycleStageDeclarations
            .Where(item => item.InventoryProjectVersionId == project.Id)
            .OrderBy(item => item.LifecycleStage)
            .ToArrayAsync(cancellationToken);
        var factorIds = activities.Select(item => item.FactorVersionId).Distinct().ToArray();
        var factorRecords = await _dbContext.EmissionFactorVersions
            .Where(item => factorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var pcr = await _dbContext.PcrVersions.AsNoTracking().SingleAsync(
            item => item.Id == project.PcrVersionId,
            cancellationToken);

        return new InventoryProjectSnapshot(
            RequireOrganization(),
            project.Id,
            project.ProductVersionId,
            project.PeriodStart,
            project.PeriodEnd,
            project.FunctionalUnit,
            project.PcrVersion,
            pcr.FormulaRuleSetVersion,
            "gwp-fixture-p0-v1",
            GetActivityUnitCatalogueVersion(activities),
            stageDeclarations.Select(item => new StageDeclaration(
                (LifecycleStage)item.LifecycleStage,
                item.IsApplicable,
                string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason)).ToArray(),
            activities.Select(activity =>
            {
                var factor = factorRecords[activity.FactorVersionId];
                return new ActivityDataSnapshot(
                    activity.Id,
                    activity.OrganizationId,
                    (LifecycleStage)activity.LifecycleStage,
                    activity.Name,
                    activity.RawValue,
                    activity.RawUnitCode,
                    activity.CanonicalValue,
                    activity.CanonicalUnitCode,
                    activity.ConversionRuleVersion,
                    activity.PeriodStart,
                    activity.PeriodEnd,
                    new EmissionFactorVersion(
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
                        factor.Applicability),
                    activity.EvidenceSha256,
                    Enum.Parse<ActivityDataKind>(activity.ActivityKind),
                    string.IsNullOrWhiteSpace(activity.SupplierOrScenario) ? null : activity.SupplierOrScenario,
                    activity.AllocationFactor,
                    activity.IsEstimated,
                    string.IsNullOrWhiteSpace(activity.EstimationReason) ? null : activity.EstimationReason,
                    activity.DataQuality,
                    activity.AmountFormulaId,
                    activity.FormulaInputsJson,
                    activity.EquipmentCategory,
                    activity.DataSourceType,
                    activity.DataProvider,
                    activity.CollectionMethod,
                    activity.SourceReference);
            }).ToArray(),
            project.DeclaredUnit,
            project.SystemBoundary,
            project.AllocationMethod,
            project.AllocationReason,
            project.Exclusions,
            project.Assumptions,
            project.EstimationReason,
            pcr.CutoffThresholdPercent,
            pcr.RoundingDecimalPlaces,
            pcr.ReportingRequirements);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!OrganizationId.HasValue)
        {
            return;
        }

        if (Section == "settings")
        {
            CanManageOrganization = await IsAllowedAsync(OrganizationPermission.ManageOrganization);
            MailSettings = await _dbContext.OrganizationMailSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
        }

        ProductVersions = await _dbContext.ProductVersions.AsNoTracking().OrderBy(item => item.NameZhTw).ToArrayAsync(cancellationToken);
        Facilities = await _dbContext.Facilities.AsNoTracking().OrderBy(item => item.Code).ToArrayAsync(cancellationToken);
        Memberships = await _dbContext.OrganizationMemberships.AsNoTracking().OrderBy(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        Invitations = await _dbContext.OrganizationInvitations.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        PcrVersions = await _dbContext.PcrVersions.AsNoTracking().OrderBy(item => item.RegistrationNumber).ThenByDescending(item => item.VersionNumber).ToArrayAsync(cancellationToken);
        PcrStageRules = await _dbContext.PcrStageRules.AsNoTracking().OrderBy(item => item.LifecycleStage).ToArrayAsync(cancellationToken);
        SelectablePcrVersions = PcrVersions
            .Where(item => ToPcrRuleSet(
                    item,
                    PcrStageRules.Where(rule => rule.PcrVersionId == item.Id))
                .IsPublishedAndApproved)
            .ToArray();
        InventoryProjects = await _dbContext.InventoryProjectVersions.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        PcrAffectedProjectCounts = InventoryProjects
            .Where(item => item.PcrVersionId.HasValue)
            .GroupBy(item => item.PcrVersionId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        PcrAffectedProjects = InventoryProjects
            .Where(item => item.PcrVersionId.HasValue)
            .GroupBy(item => item.PcrVersionId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<InventoryProjectVersionRecord>)group
                    .OrderBy(item => item.PeriodEnd)
                    .ToArray());
        if (!ProjectVersionId.HasValue || InventoryProjects.All(item => item.Id != ProjectVersionId.Value))
        {
            ProjectVersionId = InventoryProjects.FirstOrDefault()?.Id;
        }
        StageDeclarations = await _dbContext.LifecycleStageDeclarations.AsNoTracking().OrderBy(item => item.LifecycleStage).ToArrayAsync(cancellationToken);
        Factors = await _dbContext.EmissionFactorVersions.AsNoTracking().OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        SelectableFactors = Factors
            .Where(item => item.PublicationStatus == FactorPublicationStatus.Published.ToString())
            .ToArray();
        Activities = await _dbContext.ActivityData.AsNoTracking().OrderBy(item => item.LifecycleStage).ThenBy(item => item.Name).ToArrayAsync(cancellationToken);
        var selectedUnitCatalogueVersion = ProjectVersionId.HasValue
            ? GetActivityUnitCatalogueVersion(Activities.Where(item => item.InventoryProjectVersionId == ProjectVersionId.Value))
            : CurrentUnitCatalogueVersion;
        Units = await _dbContext.Units.AsNoTracking()
            .Where(item => item.CatalogueVersion == selectedUnitCatalogueVersion)
            .OrderBy(item => item.Code)
            .ToArrayAsync(cancellationToken);
        EvidenceFiles = await _dbContext.EvidenceFiles.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        Runs = await _dbContext.CalculationRuns.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        PendingFormulaRunProjectIds = Runs
            .Where(item => string.Equals(item.RuleSetVersion, PendingStageFormulaRuleSetVersion, StringComparison.Ordinal))
            .Select(item => item.ProjectVersionId)
            .ToHashSet();
        if (Section == "calculation")
        {
            var currentRunProjectIds = new HashSet<Guid>();
            foreach (var project in InventoryProjects)
            {
                var latestRun = Runs.FirstOrDefault(item => item.ProjectVersionId == project.Id);
                if (latestRun is not null
                    && !string.Equals(latestRun.RuleSetVersion, PendingStageFormulaRuleSetVersion, StringComparison.Ordinal)
                    && CanonicalManifest.Matches(
                        await BuildSnapshotAsync(project, cancellationToken),
                        latestRun.EngineBuild,
                        latestRun.InputSha256))
                {
                    currentRunProjectIds.Add(project.Id);
                }
            }

            CurrentRunProjectIds = currentRunProjectIds;
        }
        var selectedRuns = ProjectVersionId.HasValue
            ? Runs.Where(item => item.ProjectVersionId == ProjectVersionId.Value).ToArray()
            : Array.Empty<CalculationRunRecord>();
        if (selectedRuns.Length > 0)
        {
            LatestManifestHashValid = CanonicalManifest.HasValidSha256(
                selectedRuns[0].CanonicalInputManifest,
                selectedRuns[0].InputSha256);
            LatestLines = await _dbContext.CalculationLineItems.AsNoTracking()
                .Where(item => item.CalculationRunId == selectedRuns[0].Id)
                .OrderBy(item => item.LifecycleStage)
                .ThenBy(item => item.ActivityId)
                .ToArrayAsync(cancellationToken);
            LatestWarnings = await _dbContext.CalculationWarnings.AsNoTracking()
                .Where(item => item.CalculationRunId == selectedRuns[0].Id)
                .OrderBy(item => item.Code)
                .ToArrayAsync(cancellationToken);
        }

        if (selectedRuns.Length > 1)
        {
            var comparedRunIds = new[] { selectedRuns[0].Id, selectedRuns[1].Id };
            var summaries = await _dbContext.CalculationStageSummaries.AsNoTracking()
                .Where(item => comparedRunIds.Contains(item.CalculationRunId))
                .ToArrayAsync(cancellationToken);
            var baseline = new CalculationRunTotals(
                selectedRuns[1].Id,
                selectedRuns[1].ProductTotal,
                summaries.Where(item => item.CalculationRunId == selectedRuns[1].Id)
                    .ToDictionary(item => (LifecycleStage)item.LifecycleStage, item => item.Emissions));
            var candidate = new CalculationRunTotals(
                selectedRuns[0].Id,
                selectedRuns[0].ProductTotal,
                summaries.Where(item => item.CalculationRunId == selectedRuns[0].Id)
                    .ToDictionary(item => (LifecycleStage)item.LifecycleStage, item => item.Emissions));
            LatestDifference = CalculationRunDiff.Compare(baseline, candidate);
        }
    }

    private Guid RequireOrganization() => OrganizationId
        ?? throw new InvalidOperationException("請先建立組織。");

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

    private async Task<string> GetProjectUnitCatalogueVersionAsync(Guid projectVersionId, CancellationToken cancellationToken)
    {
        var versions = await _dbContext.ActivityData
            .Where(item => item.InventoryProjectVersionId == projectVersionId)
            .Select(item => item.ConversionRuleVersion)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return GetActivityUnitCatalogueVersion(versions);
    }

    private static string GetActivityUnitCatalogueVersion(IEnumerable<ActivityDataRecord> activities) =>
        GetActivityUnitCatalogueVersion(activities.Select(item => item.ConversionRuleVersion));

    private static string GetActivityUnitCatalogueVersion(IEnumerable<string> versions)
    {
        var distinctVersions = versions.Distinct(StringComparer.Ordinal).ToArray();
        return distinctVersions.Length switch
        {
            0 => CurrentUnitCatalogueVersion,
            1 => distinctVersions[0],
            _ => throw new InvalidOperationException("同一盤查專案不可混用不同單位目錄版本。")
        };
    }

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

    private static bool TryResolveOptionalControlledValue(string? selected, string? other, out string value)
    {
        value = string.Empty;
        return string.IsNullOrWhiteSpace(selected) || TryResolveControlledValue(selected, other, out value);
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

    private async Task<IReadOnlyList<PcrRuleViolation>> ValidatePcrProjectAsync(
        InventoryProjectVersionRecord project,
        bool requireCompleteInventory,
        CancellationToken cancellationToken)
    {
        if (!project.PcrVersionId.HasValue)
        {
            return
            [
                new(
                    "PCR-MISSING",
                    "InventoryProjectVersion",
                    project.Id.ToString(),
                    "盤查版本未綁定 PCR 規則版本。")
            ];
        }

        var pcr = await _dbContext.PcrVersions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == project.PcrVersionId.Value,
            cancellationToken);
        if (pcr is null)
        {
            return
            [
                new(
                    "PCR-NOT-FOUND",
                    "InventoryProjectVersion",
                    project.Id.ToString(),
                    "盤查綁定的 PCR 規則版本不存在。")
            ];
        }

        var stageRules = await _dbContext.PcrStageRules.AsNoTracking()
            .Where(item => item.PcrVersionId == pcr.Id)
            .OrderBy(item => item.LifecycleStage)
            .ToArrayAsync(cancellationToken);
        var categoryCode = await (
                from productVersion in _dbContext.ProductVersions.AsNoTracking()
                join product in _dbContext.Products.AsNoTracking() on productVersion.ProductId equals product.Id
                where productVersion.Id == project.ProductVersionId
                select product.CategoryCode)
            .SingleAsync(cancellationToken);
        var declarations = await _dbContext.LifecycleStageDeclarations.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == project.Id)
            .ToDictionaryAsync(
                item => (LifecycleStage)item.LifecycleStage,
                item => item.IsApplicable,
                cancellationToken);
        var activities = await _dbContext.ActivityData.AsNoTracking()
            .Where(item => item.InventoryProjectVersionId == project.Id)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var activityContexts = activities.Select(activity => new PcrActivityContext(
            activity.Id,
            (LifecycleStage)activity.LifecycleStage,
            Enum.TryParse<ActivityDataKind>(activity.ActivityKind, out var kind) ? kind : (ActivityDataKind)0,
            GetPopulatedActivityFields(activity))).ToArray();

        return PcrRuleEngine.Validate(
            ToPcrRuleSet(pcr, stageRules),
            new PcrProjectContext(
                project.Id,
                categoryCode,
                project.PeriodEnd,
                project.FunctionalUnit,
                project.DeclaredUnit,
                project.SystemBoundary,
                project.AllocationMethod,
                declarations,
                activityContexts,
                project.Exclusions),
            requireCompleteInventory);
    }

    private static PcrRuleSetVersion ToPcrRuleSet(
        PcrVersionRecord record,
        IEnumerable<PcrStageRuleRecord> stageRules)
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
            SplitCsv(record.PermittedAllocationMethodsCsv)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
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
                    SplitCsv(rule.PermittedActivityKindsCsv)
                        .Select(value => Enum.Parse<ActivityDataKind>(value))
                        .ToHashSet(),
                    SplitCsv(rule.RequiredFieldsCsv).ToHashSet(StringComparer.Ordinal)))
                .OrderBy(rule => rule.Stage)
                .ToArray());
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

            var kindValues = SplitCsv(input.Kinds);
            if (kindValues.Count == 0
                || kindValues.Any(value => !Enum.TryParse<ActivityDataKind>(value, true, out _)))
            {
                rules = [];
                error = $"{LifecycleStageDisplayName(input.Stage)}的活動類型清單無效。";
                return false;
            }

            var requiredFields = SplitCsv(input.RequiredFields);
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

    private static IReadOnlySet<string> GetPopulatedActivityFields(ActivityDataRecord activity)
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
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields.Add(field);
            }
        }
    }

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                    [',', ';'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string NormalizeCsv(string? value) => string.Join(",", SplitCsv(value));

    private static bool IsHttpSourceUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private void AddAudit(string action, string resourceType, Guid resourceId)
    {
        var organizationId = RequireOrganization();
        _dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = _userManager.GetUserId(User) is { } value && Guid.TryParse(value, out var id) ? id : null,
            OrganizationId = organizationId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BeforeHash = null,
            AfterHash = null,
            CorrelationId = HttpContext.TraceIdentifier,
            MetadataJson = "{}"
        });
    }
}
