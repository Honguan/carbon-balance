using System.Security.Claims;
using CarbonFootprint.Application.Governance;
using CarbonFootprint.Application.Operations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Infrastructure.Governance;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

/// <summary>Governance transitions run without any Razor PageModel: authorization, validation, audit and concurrency live in the service.</summary>
public sealed class GovernanceServiceTests
{
    [Fact]
    public async Task Pcr_DeniedCallerCannotReviewAndNothingChanges()
    {
        var (organizationId, creator, _) = await SeedAsync();
        var pcr = await SeedPcrAsync(organizationId, creator);
        await using var db = Db(organizationId);

        var result = await new PcrGovernanceService(db, new Authorizer(false), new Scope(organizationId))
            .ReviewAsync(Context(Guid.NewGuid()), pcr.Id, pcr.RowVersion, default);

        Assert.Equal(OperationStatus.Forbidden, result.Status);
        await using var verify = Db(organizationId);
        Assert.Equal("Pending", (await verify.PcrVersions.AsNoTracking().SingleAsync()).ReviewStatus);
        Assert.False(await verify.AuditEvents.AnyAsync());
    }

    [Fact]
    public async Task Pcr_CreatorCannotApproveOwnVersionButAnotherReviewerCan()
    {
        var (organizationId, creator, reviewer) = await SeedAsync();
        var pcr = await SeedPcrAsync(organizationId, creator);

        await using (var db = Db(organizationId))
        {
            var own = await new PcrGovernanceService(db, new Authorizer(true), new Scope(organizationId))
                .ReviewAsync(Context(creator), pcr.Id, pcr.RowVersion, default);
            Assert.Equal(OperationStatus.Invalid, own.Status);
        }
        await using (var db = Db(organizationId))
        {
            var other = await new PcrGovernanceService(db, new Authorizer(true), new Scope(organizationId))
                .ReviewAsync(Context(reviewer), pcr.Id, pcr.RowVersion, default);
            Assert.True(other.Succeeded);
        }

        await using var verify = Db(organizationId);
        var stored = await verify.PcrVersions.AsNoTracking().SingleAsync();
        Assert.Equal("Approved", stored.ReviewStatus);
        Assert.Equal(reviewer, stored.ReviewedBy);
        var audit = await verify.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("pcr.version.reviewed", audit.Action);
        Assert.Equal(reviewer, audit.ActorId);
    }

    [Fact]
    public async Task Pcr_StaleVersionIsRejectedAsConflictAndAuditedWithoutMutation()
    {
        var (organizationId, creator, reviewer) = await SeedAsync();
        var pcr = await SeedPcrAsync(organizationId, creator);
        await using (var editor = Db(organizationId))
        {
            (await editor.PcrVersions.SingleAsync()).Title = "Edited elsewhere";
            await editor.SaveChangesAsync();
        }

        await using var db = Db(organizationId);
        var result = await new PcrGovernanceService(db, new Authorizer(true), new Scope(organizationId))
            .RejectAsync(Context(reviewer), pcr.Id, pcr.RowVersion, default);

        Assert.Equal(OperationStatus.Conflict, result.Status);
        await using var verify = Db(organizationId);
        Assert.Equal("Pending", (await verify.PcrVersions.AsNoTracking().SingleAsync()).ReviewStatus);
        Assert.Equal("concurrency.rejected", (await verify.AuditEvents.AsNoTracking().SingleAsync()).Action);
    }

