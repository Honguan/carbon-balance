using System.Security.Claims;
using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

/// <summary>Inventory creation guards, stage applicability rules and export guards run without a Razor PageModel.
/// PCR compatibility of new inventories is covered end to end by <see cref="PcrSelectionTests"/>.</summary>
public sealed class InventoryProjectServiceTests
{
    [Fact]
    public async Task Create_RejectsDeniedCallersAndIncompleteInput()
    {
        var fixture = await SeedAsync("Draft");
        await using var db = Db(fixture.OrganizationId);
        var input = new InventoryInput(fixture.ProductVersionId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "1 piece", "piece",
            "cradle-to-grave", "mass", "by mass", null, null, null, fixture.PcrId);

        Assert.Equal(OperationStatus.Forbidden, (await Service(db, fixture, allowed: false).CreateAsync(Context(), input, default)).Status);
        var service = Service(db, fixture, allowed: true);
        Assert.Equal("請提供有效期間、功能單位與 PCR 版本識別。",
            (await service.CreateAsync(Context(), input with { PeriodStart = new DateOnly(2027, 1, 1) }, default)).Message);
        Assert.Equal("請先建立並選擇產品版本與已發布 PCR 版本。",
            (await service.CreateAsync(Context(), input with { PcrVersionId = Guid.Empty }, default)).Message);
        Assert.Equal("productVersionId", (await service.CreateAsync(Context(), input with { ProductVersionId = Guid.NewGuid() }, default)).Key);

        await using var verify = Db(fixture.OrganizationId);
        Assert.Equal(1, await verify.InventoryProjectVersions.CountAsync());
        Assert.False(await verify.AuditEvents.AnyAsync());
    }

    [Fact]
    public async Task StageApplicability_EnforcesPcrReasonAndActivityRules()
    {
        var fixture = await SeedAsync("Draft", withActivityOnManufacturing: true);
        await using var db = Db(fixture.OrganizationId);
        var service = Service(db, fixture, allowed: true);

        var mandatory = await service.SetStageApplicabilityAsync(Context(), fixture.RawMaterialStageId, false, "skip", Version(fixture, fixture.RawMaterialStageId), default);
        Assert.Equal("階段適用性不可違反已選 PCR 的必要或禁止規則。", mandatory.Message);
        var noReason = await service.SetStageApplicabilityAsync(Context(), fixture.UseStageId, false, " ", Version(fixture, fixture.UseStageId), default);
        Assert.Equal("不適用階段必須填寫原因。", noReason.Message);
        var withActivity = await service.SetStageApplicabilityAsync(Context(), fixture.ManufacturingStageId, false, "n/a", Version(fixture, fixture.ManufacturingStageId), default);
        Assert.Equal("已有活動數據的階段不可標記為不適用。", withActivity.Message);
        Assert.Equal(OperationStatus.Conflict,
            (await service.SetStageApplicabilityAsync(Context(), fixture.UseStageId, false, "n/a", Version(fixture, fixture.UseStageId) + 1, default)).Status);

        var changed = await service.SetStageApplicabilityAsync(Context(), fixture.UseStageId, false, " not used ", Version(fixture, fixture.UseStageId), default);
        Assert.True(changed.Succeeded);
        Assert.Equal(fixture.ProjectId, changed.ProjectVersionId);

        await using var verify = Db(fixture.OrganizationId);
        var use = await verify.LifecycleStageDeclarations.AsNoTracking().SingleAsync(item => item.Id == fixture.UseStageId);
        Assert.Equal((false, "not used"), (use.IsApplicable, use.Reason));
        Assert.Equal(["concurrency.rejected", "inventory.stage.applicability.changed"],
            (await verify.AuditEvents.AsNoTracking().Select(item => item.Action).ToArrayAsync()).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task StageApplicability_IsLockedAfterSubmission()
    {
        var fixture = await SeedAsync("Submitted");
        await using var db = Db(fixture.OrganizationId);

        var result = await Service(db, fixture, allowed: true)
            .SetStageApplicabilityAsync(Context(), fixture.UseStageId, false, "n/a", Version(fixture, fixture.UseStageId), default);

        Assert.Equal(OperationStatus.BadRequest, result.Status);
    }

    [Fact]
    public async Task Export_RequiresPermissionAndAnExistingRun()
    {
        var fixture = await SeedAsync("Draft");
        await using var db = Db(fixture.OrganizationId);

        // Guards return before the snapshot is read.
        Assert.Equal(OperationStatus.Forbidden, (await new InventoryExportService(db, new Authorizer(false), null!, null!)
            .ExportExcelAsync(Context(), fixture.ProjectId, null, default)).Status);
        var export = new InventoryExportService(db, new Authorizer(true), null!, null!);
        Assert.Equal(OperationStatus.NotFound, (await export.ExportExcelAsync(Context(), Guid.NewGuid(), null, default)).Status);
        Assert.Equal(OperationStatus.NotFound, (await export.ExportExcelAsync(Context(), fixture.ProjectId, Guid.NewGuid(), default)).Status);
    }

    private sealed record Fixture(Guid OrganizationId, Guid ProductVersionId, Guid PcrId, Guid ProjectId,
        Guid RawMaterialStageId, Guid ManufacturingStageId, Guid UseStageId);

    private static InventoryProjectService Service(CarbonFootprintDbContext db, Fixture fixture, bool allowed) =>
        new(db, new Authorizer(allowed), new Scope(fixture.OrganizationId));

    private static uint Version(Fixture fixture, Guid declarationId)
    {
        using var db = Db(fixture.OrganizationId);
        return db.LifecycleStageDeclarations.AsNoTracking().Single(item => item.Id == declarationId).RowVersion;
    }

    private static OperationContext Context() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), Guid.NewGuid(), "inv-" + Guid.NewGuid().ToString("N"), null);

