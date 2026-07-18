using System.Text.Json;
using CarbonFootprint.Infrastructure.LegacyImport;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var arguments = ParseArguments(args);
if (!arguments.TryGetValue("organization", out var organizationText)
    || !Guid.TryParse(organizationText, out var organizationId)
    || !arguments.TryGetValue("input", out var inputPath))
{
    Console.Error.WriteLine("Usage: --organization <guid> --input <factor.csv>");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("CARBON_MIGRATION_DB_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("CARBON_MIGRATION_DB_CONNECTION is required.");
    return 2;
}

try
{
    var options = new DbContextOptionsBuilder<CarbonFootprintDbContext>()
        .UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention()
        .Options;
    await using var dbContext = new CarbonFootprintDbContext(options, new ExplicitOrganizationScope(organizationId));
    if (!await dbContext.Organizations.AnyAsync(item => item.Id == organizationId))
    {
        Console.Error.WriteLine("Organization was not found in the scoped database.");
        return 3;
    }

    var report = await new LegacyFactorCsvImporter(dbContext).ImportAsync(
        organizationId,
        Path.GetFullPath(inputPath),
        CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return report.InvalidRows == 0 && report.ConflictRows == 0 ? 0 : 4;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static Dictionary<string, string> ParseArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < values.Length - 1; index += 2)
    {
        if (!values[index].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        result[values[index][2..]] = values[index + 1];
    }

    return result;
}

file sealed record ExplicitOrganizationScope(Guid Value) : IOrganizationScope
{
    public Guid? OrganizationId => Value;
}
