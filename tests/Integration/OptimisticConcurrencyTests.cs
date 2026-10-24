using System.Security.Claims;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Pages;
using CarbonFootprint.Web.Services;
using CarbonFootprint.Web.Security;
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

public sealed class OptimisticConcurrencyTests
{
    [Fact]
    public async Task ConcurrentPostgreSqlUpdatesOfOneWorkflowRow_LetExactlyOneWriterWin()
    {
        var organizationId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(organizationId);
        await using var first = Db(organizationId);
        await using var second = Db(organizationId);
        var a = await first.InventoryProjectVersions.SingleAsync(item => item.Id == projectId);
        var b = await second.InventoryProjectVersions.SingleAsync(item => item.Id == projectId);
        Assert.Equal(a.RowVersion, b.RowVersion);
        a.WorkflowStatus = "Submitted";
        b.WorkflowStatus = "ChangesRequested";

        using var gate = new Barrier(2);
        async Task<Exception?> Save(CarbonFootprintDbContext db)
        {
            await Task.Yield();
            gate.SignalAndWait();
            try { await db.SaveChangesAsync(); return null; }
            catch (DbUpdateConcurrencyException exception) { return exception; }
        }
        var results = await Task.WhenAll(Save(first), Save(second));

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result is DbUpdateConcurrencyException);
        await using var verify = Db(organizationId);
        var stored = await verify.InventoryProjectVersions.AsNoTracking().SingleAsync(item => item.Id == projectId);
        Assert.Equal(results[0] is null ? "Submitted" : "ChangesRequested", stored.WorkflowStatus);
    }

    [Fact]
    public async Task StaleFormToken_IsRejectedWithConflictAndRecordedWithoutChangingNewerState()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await SeedProjectAsync(organizationId, actorId, withMailSettings: true);
        uint staleVersion;
        await using (var reader = Db(organizationId))
            staleVersion = (await reader.OrganizationMailSettings.AsNoTracking().SingleAsync()).RowVersion;
        await using (var editor = Db(organizationId))
        {
            var settings = await editor.OrganizationMailSettings.SingleAsync();
            settings.Host = "newer.example.test";
            await editor.SaveChangesAsync();
        }

        var stale = await SaveMailAsync(organizationId, actorId, "stale.example.test", staleVersion);
        Assert.IsType<PageResult>(stale.Result);
        Assert.Equal(StatusCodes.Status409Conflict, stale.StatusCode);
        Assert.True(stale.ModelState.ContainsKey("concurrency"));

        await using var verify = Db(organizationId);
        var current = await verify.OrganizationMailSettings.AsNoTracking().SingleAsync();
        Assert.Equal("newer.example.test", current.Host);
        var rejection = await verify.AuditEvents.AsNoTracking().SingleAsync(item => item.Action == "concurrency.rejected");
        Assert.Equal("OrganizationMailSettings", rejection.ResourceType);
        Assert.Equal(current.Id, rejection.ResourceId);
        Assert.Null(rejection.BeforeHash);
        Assert.Null(rejection.AfterHash);
        Assert.Contains("SaveMailSettings", rejection.MetadataJson);

        var fresh = await SaveMailAsync(organizationId, actorId, "fresh.example.test", current.RowVersion);
        Assert.IsType<RedirectToPageResult>(fresh.Result);
        await using var after = Db(organizationId);
        Assert.Equal("fresh.example.test", (await after.OrganizationMailSettings.AsNoTracking().SingleAsync()).Host);
    }

    private static async Task<(IActionResult Result, int StatusCode, Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary ModelState)>
        SaveMailAsync(Guid organizationId, Guid actorId, string host, uint rowVersion)
    {
        await using var db = Db(organizationId);
        using var services = new ServiceCollection().AddSingleton(db).AddLogging()
            .AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<CarbonFootprintDbContext>().Services.BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "fixture"))
        };
        var mailPolicy = new SmtpEgressPolicy(new MailOptions(), false, (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("8.8.8.8") }));
        var emailSender = new SmtpEmailSender(Microsoft.Extensions.Options.Options.Create(new MailOptions()), db, new Scope(organizationId),
            new EphemeralDataProtectionProvider(), mailPolicy, new HttpContextAccessor());
        var page = new WorkspaceModel(db, new Scope(organizationId), null!, null!, emailSender,
            services.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, new InventorySnapshotReader(db),
            new AllowedAuthorization(), null!, null!, new EphemeralDataProtectionProvider(),
            CalculationBuildProvenance.Create("concurrency-fixture", new string('a', 40)), null!, null!)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new FixtureTempData())
        };
        var result = await page.OnPostSaveMailSettingsAsync(host, 587, true, "", "", "sender@example.test", "sender", rowVersion, default);
        return (result, http.Response.StatusCode, page.ModelState);
    }

    private static async Task<Guid> SeedProjectAsync(Guid organizationId, Guid? actorId = null, bool withMailSettings = false)
    {
        await using var seed = Db(organizationId);
        var productId = Guid.NewGuid();
        var productVersionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        if (actorId is { } id) seed.Users.Add(new ApplicationUser { Id = id, UserName = $"cc-{id:N}@example.test", Email = $"cc-{id:N}@example.test" });
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Concurrency fixture" });
        seed.Products.Add(new ProductRecord { Id = productId, OrganizationId = organizationId, Name = "Concurrency" });
        seed.ProductVersions.Add(new ProductVersionRecord
        {
            Id = productVersionId, OrganizationId = organizationId, ProductId = productId, VersionNumber = 1, NameZhTw = "Concurrency"
        });
        seed.InventoryProjectVersions.Add(new InventoryProjectVersionRecord
        {
            Id = projectId, OrganizationId = organizationId, ProductVersionId = productVersionId, VersionNumber = 1,
            PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31),
            FunctionalUnit = "1 kg product", PcrVersion = "none", WorkflowStatus = "Draft"
        });
        if (withMailSettings)
            seed.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
            {
                Id = Guid.NewGuid(), OrganizationId = organizationId, Host = "initial.example.test", Port = 587,
                FromAddress = "initial@example.test", FromName = "initial", UpdatedAt = DateTimeOffset.UtcNow
            });
        await seed.SaveChangesAsync();
        return projectId;
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
