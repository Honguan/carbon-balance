using System.Reflection;
using CarbonFootprint.Domain.Modules.Calculations;

namespace CarbonFootprint.Web.Services;

public static class ApplicationBuildProvenance
{
    public static CalculationBuildProvenance Resolve(Assembly assembly, bool isDevelopment)
    {
        var applicationVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var sourceRevision = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "SourceRevisionId")?
            .Value;

        return CalculationBuildProvenance.Create(
            applicationVersion ?? (isDevelopment ? "dev" : null),
            sourceRevision ?? (isDevelopment ? "dev" : null),
            allowDevelopment: isDevelopment);
    }
}
