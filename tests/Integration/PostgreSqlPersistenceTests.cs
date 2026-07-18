using CarbonFootprint.Infrastructure.Persistence;
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

    private static CarbonFootprintDbContext CreateContext(Guid organizationId)
    {
        var connectionString = Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Integration test 需要 CARBON_TEST_DB_CONNECTION。");
        var options = new DbContextOptionsBuilder<CarbonFootprintDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new CarbonFootprintDbContext(options, new TestOrganizationScope(organizationId));
    }

    private sealed record TestOrganizationScope(Guid Value) : IOrganizationScope
    {
        public Guid? OrganizationId => Value;
    }
}

