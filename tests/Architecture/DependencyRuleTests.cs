using CarbonFootprint.Domain.Modules.Calculations;

namespace CarbonFootprint.Architecture.Tests;

public sealed class DependencyRuleTests
{
    [Fact]
    public void Domain_DoesNotReferenceWebInfrastructureOrEntityFramework()
    {
        var references = typeof(CalculationEngine).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("CarbonFootprint.Web", references);
        Assert.DoesNotContain("CarbonFootprint.Infrastructure", references);
        Assert.DoesNotContain(references, name => name?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(references, name => name?.StartsWith("Npgsql", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RequiredDomainModules_ArePresent()
    {
        var namespaces = typeof(CalculationEngine).Assembly
            .GetTypes()
            .Select(type => type.Namespace)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        var required = new[]
        {
            "Organizations", "Products", "Standards", "Units", "Factors", "Inventories",
            "Calculations", "Evidence", "Reporting", "Audit", "LegacyImport"
        };

        foreach (var module in required)
        {
            Assert.Contains($"CarbonFootprint.Domain.Modules.{module}", namespaces);
        }
    }
}

