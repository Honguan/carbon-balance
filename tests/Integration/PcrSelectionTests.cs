using System.Security.Claims;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Pages;
using CarbonFootprint.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarbonFootprint.Integration.Tests;

public sealed class PcrSelectionTests
{
    private static readonly DateOnly PeriodStart = new(2026, 1, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 12, 31);

    [Theory]
    [InlineData("valid", true, null)]
    [InlineData("expired", false, "PCR-STATUS")]
    [InlineData("withdrawn", false, "PCR-STATUS")]
    [InlineData("superseded", false, "PCR-STATUS")]
    [InlineData("draft", false, "PCR-STATUS")]
    [InlineData("wrong-product", false, "PCR-PRODUCT-SCOPE")]
    [InlineData("wrong-functional-unit", false, "PCR-FUNCTIONAL-UNIT")]
    [InlineData("wrong-declared-unit", false, "PCR-DECLARED-UNIT")]
    [InlineData("wrong-boundary", false, "PCR-SYSTEM-BOUNDARY")]
    public async Task CreateInventory_AcceptsOnlyAvailableAndCompatiblePcrVersions(string scenario, bool accepted, string? code)
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var pcrId = Guid.NewGuid();
        await using (var seed = Db(organizationId))
        {
            var productId = Guid.NewGuid();
            seed.Users.Add(new ApplicationUser { Id = actorId, UserName = $"pcr-{actorId:N}@example.test", Email = $"pcr-{actorId:N}@example.test" });
            seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "PCR selection fixture" });
            seed.Products.Add(new ProductRecord
            {
                Id = productId, OrganizationId = organizationId, Name = "PCR product",
                CategoryCode = scenario == "wrong-product" ? "FOOD-001" : "ELECTRONICS-001"
            });
            seed.ProductVersions.Add(new ProductVersionRecord
            {
                Id = productVersionId, OrganizationId = organizationId, ProductId = productId, VersionNumber = 1, NameZhTw = "PCR"
            });
            seed.PcrVersions.Add(Pcr(organizationId, pcrId, scenario));
            await seed.SaveChangesAsync();
        }

        var functionalUnit = scenario == "wrong-functional-unit" ? "1 kg product" : "1 piece of product";
        var declaredUnit = scenario == "wrong-declared-unit" ? "kg" : "piece";
        var boundary = scenario == "wrong-boundary" ? "cradle-to-gate" : "cradle-to-grave";
        await using var db = Db(organizationId);
        var (page, result) = await CreateInventoryAsync(db, organizationId, actorId, productVersionId, pcrId,
            functionalUnit, declaredUnit, boundary);

        if (accepted)
        {
            Assert.True(result is RedirectToPageResult, string.Join("; ", page.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)) + result.GetType().Name);
            await using var verify = Db(organizationId);
            Assert.Equal(pcrId, (await verify.InventoryProjectVersions.AsNoTracking().SingleAsync()).PcrVersionId);
            return;
        }
        Assert.IsType<PageResult>(result);
        Assert.All(page.ModelState["inventory"]!.Errors, error => Assert.StartsWith(code + "：", error.ErrorMessage, StringComparison.Ordinal));
        await using var after = Db(organizationId);
        Assert.False(await after.InventoryProjectVersions.AnyAsync());
    }

    private static PcrVersionRecord Pcr(Guid organizationId, Guid id, string scenario) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        RuleSetId = Guid.NewGuid(),
        RegistrationNumber = $"PCR-{id:N}",
        VersionNumber = 1,
        Title = "Selection fixture",
        ApprovalDate = new DateOnly(2025, 12, 1),
        ValidFrom = new DateOnly(2025, 12, 1),
        ValidTo = scenario == "expired" ? new DateOnly(2026, 6, 30) : new DateOnly(2027, 12, 31),
        PublicationStatus = scenario switch { "draft" => "Draft", "withdrawn" => "Withdrawn", _ => "Published" },
        SourceReference = "https://example.test/pcr",
        OriginalDocumentObjectKey = $"test/{id:N}",
        OriginalDocumentScanStatus = "Clean",
        OriginalDocumentSha256 = new string('a', 64),
        ProductCategoryPatterns = "ELECTRONICS-*",
        FunctionalUnitPattern = "1 piece*",
        DeclaredUnitCode = "piece",
        SystemBoundaryCode = "cradle-to-grave",
        PermittedAllocationMethodsCsv = "mass",
        FormulaRuleSetVersion = "test-v1",
        ReviewStatus = "Approved",
        CustomApprovalStatus = "NotRequired",
        DeprecatedAt = scenario == "superseded" ? DateTimeOffset.UtcNow : null,
        WithdrawnAt = scenario == "withdrawn" ? DateTimeOffset.UtcNow : null,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task<(WorkspaceModel Page, IActionResult Result)> CreateInventoryAsync(
        CarbonFootprintDbContext db, Guid organizationId, Guid actorId, Guid productVersionId, Guid pcrId,
        string functionalUnit, string declaredUnit, string boundary)
    {
        using var services = new ServiceCollection().AddSingleton(db).AddLogging()
            .AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<CarbonFootprintDbContext>().Services.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "fixture"))
        };
        var page = new WorkspaceModel(db, new Scope(organizationId), null!, null!, null!,
            services.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, new InventorySnapshotReader(db),
            new AllowedAuthorization(), null!, null!, new EphemeralDataProtectionProvider(),
            CalculationBuildProvenance.Create("pcr-selection-fixture", new string('a', 40)), null!, null!, InventoryServices.Workflow(db, new AllowedAuthorization(), new Scope(organizationId)), InventoryServices.Readiness(db), InventoryServices.Activities(db, new AllowedAuthorization(), new Scope(organizationId)))
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new FixtureTempData())
        };
        var result = await page.OnPostCreateInventoryAsync(productVersionId, PeriodStart, PeriodEnd, functionalUnit,
            declaredUnit, boundary, "mass", "mass allocation", "none", "none", "none", pcrId, default);
        return (page, result);
    }

    private static DbContextOptions<CarbonFootprintDbContext> Options() => new DbContextOptionsBuilder<CarbonFootprintDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION") ?? throw new InvalidOperationException("Isolated database required"))
        .UseSnakeCaseNamingConvention().Options;
    private static CarbonFootprintDbContext Db(Guid organizationId) => new(Options(), new Scope(organizationId));
    private sealed record Scope(Guid Id) : IOrganizationScope { public Guid? OrganizationId => Id; }
    private sealed class AllowedAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Success());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(AuthorizationResult.Success());
    }
    private sealed class FixtureTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
