using CarbonFootprint.Application.Calculations;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Units;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
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

    public WorkspaceModel(
        CarbonFootprintDbContext dbContext,
        IOrganizationScope organizationScope,
        OrganizationOnboardingService onboardingService,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        CalculateInventoryHandler calculateHandler)
    {
        _dbContext = dbContext;
        _organizationScope = organizationScope;
        _onboardingService = onboardingService;
        _userManager = userManager;
        _signInManager = signInManager;
        _calculateHandler = calculateHandler;
    }

    public Guid? OrganizationId => _organizationScope.OrganizationId;

    public IReadOnlyList<ProductVersionRecord> ProductVersions { get; private set; } = [];

    public IReadOnlyList<InventoryProjectVersionRecord> InventoryProjects { get; private set; } = [];

    public IReadOnlyList<EmissionFactorVersionRecord> Factors { get; private set; } = [];

    public IReadOnlyList<ActivityDataRecord> Activities { get; private set; } = [];

    public IReadOnlyList<UnitRecord> Units { get; private set; } = [];

    public IReadOnlyList<CalculationRunRecord> Runs { get; private set; } = [];

    public IReadOnlyList<CalculationLineRecord> LatestLines { get; private set; } = [];

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
        string pcrVersion,
        CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganization();
        if (periodStart > periodEnd || string.IsNullOrWhiteSpace(functionalUnit) || string.IsNullOrWhiteSpace(pcrVersion))
        {
            ModelState.AddModelError("inventory", "請提供有效期間、功能單位與 PCR 版本識別。");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!await _dbContext.ProductVersions.AnyAsync(item => item.Id == productVersionId, cancellationToken))
        {
            return NotFound();
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
            PcrVersion = pcrVersion.Trim(),
            WorkflowStatus = "Draft",
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddAudit("inventory.version.created", "InventoryProjectVersion", projectId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "盤查專案第 1 版已建立。";
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
            PublicationStatus = "Published",
            SourceDatasetVersion = sourceDatasetVersion.Trim(),
            LicenseCode = licenseCode.Trim()
        });
        AddAudit("factor.version.published", "EmissionFactorVersion", factorVersionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        StatusMessage = "係數版本已建立；此 Golden Slice 資料仍須領域與授權審核。";
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

    public async Task<IActionResult> OnPostCalculateAsync(Guid inventoryProjectVersionId, CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganization();
        var project = await _dbContext.InventoryProjectVersions.SingleOrDefaultAsync(
            item => item.Id == inventoryProjectVersionId,
            cancellationToken);
        if (project is null)
        {
            return NotFound();
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
            await _calculateHandler.HandleAsync(
                new CalculateInventoryCommand(Guid.NewGuid(), snapshot, engineBuild, null),
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
        InventoryProjects = await _dbContext.InventoryProjectVersions.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        Factors = await _dbContext.EmissionFactorVersions.AsNoTracking().OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        Activities = await _dbContext.ActivityData.AsNoTracking().OrderBy(item => item.LifecycleStage).ThenBy(item => item.Name).ToArrayAsync(cancellationToken);
        Runs = await _dbContext.CalculationRuns.AsNoTracking().OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        if (Runs.Count > 0)
        {
            LatestLines = await _dbContext.CalculationLineItems.AsNoTracking()
                .Where(item => item.CalculationRunId == Runs[0].Id)
                .OrderBy(item => item.LifecycleStage)
                .ThenBy(item => item.ActivityId)
                .ToArrayAsync(cancellationToken);
        }
    }

    private Guid RequireOrganization() => OrganizationId
        ?? throw new InvalidOperationException("請先建立組織。");

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
