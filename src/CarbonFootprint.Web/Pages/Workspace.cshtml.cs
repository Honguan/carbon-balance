using CarbonFootprint.Application.Calculations;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Web.Pages;

[Authorize]
public sealed class WorkspaceModel : PageModel
{
    private readonly CarbonFootprintDbContext _dbContext;
    private readonly IOrganizationScope _organizationScope;
    private readonly OrganizationOnboardingService _onboardingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly CalculateInventoryHandler _calculateHandler;
    private readonly IAuthorizationService _authorizationService;
    private readonly EvidenceStorageService _evidenceStorageService;

    public WorkspaceModel(
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        OrganizationOnboardingService onboardingService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CalculateInventoryHandler calculateHandler,
        IAuthorizationService authorizationService,
        EvidenceStorageService evidenceStorageService)
    {
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _onboardingService = onboardingService;
        _userManager = userManager;
        _signInManager = signInManager;
        _calculateHandler = calculateHandler;
        _authorizationService = authorizationService;
        _evidenceStorageService = evidenceStorageService;
    }

    public Guid? OrganizationId => _organizationScope.OrganizationId;

    public IReadOnlyList<ProductVersionRecord> ProductVersions { get; private set; } = [];

    public IReadOnlyList<InventoryProjectVersionRecord> InventoryProjects { get; private set; } = [];

    public IReadOnlyList<EmissionFactorVersionRecord> Factors { get; private set; } = [];

    public IReadOnlyList<PcrVersionRecord> PcrVersions { get; private set; } = [];

    public IReadOnlyList<ActivityDataRecord> Activities { get; private set; } = [];

    public IReadOnlyList<EvidenceFileRecord> EvidenceFiles { get; private set; } = [];

    public IReadOnlyList<UnitRecord> Units { get; private set; } = [];

    public IReadOnlyList<CalculationRunRecord> Runs { get; private set; } = [];

    public IReadOnlyList<CalculationLineRecord> LatestLines { get; private set; } = [];

    public IReadOnlyList<CalculationWarningRecord> LatestWarnings { get; private set; } = [];

    public CalculationRunDifference? LatestDifference { get; private set; }

