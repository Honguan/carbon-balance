using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CarbonFootprint.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CarbonFootprintDbContext>
{
    public CarbonFootprintDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CARBON_DB_CONNECTION")
            ?? "Host=localhost;Database=carbon_footprint";
        var options = new DbContextOptionsBuilder<CarbonFootprintDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new CarbonFootprintDbContext(options, new UnscopedOrganizationScope());
    }
}

