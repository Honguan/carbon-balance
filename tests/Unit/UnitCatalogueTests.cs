using CarbonFootprint.Domain.Modules.Units;

namespace CarbonFootprint.Unit.Tests;

public sealed class UnitCatalogueTests
{
    [Fact]
    public void Convert_GramsToKilograms_PreservesDecimalPrecision()
    {
        var catalogue = new UnitCatalogue(
            "units-1",
            [
                new UnitDefinition(Guid.NewGuid(), "kg", "mass", 1m, 0m, "kg", "units-1"),
                new UnitDefinition(Guid.NewGuid(), "g", "mass", 0.001m, 0m, "kg", "units-1")
            ]);

        var result = catalogue.Convert(1234.567890123m, "g", "kg");

        Assert.Equal(1.234567890123m, result);
    }

    [Fact]
    public void Convert_AcrossDimensions_IsRejected()
    {
        var catalogue = new UnitCatalogue(
            "units-1",
            [
                new UnitDefinition(Guid.NewGuid(), "kg", "mass", 1m, 0m, "kg", "units-1"),
                new UnitDefinition(Guid.NewGuid(), "kWh", "energy", 1m, 0m, "kWh", "units-1")
            ]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => catalogue.Convert(1m, "kg", "kWh"));

        Assert.Contains("不可將 mass 換算為 energy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_AliasAndCompositeUnit_UsesVersionedDefinition()
    {
        var tonneKilometre = new UnitDefinition(
            Guid.NewGuid(),
            "tonne-km",
            "transport-work",
            1m,
            0m,
            "tonne-km",
            "units-2",
            ["t-km", "tkm"],
            "tonne*km");
        var catalogue = new UnitCatalogue("units-2", [tonneKilometre]);

        Assert.Equal(12.5m, catalogue.Convert(12.5m, "tkm", "tonne-km"));
        Assert.Equal("tonne*km", catalogue.Get("t-km").CompositeExpression);
        Assert.Equal("units-2", catalogue.Version);
    }
}
