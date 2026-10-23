using System.Security.Claims;
using System.Text.Json;
using CarbonFootprint.Domain.Modules.Calculations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Web.Pages;
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

public sealed class AuditMutationPathTests
{
    [Theory]
    [InlineData("product", false)]
    [InlineData("product", true)]
    [InlineData("smtp", false)]
    [InlineData("smtp", true)]
    public async Task RealWorkspaceMutationRecordsAllResourcesAndRollsBackWhenAuditInsertFails(string operation, bool rejectAudit)
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        await using (var seed = Db(organizationId))
        {
            seed.Users.Add(User(actorId));
            seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Handler audit fixture" });
            seed.Facilities.Add(new FacilityRecord { Id = facilityId, OrganizationId = organizationId, Name = "Fixture facility", Code = "fixture" });
            await seed.SaveChangesAsync();
        }
        var correlation = rejectAudit ? new string('x', 101) : "handler-" + Guid.NewGuid().ToString("N");
        await using (var db = Db(organizationId))
        {
            using var services = new ServiceCollection().AddSingleton(db).AddLogging()
                .AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<CarbonFootprintDbContext>().Services.BuildServiceProvider();
            var http = new DefaultHttpContext
            {
                TraceIdentifier = correlation,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "fixture"))
            };
            var mailPolicy = new SmtpEgressPolicy(new MailOptions(), false, (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("8.8.8.8") }));
            var emailSender = new SmtpEmailSender(Microsoft.Extensions.Options.Options.Create(new MailOptions()), db, new Scope(organizationId),
                new EphemeralDataProtectionProvider(), mailPolicy, new HttpContextAccessor());
            var page = new WorkspaceModel(db, new Scope(organizationId), null!, null!, emailSender,
                services.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, new InventorySnapshotReader(db),
                new AllowedAuthorization(), null!, null!, new EphemeralDataProtectionProvider(),
                CalculationBuildProvenance.Create("audit-fixture", new string('a', 40)))
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new FixtureTempData())
            };
            Task<IActionResult> Invoke() => operation == "product"
                ? page.OnPostCreateProductAsync("Audited product", "fixture", facilityId, default)
                : page.OnPostSaveMailSettingsAsync("smtp.example.test", 587, true, "login-secret@example.test",
                    "password-secret-sentinel", "sender-secret@example.test", "sender-secret-name", default);
            if (rejectAudit) await Assert.ThrowsAsync<DbUpdateException>(Invoke);
            else Assert.IsType<RedirectToPageResult>(await Invoke());
        }
        await using var verify = Db(organizationId);
        var audits = await verify.AuditEvents.AsNoTracking().ToArrayAsync();
        Assert.Equal(rejectAudit ? 0 : operation == "product" ? 2 : 1, audits.Length);
        Assert.Equal(!rejectAudit && operation == "product", await verify.Products.AnyAsync());
        Assert.Equal(!rejectAudit && operation == "product", await verify.ProductVersions.AnyAsync());
        Assert.Equal(!rejectAudit && operation == "smtp", await verify.OrganizationMailSettings.AnyAsync());
        if (rejectAudit) return;
        Assert.Equal(operation == "product" ? new[] { "Product", "ProductVersion" } : new[] { "OrganizationMailSettings" },
            audits.Select(item => item.ResourceType).Order(StringComparer.Ordinal).ToArray());
        foreach (var audit in audits) AssertCreateEvidence(audit, actorId, correlation);
        if (operation == "smtp")
        {
            var audit = Assert.Single(audits);
            Assert.DoesNotContain("secret", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
            using var metadata = JsonDocument.Parse(audit.MetadataJson);
            Assert.True(metadata.RootElement.GetProperty("credentialsChanged").GetBoolean());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealOnboardingCreatesOrganizationAndOwnerEvidenceAtomically(bool rejectAudit)
    {
        var user = User(Guid.NewGuid());
        await using (var seed = Db(Guid.NewGuid())) { seed.Users.Add(user); await seed.SaveChangesAsync(); }
        var correlation = rejectAudit ? new string('y', 101) : "onboarding-" + Guid.NewGuid().ToString("N");
        var service = new OrganizationOnboardingService(Options());
        if (rejectAudit)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(user, correlation, default, correlation));
            await using var verify = Db(Guid.NewGuid());
            Assert.False(await verify.Organizations.IgnoreQueryFilters().AnyAsync(item => item.Name == correlation));
            Assert.False(await verify.OrganizationMemberships.IgnoreQueryFilters().AnyAsync(item => item.UserId == user.Id));
            return;
        }
        var organizationId = await service.CreateAsync(user, correlation, default, correlation, "session-fixture-reference");
        await using var db = Db(organizationId);
        Assert.True(await db.OrganizationMemberships.AnyAsync(item => item.UserId == user.Id && item.Role == "Owner"));
        var audits = await db.AuditEvents.AsNoTracking().ToArrayAsync();
        Assert.Equal(new[] { "Organization", "OrganizationMembership" }, audits.Select(item => item.ResourceType).Order(StringComparer.Ordinal).ToArray());
        foreach (var audit in audits) AssertCreateEvidence(audit, user.Id, correlation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealInvitationAcceptanceRecordsBothResourcesWithoutTokenOrEmailAndIsAtomic(bool rejectAudit)
    {
        var owner = User(Guid.NewGuid());
        var invitee = User(Guid.NewGuid());
        await using (var seed = Db(Guid.NewGuid())) { seed.Users.AddRange(owner, invitee); await seed.SaveChangesAsync(); }
        var organizationId = await new OrganizationOnboardingService(Options()).CreateAsync(owner, "Invite audit fixture", default);
        var service = new OrganizationInvitationService(Options());
        var createCorrelation = "invite-create-" + Guid.NewGuid().ToString("N");
        var token = await service.CreateAsync(organizationId, owner.Id, invitee.Email!, OrganizationRole.Contributor, default, createCorrelation);
        var correlation = rejectAudit ? new string('z', 101) : "invite-accept-" + Guid.NewGuid().ToString("N");
        if (rejectAudit) await Assert.ThrowsAsync<DbUpdateException>(() => service.AcceptAsync(invitee, token, default, correlation));
        else Assert.Equal(organizationId, await service.AcceptAsync(invitee, token, default, correlation));
        await using var db = Db(organizationId);
        var invitation = await db.OrganizationInvitations.SingleAsync();
        Assert.Equal(!rejectAudit, invitation.AcceptedAt.HasValue);
        Assert.Equal(!rejectAudit, await db.OrganizationMemberships.AnyAsync(item => item.UserId == invitee.Id));
        var createdAudit = await db.AuditEvents.SingleAsync(item => item.CorrelationId == createCorrelation);
        AssertCreateEvidence(createdAudit, owner.Id, createCorrelation);
        var audits = await db.AuditEvents.Where(item => item.CorrelationId == correlation).ToArrayAsync();
        Assert.Equal(rejectAudit ? 0 : 2, audits.Length);
        foreach (var audit in audits.Append(createdAudit))
        {
            Assert.DoesNotContain(token, audit.MetadataJson);
            Assert.DoesNotContain(invitee.Email!, audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        }
        if (rejectAudit) return;
        var invitationAudit = Assert.Single(audits, item => item.ResourceType == "OrganizationInvitation");
        Assert.Equal(createdAudit.AfterHash, invitationAudit.BeforeHash);
        Assert.NotEqual(invitationAudit.BeforeHash, invitationAudit.AfterHash);
        Assert.Equal(invitee.Id, invitationAudit.ActorId);
        var membershipAudit = Assert.Single(audits, item => item.ResourceType == "OrganizationMembership");
        AssertCreateEvidence(membershipAudit, invitee.Id, correlation);
    }

    private static void AssertCreateEvidence(AuditEventRecord audit, Guid actor, string correlation)
    {
        Assert.Equal(actor, audit.ActorId); Assert.Equal(correlation, audit.CorrelationId);
        Assert.Null(audit.BeforeHash); Assert.NotNull(audit.AfterHash);
        using var metadata = JsonDocument.Parse(audit.MetadataJson);
        Assert.Equal(AuditWriter.SchemaVersion, metadata.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("create", metadata.RootElement.GetProperty("operation").GetString());
        Assert.NotEmpty(metadata.RootElement.GetProperty("changedFields").EnumerateArray());
        Assert.NotEmpty(metadata.RootElement.GetProperty("safeChanges").EnumerateObject());
        Assert.False(string.IsNullOrWhiteSpace(metadata.RootElement.GetProperty("reason").GetString()));
        Assert.Equal(correlation, metadata.RootElement.GetProperty("correlationId").GetString());
    }

    private static ApplicationUser User(Guid id) => new()
    {
        Id = id,
        UserName = $"audit-{id:N}@example.test",
        Email = $"audit-{id:N}@example.test",
        NormalizedUserName = $"AUDIT-{id:N}@EXAMPLE.TEST".ToUpperInvariant(),
        NormalizedEmail = $"AUDIT-{id:N}@EXAMPLE.TEST".ToUpperInvariant()
    };
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