    [Fact]
    public async Task Factor_PublishWithdrawsPublishedPredecessorAndRejectsUnreviewedDraft()
    {
        var (organizationId, _, reviewer) = await SeedAsync();
        var factorId = Guid.NewGuid();
        var published = Factor(organizationId, factorId, 1, "Published", "Approved");
        var draft = Factor(organizationId, factorId, 2, "Draft", "Pending");
        await using (var seed = Db(organizationId))
        {
            seed.EmissionFactorVersions.AddRange(published, draft);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(organizationId))
        {
            var early = await new FactorGovernanceService(db, new Authorizer(true), new Scope(organizationId))
                .PublishAsync(Context(reviewer), draft.Id, draft.RowVersion, default);
            Assert.Equal(OperationStatus.Invalid, early.Status);
        }
        await using (var db = Db(organizationId))
        {
            var service = new FactorGovernanceService(db, new Authorizer(true), new Scope(organizationId));
            var current = await db.EmissionFactorVersions.AsNoTracking().SingleAsync(item => item.Id == draft.Id);
            Assert.True((await service.ReviewAsync(Context(reviewer), draft.Id, current.RowVersion, default)).Succeeded);
        }
        await using (var db = Db(organizationId))
        {
            var service = new FactorGovernanceService(db, new Authorizer(true), new Scope(organizationId));
            var current = await db.EmissionFactorVersions.AsNoTracking().SingleAsync(item => item.Id == draft.Id);
            Assert.True((await service.PublishAsync(Context(reviewer), draft.Id, current.RowVersion, default)).Succeeded);
        }

        await using var verify = Db(organizationId);
        var rows = await verify.EmissionFactorVersions.AsNoTracking().ToDictionaryAsync(item => item.Id);
        Assert.Equal("Withdrawn", rows[published.Id].PublicationStatus);
        Assert.Equal("Published", rows[draft.Id].PublicationStatus);
        Assert.Equal(1, await verify.AuditEvents.CountAsync(item => item.Action == "factor.version.published"));
        Assert.Equal(1, await verify.AuditEvents.CountAsync(item => item.Action == "factor.version.withdrawn"));
    }

    private static async Task<(Guid OrganizationId, Guid Creator, Guid Reviewer)> SeedAsync()
    {
        var organizationId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        await using var seed = Db(organizationId);
        foreach (var id in new[] { creator, reviewer })
            seed.Users.Add(new CarbonFootprint.Infrastructure.Identity.ApplicationUser { Id = id, UserName = $"gov-{id:N}@example.test", Email = $"gov-{id:N}@example.test" });
        seed.Organizations.Add(new OrganizationRecord { Id = organizationId, Name = "Governance fixture" });
        await seed.SaveChangesAsync();
        return (organizationId, creator, reviewer);
    }

    private static async Task<PcrVersionRecord> SeedPcrAsync(Guid organizationId, Guid creator)
    {
        await using var seed = Db(organizationId);
        var pcr = new PcrVersionRecord
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RuleSetId = Guid.NewGuid(), RegistrationNumber = $"PCR-{Guid.NewGuid():N}",
            VersionNumber = 1, Title = "Governance fixture", PublicationStatus = "Draft", SourceReference = "https://example.test/pcr",
            ReviewStatus = "Pending", CustomApprovalStatus = "NotRequired", CreatedBy = creator, CreatedAt = DateTimeOffset.UtcNow
        };
        seed.PcrVersions.Add(pcr);
        await seed.SaveChangesAsync();
        return pcr;
    }

    private static EmissionFactorVersionRecord Factor(Guid organizationId, Guid factorId, int version, string status, string review) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, FactorId = factorId, VersionNumber = version, Name = "Steel", Value = 2m,
        NumeratorUnitCode = "kgCO2e", DenominatorUnitCode = "kg", Geography = "TW", ValidFrom = new DateOnly(2026, 1, 1),
        PublicationStatus = status, SourceDatasetVersion = "v1", LicenseCode = "CC0", SourceType = "government-database", SourceName = "MOENV",
        SourceReference = "https://example.test/factor", DatasetName = "dataset", OriginalDocumentName = "factor.csv",
        OriginalDocumentSha256 = new string('a', 64), Applicability = "global", ReviewStatus = review
    };

    private static OperationContext Context(Guid actor) =>
        new(new ClaimsPrincipal(new ClaimsIdentity()), actor, "gov-" + Guid.NewGuid().ToString("N"), null);

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
