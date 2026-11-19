using System.Security.Claims;
using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

/// <summary>Activity and evidence operations run without a Razor PageModel.</summary>
public sealed class ActivityDataServiceTests
{
    [Fact]
    public async Task Save_CreatesAuditedVersion_AndCorrectionRetiresThePrevious()
    {
        var fixture = await SeedAsync("Draft");
        Guid firstId;
        await using (var db = Db(fixture.OrganizationId))
        {
            var created = await Service(db, fixture.OrganizationId, allowed: true).SaveAsync(Context(), Input(fixture, 2m), null, default);
            Assert.True(created.Succeeded, created.Message);
            Assert.Equal(fixture.ProjectId, created.ProjectVersionId);
            firstId = (await db.ActivityData.AsNoTracking().SingleAsync(item => item.Name == "Steel")).Id;
        }
        await using (var db = Db(fixture.OrganizationId))
        {
            var corrected = await Service(db, fixture.OrganizationId, allowed: true).SaveAsync(Context(), Input(fixture, 3m), firstId, default);
            Assert.True(corrected.Succeeded, corrected.Message);
        }

        await using var verify = Db(fixture.OrganizationId);
        var versions = await verify.ActivityData.AsNoTracking().Where(item => item.Name == "Steel").ToArrayAsync();
        Assert.NotNull(Assert.Single(versions, item => item.Id == firstId).RetiredAt);
        Assert.Equal(3m, Assert.Single(versions, item => item.RetiredAt == null).RawValue);
        Assert.Equal(2, await verify.AuditEvents.CountAsync(item => item.Action == "activity.version.created"));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(item => item.Action == "activity.version.superseded"));
    }

    [Fact]
    public async Task Save_RejectsDeniedCallers_LockedInventories_AndNotApplicableStages()
    {
        var draft = await SeedAsync("Draft");
        var submitted = await SeedAsync("Submitted");
        await using var db = Db(draft.OrganizationId);
        Assert.Equal(OperationStatus.Forbidden,
            (await Service(db, draft.OrganizationId, allowed: false).SaveAsync(Context(), Input(draft, 1m), null, default)).Status);
        var notApplicable = await Service(db, draft.OrganizationId, allowed: true)
            .SaveAsync(Context(), Input(draft, 1m) with { LifecycleStage = LifecycleStage.Distribution }, null, default);
        Assert.Equal("不適用階段不可新增活動數據。", notApplicable.Message);

        await using var lockedDb = Db(submitted.OrganizationId);
        var locked = await Service(lockedDb, submitted.OrganizationId, allowed: true).SaveAsync(Context(), Input(submitted, 1m), null, default);
        Assert.Equal(OperationStatus.Invalid, locked.Status);
        Assert.Equal("盤查已送審或核准，不可再新增活動資料。", locked.Message);

        await using var verify = Db(draft.OrganizationId);
        Assert.False(await verify.AuditEvents.AnyAsync(item => item.Action.StartsWith("activity.")));
    }

    [Fact]
    public async Task RetireAndEvidence_RespectWorkflowAndRequireAFile()
    {
        var draft = await SeedAsync("Draft", withActivity: true);
        var approved = await SeedAsync("Approved", withActivity: true);

        await using (var db = Db(approved.OrganizationId))
        {
            var service = Service(db, approved.OrganizationId, allowed: true);
            Assert.Equal(OperationStatus.BadRequest, (await service.RetireAsync(Context(), approved.ActivityId, default)).Status);
            Assert.Equal("盤查已送審或核准，不可再變更佐證檔案。",
                (await service.AttachEvidenceAsync(Context(), approved.ActivityId, new EvidenceUpload(null, 0, "", ""), default)).Message);
        }
        await using (var db = Db(draft.OrganizationId))
        {
            var service = Service(db, draft.OrganizationId, allowed: true);
            Assert.Equal("請選擇非空白佐證檔案。",
                (await service.AttachEvidenceAsync(Context(), draft.ActivityId, new EvidenceUpload(null, 0, "", ""), default)).Message);
            var retired = await service.RetireAsync(Context(), draft.ActivityId, default);
            Assert.True(retired.Succeeded);
            Assert.Equal(draft.ProjectId, retired.ProjectVersionId);
            Assert.Equal(OperationStatus.NotFound, (await service.RetireAsync(Context(), draft.ActivityId, default)).Status);
        }

        await using var verify = Db(draft.OrganizationId);
        Assert.Equal("activity.retired", (await verify.AuditEvents.AsNoTracking().SingleAsync()).Action);
    }

    private sealed record Fixture(Guid OrganizationId, Guid ProjectId, Guid FactorId, Guid ActivityId);

    private static ActivityInput Input(Fixture fixture, decimal value) => new(
        fixture.ProjectId, LifecycleStage.RawMaterial, ActivityDataKind.Material, "Steel", null, "supplier", null, null,
        "measured", null, "supplier", null, "meter", null, "invoice-1", value, null, null, null, null, null,
        "kg", "kg", fixture.FactorId, 1m, false, null, "primary");

    // Evidence storage is not reached by these cases.
    private static ActivityDataService Service(CarbonFootprintDbContext db, Guid organizationId, bool allowed) =>
        new(db, new Authorizer(allowed), new Scope(organizationId), null!);

    private static OperationContext Context() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), Guid.NewGuid(), "act-" + Guid.NewGuid().ToString("N"), null);

    private static async Task<Fixture> SeedAsync(string status, bool withActivity = false)
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var factorId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        await using var seed = Db(organizationId);
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Activity fixture" });
        seed.Products.Add(new ProductRecord { Id = productId, OrganizationId = organizationId, Name = "Activity" });
        seed.ProductVersions.Add(new ProductVersionRecord
        {
            Id = productVersionId,
            OrganizationId = organizationId,
            ProductId = productId,
            VersionNumber = 1,
            NameZhTw = "Activity"
        });
        seed.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId,
            OrganizationId = organizationId,
            ProductVersionId = productVersionId,
            VersionNumber = 1,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            FunctionalUnit = "1 kg product",
            PcrVersion = "none",
            WorkflowStatus = status
        });
        seed.LifecycleStageDeclarations.AddRange(
            new LifecycleStageDeclarationRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                InventoryProjectVersionId = projectId,
                LifecycleStage = (int)LifecycleStage.RawMaterial,
                IsApplicable = true
            },
            new LifecycleStageDeclarationRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                InventoryProjectVersionId = projectId,
                LifecycleStage = (int)LifecycleStage.Distribution,
                IsApplicable = false,
                Reason = "not shipped"
            });
        seed.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
        {
            Id = factorId,
            OrganizationId = organizationId,
            FactorId = Guid.NewGuid(),
            VersionNumber = 1,
            Name = "Steel factor",
            Value = 2m,
            NumeratorUnitCode = "kgCO2e",
            DenominatorUnitCode = "kg",
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
        if (withActivity)
        {
            seed.ActivityData.Add(new ActivityDataRecord
            {
                Id = activityId,
                OrganizationId = organizationId,
                InventoryProjectVersionId = projectId,
                LifecycleStage = (int)LifecycleStage.RawMaterial,
                Name = "Seeded",
                ActivityKind = "Material",
                RawValue = 1m,
                RawUnitCode = "kg",
                CanonicalValue = 1m,
                CanonicalUnitCode = "kg",
                AmountFormulaId = ActivityAmountFormula.DirectFormulaId,
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
        return new Fixture(organizationId, projectId, factorId, activityId);
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