    public bool? LatestManifestHashValid { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateOrganizationAsync(string organizationName, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User)
            ?? throw new InvalidOperationException("找不到目前使用者。");
        try
        {
            await _onboardingService.CreateAsync(user, organizationName, cancellationToken);
            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "組織已建立。";
            return RedirectToPage();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError("organizationName", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCreateProductAsync(string productName, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        if (string.IsNullOrWhiteSpace(productName))
        {
            ModelState.AddModelError("productName", "產品名稱不可為空。");
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
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateInventoryAsync(
        Guid productVersionId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string functionalUnit,
        Guid pcrVersionId,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        if (periodStart > periodEnd || string.IsNullOrWhiteSpace(functionalUnit))
        {
            ModelState.AddModelError("inventory", "請提供有效期間、功能單位與 PCR 版本識別。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!await _dbContext.ProductVersions.AnyAsync(item => item.Id == productVersionId, cancellationToken))
        {
            return NotFound();
        }

        var pcr = await _dbContext.PcrVersions.SingleOrDefaultAsync(item => item.Id == pcrVersionId, cancellationToken);
        if (pcr is null)
        {
            return NotFound();
        }

        var pcrReference = ToPcrReference(pcr);
        if (!pcrReference.IsAvailableOn(periodEnd))
        {
            ModelState.AddModelError("inventory", "PCR 版本未發布、已撤回或不在盤查期間有效範圍。");
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
            PcrVersionId = pcr.Id,
            PcrVersion = $"{pcr.RegistrationNumber}-v{pcr.VersionNumber}",
            WorkflowStatus = InventoryWorkflowStatus.Draft.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddAudit("inventory.version.created", "InventoryProjectVersion", projectId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "盤查專案第 1 版已建立。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreatePcrAsync(
        string registrationNumber,
        int versionNumber,
        string title,
        DateOnly? validFrom,
        DateOnly? validTo,
        string sourceReference,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(registrationNumber)
            || versionNumber < 1
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(sourceReference)
            || validFrom > validTo)
        {
            ModelState.AddModelError("pcr", "PCR 登錄編號、正整數版本、名稱、來源與有效期間必須有效。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var pcrVersionId = Guid.NewGuid();
        _dbContext.PcrVersions.Add(new PcrVersionRecord
        {
            Id = pcrVersionId,
            OrganizationId = RequireOrganization(),
            RegistrationNumber = registrationNumber.Trim(),
            VersionNumber = versionNumber,
            Title = title.Trim(),
            ValidFrom = validFrom,
            ValidTo = validTo,
            PublicationStatus = PcrPublicationStatus.Draft.ToString(),
            SourceReference = sourceReference.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddAudit("pcr.version.created", "PcrVersion", pcrVersionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "PCR 草稿已建立；發布後才可建立新盤查。";
        return RedirectToPage();
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

        if (!string.Equals(pcr.PublicationStatus, PcrPublicationStatus.Draft.ToString(), StringComparison.Ordinal))
        {
            ModelState.AddModelError("pcr", "只有草稿 PCR 版本可發布。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        pcr.PublicationStatus = PcrPublicationStatus.Published.ToString();
        pcr.PublishedAt = DateTimeOffset.UtcNow;
        AddAudit("pcr.version.published", "PcrVersion", pcr.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "PCR 版本已發布。";
        return RedirectToPage();
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
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateFactorAsync(
        string factorName,
        decimal? factorValue,
        string denominatorUnitCode,
        string sourceDatasetVersion,
        string licenseCode,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.ManageFactors))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        if (string.IsNullOrWhiteSpace(factorName)
            || factorValue is null or < 0m
            || string.IsNullOrWhiteSpace(sourceDatasetVersion)
            || string.IsNullOrWhiteSpace(licenseCode))
        {
            ModelState.AddModelError("factor", "係數名稱、非負數值、來源版本與授權識別皆為必填。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!await _dbContext.Units.AnyAsync(item => item.Code == denominatorUnitCode, cancellationToken))
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
            Geography = "TW",
            ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = new DateOnly(2027, 12, 31),
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = sourceDatasetVersion.Trim(),
            LicenseCode = licenseCode.Trim()
        });
        AddAudit("factor.version.created", "EmissionFactorVersion", factorVersionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "係數草稿已建立；發布後才可用於新計算。";
        return RedirectToPage();
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

        if (!string.Equals(factor.PublicationStatus, FactorPublicationStatus.Draft.ToString(), StringComparison.Ordinal))
        {
            ModelState.AddModelError("factor", "只有草稿係數版本可發布。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        factor.PublicationStatus = FactorPublicationStatus.Published.ToString();
        AddAudit("factor.version.published", "EmissionFactorVersion", factor.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "係數版本已發布。";
        return RedirectToPage();
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
        AddAudit("factor.version.withdrawn", "EmissionFactorVersion", factor.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "係數版本已撤回；歷史計算不受影響。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddActivityAsync(
        Guid inventoryProjectVersionId,
        LifecycleStage lifecycleStage,
        string activityName,
        decimal? rawValue,
        string rawUnitCode,
        string canonicalUnitCode,
        Guid factorVersionId,
        CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
        {
            return Forbid();
        }

        var organizationId = RequireOrganization();
        var project = await _dbContext.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == inventoryProjectVersionId,
            cancellationToken);
        var factor = await _dbContext.EmissionFactorVersions.SingleOrDefaultAsync(
            item => item.Id == factorVersionId,
            cancellationToken);
        if (project is null || factor is null)
        {
            return NotFound();
        }

        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus)))
        {
            ModelState.AddModelError("activity", "盤查已送審或核准，不可再新增活動資料。");
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
            factor.LicenseCode);
        if (!factorVersion.IsSelectableOn(project.PeriodEnd))
        {
            ModelState.AddModelError("activity", "係數版本未發布、已撤回或不在盤查期間有效範圍。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (rawValue is null or < 0m || string.IsNullOrWhiteSpace(activityName))
        {
            ModelState.AddModelError("activity", "活動名稱與非負活動量為必填。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var unitRecords = await _dbContext.Units
                .Where(item => item.Code == rawUnitCode || item.Code == canonicalUnitCode)
                .ToArrayAsync(cancellationToken);
            var catalogue = new UnitCatalogue(
                "units-p0-v1",
                unitRecords.Select(item => new UnitDefinition(
                    item.Id,
                    item.Code,
                    item.Dimension,
                    item.ScaleToCanonical,
                    item.OffsetToCanonical,
                    item.CanonicalCode,
                    item.CatalogueVersion)));
            var canonicalValue = catalogue.Convert(rawValue.Value, rawUnitCode, canonicalUnitCode);
            if (!string.Equals(canonicalUnitCode, factor.DenominatorUnitCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("活動 canonical 單位必須等於係數分母單位。");
            }

            var activityId = Guid.NewGuid();
            _dbContext.ActivityData.Add(new ActivityDataRecord
            {
                Id = activityId,
                OrganizationId = organizationId,
                InventoryProjectVersionId = project.Id,
                LifecycleStage = (int)lifecycleStage,
                Name = activityName.Trim(),
                RawValue = rawValue.Value,
                RawUnitCode = rawUnitCode,
                CanonicalValue = canonicalValue,
                CanonicalUnitCode = canonicalUnitCode,
                ConversionRuleVersion = "units-p0-v1",
                PeriodStart = project.PeriodStart,
                PeriodEnd = project.PeriodEnd,
                FactorVersionId = factor.Id,
                EvidenceSha256 = null
            });
            AddAudit("activity.version.created", "ActivityDataVersion", activityId);
            await _dbContext.SaveChangesAsync(cancellationToken);
            StatusMessage = "活動數據已保存。";
            return RedirectToPage();
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
            ModelState.AddModelError("evidence", "盤查已送審或核准，不可再變更 Evidence。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (evidenceFile is null || evidenceFile.Length <= 0)
        {
            ModelState.AddModelError("evidence", "請選擇非空白 Evidence 檔案。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (await _dbContext.EvidenceFiles.AnyAsync(item => item.ActivityDataId == activity.Id, cancellationToken))
        {
            ModelState.AddModelError("evidence", "P0 每筆活動僅允許一份 Evidence；請建立活動資料新版本以更換。");
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
            StatusMessage = "Evidence 已通過惡意程式掃描、寫入物件儲存並綁定活動。";
            return RedirectToPage();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ModelState.AddModelError("evidence", $"Evidence 未保存：{exception.Message}");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSubmitInventoryAsync(Guid inventoryProjectVersionId, CancellationToken cancellationToken)
    {
        if (!await IsAllowedAsync(OrganizationPermission.EditInventory))
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

        if (!await _dbContext.CalculationRuns.AnyAsync(item => item.ProjectVersionId == project.Id, cancellationToken))
        {
            ModelState.AddModelError("review", "盤查至少需要一個不可變計算 Run 才能送審。");
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
        return RedirectToPage();
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
        return RedirectToPage();
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

        var organizationId = RequireOrganization();
        var project = await _dbContext.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == inventoryProjectVersionId,
            cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!InventoryWorkflow.AllowsEditing(Enum.Parse<InventoryWorkflowStatus>(project.WorkflowStatus)))
        {
            ModelState.AddModelError("calculation", "盤查已送審或核准，不可建立新計算 Run。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!project.PcrVersionId.HasValue)
        {
            ModelState.AddModelError("calculation", "盤查版本未綁定受治理的 PCR 版本。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var pcr = await _dbContext.PcrVersions.SingleOrDefaultAsync(
            item => item.Id == project.PcrVersionId.Value,
            cancellationToken);
        if (pcr is null || !ToPcrReference(pcr).IsAvailableOn(project.PeriodEnd))
        {
            ModelState.AddModelError("calculation", "PCR 版本未發布、已撤回或不在盤查期間有效範圍。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var activities = await _dbContext.ActivityData
            .Where(item => item.InventoryProjectVersionId == project.Id)
            .OrderBy(item => item.LifecycleStage)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var factorIds = activities.Select(item => item.FactorVersionId).Distinct().ToArray();
        var factorRecords = await _dbContext.EmissionFactorVersions
            .Where(item => factorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        try
        {
            var snapshot = new InventoryProjectSnapshot(
                organizationId,
                project.Id,
                project.ProductVersionId,
                project.PeriodStart,
                project.PeriodEnd,
                project.FunctionalUnit,
                project.PcrVersion,
                "rules-p0-v1",
                "gwp-fixture-p0-v1",
                "units-p0-v1",
                Enum.GetValues<LifecycleStage>()
                    .Select(stage => new StageDeclaration(stage, true, null))
                    .ToArray(),
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
                            factor.LicenseCode),
                        activity.EvidenceSha256);
                }).ToArray());

            var engineBuild = typeof(WorkspaceModel).Assembly.GetName().Version?.ToString() ?? "dev";
            var supersedesRunId = await _dbContext.CalculationRuns
                .Where(item => item.ProjectVersionId == project.Id)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            await _calculateHandler.HandleAsync(
                new CalculateInventoryCommand(Guid.NewGuid(), snapshot, engineBuild, supersedesRunId),
                cancellationToken);
            StatusMessage = "不可變 CalculationRun 已建立。";
            return RedirectToPage();
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError("calculation", exception.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Units = await _dbContext.Units.AsNoTracking().OrderBy(item => item.Code).ToArrayAsync(cancellationToken);
        if (!OrganizationId.HasValue)
        {
            return;
        }

        ProductVersions = await _dbContext.ProductVersions.AsNoTracking().OrderBy(item => item.NameZhTw).ToArrayAsync(cancellationToken);
        PcrVersions = await _dbContext.PcrVersions.AsNoTracking().OrderBy(item => item.RegistrationNumber).ThenByDescending(item => item.VersionNumber).ToArrayAsync(cancellationToken);
        InventoryProjects = await _dbContext.InventoryProjectVersions.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        Factors = await _dbContext.EmissionFactorVersions.AsNoTracking().OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        Activities = await _dbContext.ActivityData.AsNoTracking().OrderBy(item => item.LifecycleStage).ThenBy(item => item.Name).ToArrayAsync(cancellationToken);
        EvidenceFiles = await _dbContext.EvidenceFiles.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        Runs = await _dbContext.CalculationRuns.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        if (Runs.Count > 0)
        {
            LatestManifestHashValid = CanonicalManifest.HasValidSha256(
                Runs[0].CanonicalInputManifest,
                Runs[0].InputSha256);
            LatestLines = await _dbContext.CalculationLineItems.AsNoTracking()
                .Where(item => item.CalculationRunId == Runs[0].Id)
                .OrderBy(item => item.LifecycleStage)
                .ThenBy(item => item.ActivityId)
                .ToArrayAsync(cancellationToken);
            LatestWarnings = await _dbContext.CalculationWarnings.AsNoTracking()
                .Where(item => item.CalculationRunId == Runs[0].Id)
                .OrderBy(item => item.Code)
                .ToArrayAsync(cancellationToken);
        }

        if (Runs.Count > 1)
        {
            var comparedRunIds = new[] { Runs[0].Id, Runs[1].Id };
            var summaries = await _dbContext.CalculationStageSummaries.AsNoTracking()
                .Where(item => comparedRunIds.Contains(item.CalculationRunId))
                .ToArrayAsync(cancellationToken);
            var baseline = new CalculationRunTotals(
                Runs[1].Id,
                Runs[1].ProductTotal,
                summaries.Where(item => item.CalculationRunId == Runs[1].Id)
                    .ToDictionary(item => (LifecycleStage)item.LifecycleStage, item => item.Emissions));
            var candidate = new CalculationRunTotals(
                Runs[0].Id,
                Runs[0].ProductTotal,
                summaries.Where(item => item.CalculationRunId == Runs[0].Id)
                    .ToDictionary(item => (LifecycleStage)item.LifecycleStage, item => item.Emissions));
            LatestDifference = CalculationRunDiff.Compare(baseline, candidate);
        }
    }

    private Guid RequireOrganization() => OrganizationId
        ?? throw new InvalidOperationException("請先建立組織。");

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

    private static PcrVersionReference ToPcrReference(PcrVersionRecord record) => new(
        record.Id,
        record.RegistrationNumber,
        record.VersionNumber,
        record.ValidFrom,
        record.ValidTo,
        Enum.Parse<PcrPublicationStatus>(record.PublicationStatus));

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