    private static async Task<Fixture> SeedAsync(string status, bool withActivityOnManufacturing = false)
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var pcrId = Guid.NewGuid();
        var stageIds = Enum.GetValues<LifecycleStage>().ToDictionary(stage => stage, _ => Guid.NewGuid());
        await using var seed = Db(organizationId);
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Inventory fixture" });
        seed.Products.Add(new ProductRecord { Id = productId, OrganizationId = organizationId, Name = "Inventory", CategoryCode = "ELECTRONICS-1" });
        seed.ProductVersions.Add(new ProductVersionRecord
        {
            Id = productVersionId,
            OrganizationId = organizationId,
            ProductId = productId,
            VersionNumber = 1,
            NameZhTw = "Inventory"
        });
        seed.PcrVersions.Add(new PcrVersionRecord
        {
            Id = pcrId,
            OrganizationId = organizationId,
            RuleSetId = Guid.NewGuid(),
            RegistrationNumber = $"PCR-{pcrId:N}",
            VersionNumber = 1,
            Title = "Inventory PCR",
            PublicationStatus = "Draft",
            SourceReference = "https://example.test/pcr"
        });
        seed.PcrStageRules.Add(new PcrStageRuleRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PcrVersionId = pcrId,
            LifecycleStage = (int)LifecycleStage.RawMaterial,
            Requirement = "Mandatory"
        });
        seed.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId,
            OrganizationId = organizationId,
            ProductVersionId = productVersionId,
            VersionNumber = 1,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            FunctionalUnit = "1 piece",
            PcrVersion = "none",
            WorkflowStatus = status,
            PcrVersionId = pcrId
        });
        seed.LifecycleStageDeclarations.AddRange(stageIds.Select(pair => new LifecycleStageDeclarationRecord
        {
            Id = pair.Value,
            OrganizationId = organizationId,
            InventoryProjectVersionId = projectId,
            LifecycleStage = (int)pair.Key,
            IsApplicable = true
        }));
        if (withActivityOnManufacturing)
        {
            var factorId = Guid.NewGuid();
            seed.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
            {
                Id = factorId,
                OrganizationId = organizationId,
                FactorId = Guid.NewGuid(),
                VersionNumber = 1,
                Name = "Energy",
                Value = 1m,
                NumeratorUnitCode = "kgCO2e",
                DenominatorUnitCode = "kWh",
                Geography = "TW",
                ValidFrom = new DateOnly(2026, 1, 1),
                PublicationStatus = "Published",
                SourceDatasetVersion = "v1",
                LicenseCode = "CC0",
                SourceType = "government-database",
                SourceName = "MOENV",
                SourceReference = "https://example.test/factor",
                DatasetName = "dataset",
                OriginalDocumentName = "factor.csv",
                OriginalDocumentSha256 = new string('a', 64),
                Applicability = "global",
                ReviewStatus = "Approved"
            });
            seed.ActivityData.Add(new ActivityDataRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                InventoryProjectVersionId = projectId,
                LifecycleStage = (int)LifecycleStage.Manufacturing,
                Name = "Electricity",
                ActivityKind = "Energy",
                RawValue = 1m,
                RawUnitCode = "kWh",
                CanonicalValue = 1m,
                CanonicalUnitCode = "kWh",
                AmountFormulaId = CarbonFootprint.Domain.Modules.Calculations.ActivityAmountFormula.DirectFormulaId,
                FormulaInputsJson = "{}",
                ConversionRuleVersion = "units-p0-v2",
                PeriodStart = new DateOnly(2026, 1, 1),
                PeriodEnd = new DateOnly(2026, 12, 31),
                FactorVersionId = factorId,
                AllocationFactor = 1m,
                DataQuality = "measured"
            });
        }
        await seed.SaveChangesAsync();
        return new Fixture(organizationId, productVersionId, pcrId, projectId,
            stageIds[LifecycleStage.RawMaterial], stageIds[LifecycleStage.Manufacturing], stageIds[LifecycleStage.Use]);
    }

    private static DbContextOptions<CarbonFootprintDbContext> Options() => new DbContextOptionsBuilder<CarbonFootprintDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION") ?? throw new InvalidOperationException("Isolated database required"))
        .UseSnakeCaseNamingConvention().Options;
    private static CarbonFootprintDbContext Db(Guid organizationId) => new(Options(), new Scope(organizationId));
    private sealed record Scope(Guid Id) : IOrganizationScope { public Guid? OrganizationId => Id; }
    private sealed class Authorizer(bool allowed) : IOperationAuthorizer
    {
        public Task<bool> IsAllowedAsync(OperationContext context, OrganizationPermission permission, bool requireMfa) => Task.FromResult(allowed);
    }
}
