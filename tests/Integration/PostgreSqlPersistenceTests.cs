using CarbonFootprint.Application.Factors;
using CarbonFootprint.Domain.Modules.Factors;
using CarbonFootprint.Infrastructure.Persistence;
using CarbonFootprint.Infrastructure.LegacyImport;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Organizations;
using CarbonFootprint.Domain.Modules.Organizations;
using CarbonFootprint.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CarbonFootprint.Integration.Tests;

public sealed class PostgreSqlPersistenceTests
{
    [Fact]
    public void Model_HasNoPendingMigrationChanges()
    {
        using var dbContext = CreateContext(Guid.NewGuid());

        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task UnitCatalogueV2_SeedsTransportAndTonneConversions()
    {
        await using var dbContext = CreateContext(Guid.NewGuid());

        var units = await dbContext.Units
            .Where(item => item.CatalogueVersion == "units-p0-v2")
            .OrderBy(item => item.Code)
            .ToArrayAsync();

        Assert.Equal(5, units.Length);
        Assert.Equal(1000m, units.Single(item => item.Code == "tonne").ScaleToCanonical);
        Assert.Equal("transport-work", units.Single(item => item.Code == "tonne-km").Dimension);
    }

    [Fact]
    public async Task QueryFilters_KeepOrganizationsIsolated_AndRejectCrossTenantWrite()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        await using (var contextA = CreateContext(organizationA))
        {
            contextA.Organizations.Add(new OrganizationRecord
            {
                Id = organizationA,
                Name = "整合測試組織 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            contextA.Products.Add(new ProductRecord
            {
                Id = productA,
                OrganizationId = organizationA,
                Name = "產品 A",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = CreateContext(organizationB))
        {
            contextB.Organizations.Add(new OrganizationRecord
            {
                Id = organizationB,
                Name = "整合測試組織 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            contextB.Products.Add(new ProductRecord
            {
                Id = productB,
                OrganizationId = organizationB,
                Name = "產品 B",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await contextB.SaveChangesAsync();
        }

        await using (var contextA = CreateContext(organizationA))
        {
            Assert.Equal([productA], await contextA.Products.Select(item => item.Id).ToArrayAsync());
            Assert.Null(await contextA.Products.SingleOrDefaultAsync(item => item.Id == productB));

            contextA.Products.Add(new ProductRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                Name = "越權寫入",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => contextA.SaveChangesAsync());
            Assert.Contains("不符合目前組織範圍", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task QueryFilters_ResolveOrganizationWhenRequestScopeBecomesAvailable()
    {
        var organizationId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using (var seededContext = CreateContext(organizationId))
        {
            seededContext.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "延遲租戶測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            seededContext.Products.Add(new ProductRecord
            {
                Id = productId,
                OrganizationId = organizationId,
                Name = "延遲租戶測試產品",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seededContext.SaveChangesAsync();
        }

        var scope = new MutableOrganizationScope();
        await using var context = CreateContext(scope);
        Assert.Empty(await context.Products.ToArrayAsync());

        scope.OrganizationId = organizationId;
        Assert.Equal([productId], await context.Products.Select(item => item.Id).ToArrayAsync());
    }

    [Fact]
    public async Task LegacyFactorImporter_StagesValidInvalidAndConflictRowsWithoutPublishing()
    {
        var organizationId = Guid.NewGuid();
        var uniqueName = Guid.NewGuid().ToString("N");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"legacy-factors-{uniqueName}.csv");
        await File.WriteAllTextAsync(
            sourcePath,
            $"name,value,denominator_unit,source_version,license_code\n" +
            $"factor-{uniqueName},2.5,kg,dataset-1,fixture\n" +
            $"invalid-{uniqueName},-1,unknown,dataset-1,fixture\n" +
            $"factor-{uniqueName},2.5,kg,dataset-1,fixture\n");

        try
        {
            await using var context = CreateContext(organizationId);
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "Legacy staging 測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();

            var report = await new LegacyFactorCsvImporter(context).ImportAsync(
                organizationId,
                sourcePath,
                CancellationToken.None);

            Assert.Equal(1, report.ParsedRows);
            Assert.Equal(1, report.InvalidRows);
            Assert.Equal(1, report.ConflictRows);
            Assert.Equal(3, await context.LegacyStagingRows.CountAsync());
            Assert.Empty(await context.EmissionFactorVersions.ToArrayAsync());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task MoenvFactorSynchronization_IsIdempotentAndKeepsImportedFactorAsDraft()
    {
        var organizationId = Guid.NewGuid();
        await using (var context = CreateContext(organizationId))
        {
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "部署係數匯入測試組織",
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.EmissionFactorVersions.Add(new EmissionFactorVersionRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                FactorId = Guid.NewGuid(),
                VersionNumber = 1,
                Name = "測試天然氣",
                Value = 2.5m,
                NumeratorUnitCode = "kgCO2e",
                DenominatorUnitCode = "kg",
                Geography = "TW",
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = null,
                PublicationStatus = FactorPublicationStatus.Draft.ToString(),
                SourceDatasetVersion = "CFP_P_02-2026",
                LicenseCode = "政府資料開放授權條款第1版",
                SourceType = "government-database",
                SourceName = "環境部",
                SourceReference = MoenvFactorClient.DatasetReference,
                DatasetName = "環境部碳足跡排放係數",
                OriginalDocumentName = "CFP_P_02-record-draft.json",
                OriginalDocumentSha256 = new string('a', 64),
                Applicability = "測試適用性",
                ReviewStatus = FactorReviewStatus.Pending.ToString()
            });
            await context.SaveChangesAsync();
        }

        var source = new StubMoenvFactorSource(new MoenvFactorDownload(
            [
                new MoenvFactorRecord(
                    "測試電力",
                    0.5m,
                    "kWh",
                    "環境部",
                    2026,
                    new string('a', 64)),
                new MoenvFactorRecord(
                    "測試天然氣",
                    2.5m,
                    "kg",
                    "環境部",
                    2026,
                    new string('a', 64))
            ],
            2));
        var service = new MoenvFactorSynchronizationService(CreateOptions(), source);

        var first = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "deployment-test",
            CancellationToken.None);
        var second = await service.SynchronizeOrganizationAsync(
            organizationId,
            actorId: null,
            correlationId: "deployment-test",
            CancellationToken.None);

        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(0, first.UnchangedCount);
        Assert.Equal(1, first.PublishedExistingCount);
        Assert.Equal(2, first.SkippedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(2, second.UnchangedCount);
        Assert.Equal(0, second.PublishedExistingCount);

        await using var verification = CreateContext(organizationId);
        var factors = await verification.EmissionFactorVersions
            .Where(item => item.SourceReference == MoenvFactorClient.DatasetReference)
            .OrderBy(item => item.Name)
            .ToArrayAsync();
        Assert.Equal(2, factors.Length);
        Assert.All(factors, factor =>
        {
            Assert.Equal(FactorPublicationStatus.Published.ToString(), factor.PublicationStatus);
            Assert.Equal(FactorReviewStatus.NotRequired.ToString(), factor.ReviewStatus);
            Assert.Null(factor.ReviewedAt);
            Assert.NotNull(factor.PublishedAt);
        });
        var factor = factors.Single(item => item.Name == "測試電力");
        var audit = await verification.AuditEvents.SingleAsync(
            item => item.Action == "factor.version.synced" && item.ResourceId == factor.Id);
        Assert.Null(audit.ActorId);
        Assert.Equal("deployment-test", audit.CorrelationId);
        Assert.Single(await verification.AuditEvents
            .Where(item => item.Action == "factor.version.auto-published")
            .ToArrayAsync());
        var synchronizationAudits = await verification.AuditEvents
            .Where(item =>
                item.Action == "factor.synchronization.completed"
                && item.ResourceId == organizationId)
            .OrderBy(item => item.Timestamp)
            .ToArrayAsync();
        Assert.Equal(2, synchronizationAudits.Length);
        Assert.All(synchronizationAudits, item =>
        {
            Assert.Null(item.ActorId);
            Assert.Equal("deployment-test", item.CorrelationId);
            Assert.Contains(MoenvFactorClient.DatasetReference, item.MetadataJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task OrganizationInvitation_RequiresMatchingEmail_AndCreatesScopedMembership()
    {
        var organizationId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var inviteeId = Guid.NewGuid();
        var inviteeEmail = $"invitee-{inviteeId:N}@example.test";
        await using (var context = CreateContext(organizationId))
        {
            context.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerId,
                    UserName = $"owner-{ownerId:N}@example.test",
                    NormalizedUserName = $"OWNER-{ownerId:N}@EXAMPLE.TEST"
                },
                new ApplicationUser
                {
                    Id = inviteeId,
                    UserName = inviteeEmail,
                    NormalizedUserName = inviteeEmail.ToUpperInvariant(),
                    Email = inviteeEmail,
                    NormalizedEmail = inviteeEmail.ToUpperInvariant()
                });
            context.Organizations.Add(new OrganizationRecord
            {
                Id = organizationId,
                Name = "Invitation integration organization",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var service = new OrganizationInvitationService(CreateOptions());
        var token = await service.CreateAsync(
            organizationId,
            ownerId,
            inviteeEmail,
            OrganizationRole.Contributor,
            CancellationToken.None);
        var wrongUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "wrong@example.test",
            NormalizedEmail = "WRONG@EXAMPLE.TEST"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(wrongUser, token, CancellationToken.None));

        var invitee = new ApplicationUser
        {
            Id = inviteeId,
            Email = inviteeEmail,
            NormalizedEmail = inviteeEmail.ToUpperInvariant()
        };
        Assert.Equal(organizationId, await service.AcceptAsync(invitee, token, CancellationToken.None));

        await using var verification = CreateContext(organizationId);
        var membership = await verification.OrganizationMemberships.SingleAsync(item => item.UserId == inviteeId);
        Assert.Equal(OrganizationRole.Contributor.ToString(), membership.Role);
        Assert.NotNull((await verification.OrganizationInvitations.SingleAsync()).AcceptedAt);
        Assert.True(await verification.UserClaims.AnyAsync(item =>
            item.UserId == inviteeId && item.ClaimType == "organization_id" && item.ClaimValue == organizationId.ToString()));
    }

    private static CarbonFootprintDbContext CreateContext(Guid organizationId)
        => CreateContext(new TestOrganizationScope(organizationId));

    private static CarbonFootprintDbContext CreateContext(IOrganizationScope organizationScope)
    {
        return new CarbonFootprintDbContext(CreateOptions(), organizationScope);
    }

    private static DbContextOptions<CarbonFootprintDbContext> CreateOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Integration test 需要 CARBON_TEST_DB_CONNECTION。");
        return new DbContextOptionsBuilder<CarbonFootprintDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
    }

    private sealed record TestOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }

    private sealed class MutableOrganizationScope : IOrganizationScope
    {
        public Guid? OrganizationId { get; set; }
    }

    private sealed class StubMoenvFactorSource(MoenvFactorDownload download) : IMoenvFactorSource
    {
        public Task<MoenvFactorDownload> DownloadAsync(CancellationToken cancellationToken)
            => Task.FromResult(download);
    }
}
