using System.Security.Claims;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Application.Organizations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

/// <summary>Organization administration and product catalogue operations run without a Razor PageModel.</summary>
public sealed class OrganizationProductServiceTests
{
    [Fact]
    public async Task Facility_CodesAreNormalizedAndUnique()
    {
        var organizationId = await SeedOrganizationAsync();
        await using var db = Db(organizationId);
        var products = new ProductCatalogueService(db, new Authorizer(true), new Scope(organizationId));

        Assert.True((await products.CreateFacilityAsync(Context(), " tp-01 ", " Taipei ", default)).Succeeded);
        var duplicate = await products.CreateFacilityAsync(Context(), "TP-01", "Again", default);
        Assert.Equal(("facilityCode", "廠場代碼不可重複。"), (duplicate.Key, duplicate.Message));
        Assert.Equal(OperationStatus.Invalid, (await products.CreateFacilityAsync(Context(), "", "x", default)).Status);

        await using var verify = Db(organizationId);
        Assert.Equal("TP-01", (await verify.Facilities.AsNoTracking().SingleAsync()).Code);
        Assert.Equal("facility.created", (await verify.AuditEvents.AsNoTracking().SingleAsync()).Action);
    }

    [Fact]
    public async Task Product_RequiresOwnFacility_AndCreatesAuditedFirstVersion()
    {
        var organizationId = await SeedOrganizationAsync();
        var foreignOrganizationId = await SeedOrganizationAsync();
        var facilityId = await SeedFacilityAsync(organizationId);
        var foreignFacilityId = await SeedFacilityAsync(foreignOrganizationId);
        await using var db = Db(organizationId);

        Assert.Equal(OperationStatus.Forbidden,
            (await new ProductCatalogueService(db, new Authorizer(false), new Scope(organizationId)).CreateProductAsync(Context(), "P", "C", facilityId, default)).Status);
        var products = new ProductCatalogueService(db, new Authorizer(true), new Scope(organizationId));
        Assert.Equal("所選廠場不存在或不屬於目前組織。", (await products.CreateProductAsync(Context(), "P", "C", foreignFacilityId, default)).Message);
        Assert.True((await products.CreateProductAsync(Context(), " Widget ", " ab ", facilityId, default)).Succeeded);

        await using var verify = Db(organizationId);
        var product = await verify.Products.AsNoTracking().SingleAsync();
        Assert.Equal(("Widget", "AB"), (product.Name, product.CategoryCode));
        Assert.Equal(1, (await verify.ProductVersions.AsNoTracking().SingleAsync()).VersionNumber);
        Assert.Equal(["Product", "ProductVersion"], (await verify.AuditEvents.AsNoTracking().Select(item => item.ResourceType).ToArrayAsync())
            .Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Membership_OwnerCannotBeRevoked_AndStaleVersionsAreConflicts()
    {
        var organizationId = await SeedOrganizationAsync();
        var ownerId = Guid.NewGuid();
        Guid membershipId;
        await using (var seed = Db(organizationId))
        {
            seed.Users.Add(new CarbonFootprint.Infrastructure.Identity.ApplicationUser { Id = ownerId, UserName = $"o-{ownerId:N}@example.test", Email = $"o-{ownerId:N}@example.test" });
            var membership = new OrganizationMembershipRecord { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = ownerId, Role = "Owner" };
            seed.OrganizationMemberships.Add(membership);
            await seed.SaveChangesAsync();
            membershipId = membership.Id;
        }
        await using var db = Db(organizationId);
        var rowVersion = (await db.OrganizationMemberships.AsNoTracking().SingleAsync()).RowVersion;
        var service = Administration(db, organizationId);

        var owner = await service.RevokeMemberAsync(Context(), membershipId, rowVersion, default);
        Assert.Equal(("membership", "不可撤銷組織擁有者。"), (owner.Key, owner.Message));
        Assert.Equal(OperationStatus.Conflict, (await service.RevokeMemberAsync(Context(), membershipId, rowVersion + 1, default)).Status);

        await using var verify = Db(organizationId);
        Assert.Null((await verify.OrganizationMemberships.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Equal("concurrency.rejected", (await verify.AuditEvents.AsNoTracking().SingleAsync()).Action);
    }

    [Theory]
    [InlineData("", 587, "sender@example.test", "Sender")]
    [InlineData("smtp.example.test", 0, "sender@example.test", "Sender")]
    [InlineData("smtp.example.test", 587, "", "Sender")]
    [InlineData("smtp.example.test", 587, "Name <sender@example.test>", "Sender")]
    [InlineData("smtp.example.test", 587, "sender@example.test", " ")]
    public async Task MailSettings_InvalidInputIsRejectedBeforeAnyWrite(string host, int port, string from, string fromName)
    {
        var organizationId = await SeedOrganizationAsync();
        await using var db = Db(organizationId);

        var result = await Administration(db, organizationId)
            .SaveMailSettingsAsync(Context(), new MailSettingsInput(host, port, true, null, null, from, fromName), 0, default);

        Assert.Equal(("mail", OperationStatus.Invalid), (result.Key, result.Status));
        await using var verify = Db(organizationId);
        Assert.False(await verify.OrganizationMailSettings.AnyAsync());
    }

    [Fact]
    public async Task TestMail_InvalidRecipientIsRejected()
    {
        var organizationId = await SeedOrganizationAsync();
        await using var db = Db(organizationId);
        var service = Administration(db, organizationId);

        Assert.Equal("recipient", (await service.SendTestMailAsync(Context(), "", default)).Key);
        Assert.Equal("recipient", (await service.SendTestMailAsync(Context(), "not-an-address", default)).Key);
    }

    // Validation and membership guards return before SMTP, invitations or the user store are touched.
    private static OrganizationAdministrationService Administration(CarbonFootprintDbContext db, Guid organizationId) =>
        new(db, new Authorizer(true), new Scope(organizationId), null!, null!, null!, new EphemeralDataProtectionProvider());

    private static async Task<Guid> SeedOrganizationAsync()
    {
        var organizationId = Guid.NewGuid();
        await using var seed = Db(organizationId);
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Organization fixture" });
        await seed.SaveChangesAsync();
        return organizationId;
    }

    private static async Task<Guid> SeedFacilityAsync(Guid organizationId)
    {
        var facilityId = Guid.NewGuid();
        await using var seed = Db(organizationId);
        seed.Facilities.Add(new FacilityRecord { Id = facilityId, OrganizationId = organizationId, Code = "F" + facilityId.ToString("N")[..6], Name = "Facility" });
        await seed.SaveChangesAsync();
        return facilityId;
    }

    private static OperationContext Context() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), Guid.NewGuid(), "org-" + Guid.NewGuid().ToString("N"), null);

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
