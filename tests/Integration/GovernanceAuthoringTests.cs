using System.Security.Claims;
using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Inventories;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Inventories;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

/// <summary>PCR and factor drafts are created through governance operations, not Razor handlers.</summary>
public sealed class GovernanceAuthoringTests
{
    [Fact]
    public async Task Factor_CreateValidatesControlledValues_AndAuditsTheDraft()
    {
        var organizationId = await SeedOrganizationAsync();
        await using var db = Db(organizationId);
        var service = new FactorGovernanceService(db, new Authorizer(true), new Scope(organizationId));
        var input = new FactorInput("Grid electricity", 0.5m, "kWh", "__other__", " utility report ", "TW", null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "2026", "CC0", "Utility", "https://example.test/f", "dataset", "f.pdf", "TW grid");

        Assert.Equal(OperationStatus.Forbidden,
            (await new FactorGovernanceService(db, new Authorizer(false), new Scope(organizationId)).CreateAsync(Context(), input, default)).Status);
        Assert.Equal("來源類型、地域、有效期間與原始文件皆為必填。", (await service.CreateAsync(Context(), input with { SourceTypeOther = " " }, default)).Message);
        Assert.Equal("係數分母必須使用受控單位。", (await service.CreateAsync(Context(), input with { DenominatorUnitCode = "furlong" }, default)).Message);
        Assert.True((await service.CreateAsync(Context(), input, default)).Succeeded);

        await using var verify = Db(organizationId);
        var factor = await verify.EmissionFactorVersions.AsNoTracking().SingleAsync();
        Assert.Equal(("utility report", "Draft", "Pending", 1), (factor.SourceType, factor.PublicationStatus, factor.ReviewStatus, factor.VersionNumber));
        Assert.Equal("factor.version.created", (await verify.AuditEvents.AsNoTracking().SingleAsync()).Action);
    }

    [Fact]
    public async Task Factor_SupersedeCreatesNextDraft_OnlyFromPublished_AndRejectsStaleVersions()
    {
        var organizationId = await SeedOrganizationAsync();
        var draftId = await SeedFactorAsync(organizationId, "Draft");
        var publishedId = await SeedFactorAsync(organizationId, "Published");
        await using var db = Db(organizationId);
        var service = new FactorGovernanceService(db, new Authorizer(true), new Scope(organizationId));
        var input = new FactorSupersedeInput(0.42m, "2027", "https://example.test/new", "new.pdf", "TW grid");

        Assert.Equal(OperationStatus.Invalid, (await service.SupersedeAsync(Context(), draftId, input, Version(organizationId, draftId), default)).Status);
        Assert.Equal(OperationStatus.Conflict, (await service.SupersedeAsync(Context(), publishedId, input, Version(organizationId, publishedId) + 1, default)).Status);
        Assert.True((await service.SupersedeAsync(Context(), publishedId, input, Version(organizationId, publishedId), default)).Succeeded);

        await using var verify = Db(organizationId);
        var successor = await verify.EmissionFactorVersions.AsNoTracking().SingleAsync(item => item.SupersedesVersionId == publishedId);
        Assert.Equal((2, 0.42m, "Draft"), (successor.VersionNumber, successor.Value, successor.PublicationStatus));
        Assert.Equal("Published", (await verify.EmissionFactorVersions.AsNoTracking().SingleAsync(item => item.Id == publishedId)).PublicationStatus);
        Assert.Equal(["concurrency.rejected", "factor.version.superseded"],
            (await verify.AuditEvents.AsNoTracking().Select(item => item.Action).ToArrayAsync()).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Pcr_CreateRejectsIncompleteInput_BeforeStoringTheDocument()
    {
        var organizationId = await SeedOrganizationAsync();
        await using var db = Db(organizationId);
        // Every case is rejected before the document store is reached, so none is configured.
        var service = new PcrGovernanceService(db, new Authorizer(true), new Scope(organizationId), null!);
        using var content = new MemoryStream([1, 2, 3]);
        var document = new EvidenceUpload(content, content.Length, "pcr.pdf", "application/pdf");
        var stages = Enum.GetValues<LifecycleStage>().Select(stage => new PcrStageInput(stage, "Optional", "Material", "")).ToArray();
        var input = new PcrInput("PCR-1", 1, "Mouse", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31),
            "https://example.test/pcr", "ISO 14067", "CCC", "mice", "rules", "ELECTRONICS-*", "1 piece*", "piece", "cradle-to-grave",
            "mass", 1m, "pcr-formulas-v1", 6, "report", false, null, stages);

        Assert.Equal(OperationStatus.Forbidden,
            (await new PcrGovernanceService(db, new Authorizer(false), new Scope(organizationId), null!).CreateAsync(Context(), input, document, default)).Status);
        Assert.Equal("PCR 識別、適用條件、計算規則、原始文件與有效期間必須完整且有效。",
            (await service.CreateAsync(Context(), input, document with { Content = null, Length = 0 }, default)).Message);
        Assert.Equal("PCR 識別、適用條件、計算規則、原始文件與有效期間必須完整且有效。",
            (await service.CreateAsync(Context(), input with { SourceReference = "ftp://example.test/pcr" }, document, default)).Message);
        Assert.Equal("PCR 標示單位必須使用目前受控單位目錄中的代碼。",
            (await service.CreateAsync(Context(), input with { DeclaredUnitCode = "furlong" }, document, default)).Message);
        Assert.Equal("PCR 公式規則版本目前僅支援 pcr-formulas-v1。",
            (await service.CreateAsync(Context(), input with { FormulaRuleSetVersion = "v2" }, document, default)).Message);
        var badStage = await service.CreateAsync(Context(),
            input with { Stages = [.. stages.Skip(1), new PcrStageInput(LifecycleStage.RawMaterial, "Sometimes", "Material", "")] }, document, default);
        Assert.Equal(("pcr", "原料取得階段的必要性設定無效。"), (badStage.Key, badStage.Message));

        await using var verify = Db(organizationId);
        Assert.False(await verify.PcrVersions.AnyAsync());
        Assert.False(await verify.AuditEvents.AnyAsync());
    }

    private static uint Version(Guid organizationId, Guid factorId)
    {
        using var db = Db(organizationId);
        return db.EmissionFactorVersions.AsNoTracking().Single(item => item.Id == factorId).RowVersion;
    }

    private static async Task<Guid> SeedOrganizationAsync()
    {
        var organizationId = Guid.NewGuid();
        await using var seed = Db(organizationId);
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Authoring fixture" });
        await seed.SaveChangesAsync();
        return organizationId;
    }

    private static async Task<Guid> SeedFactorAsync(Guid organizationId, string status)
    {
        var id = Guid.NewGuid();
        await using var seed = Db(organizationId);
        seed.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
        {
            Id = id, OrganizationId = organizationId, FactorId = Guid.NewGuid(), VersionNumber = 1, Name = "Grid", Value = 0.5m,
            NumeratorUnitCode = "kgCO2e", DenominatorUnitCode = "kWh", Geography = "TW", ValidFrom = new DateOnly(2026, 1, 1),
            PublicationStatus = status, SourceDatasetVersion = "v1", LicenseCode = "CC0", SourceType = "government-database",
            SourceName = "MOENV", SourceReference = "https://example.test/factor", DatasetName = "dataset",
            OriginalDocumentName = "factor.csv", OriginalDocumentSha256 = new string('a', 64), Applicability = "global", ReviewStatus = "Approved"
        });
        await seed.SaveChangesAsync();
        return id;
    }

    private static OperationContext Context() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), Guid.NewGuid(), "gov-" + Guid.NewGuid().ToString("N"), null);

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
