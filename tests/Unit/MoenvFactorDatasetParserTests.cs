using CarbonFootprint.Application.Factors;

namespace CarbonFootprint.Unit.Tests;

public sealed class MoenvFactorDatasetParserTests
{
    [Fact]
    public void Parse_OfficialRecords_ReturnsControlledUnitsAndSkippedCount()
    {
        const string json =
            """
            {
              "records": [
                { "name": "電力", "coe": "0.494", "unit": "kWh", "departmentname": "環境部", "announcementyear": "2025" },
                { "name": "原料", "coe": "1.25", "unit": "公斤", "departmentname": "", "announcementyear": "2024" },
                { "name": "未知", "coe": "2", "unit": "箱", "departmentname": "環境部", "announcementyear": "2024" }
              ]
            }
            """;

        var result = MoenvFactorDatasetParser.Parse(json);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("kWh", result.Records[0].DenominatorUnitCode);
        Assert.Equal("kg", result.Records[1].DenominatorUnitCode);
        Assert.Equal(0.494m, result.Records[0].Value);
        Assert.Equal(2025, result.Records[0].AnnouncementYear);
        Assert.All(result.Records, item => Assert.Matches("^[0-9a-f]{64}$", item.SourceRecordSha256));
    }

    [Fact]
    public void Parse_InvalidCoefficient_IsSkipped()
    {
        const string json = """{"records":[{"name":"無效","coe":"非數值","unit":"kg"}]}""";

        var result = MoenvFactorDatasetParser.Parse(json);

        Assert.Empty(result.Records);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void Parse_CurrentOfficialArrayResponse_ImportsCompoundDeclaredUnit()
    {
        const string json =
            """
            [
              {
                "name": "合金鋼鋼胚（機械五金用）",
                "coe": "0.5661",
                "unit": "公斤(kg)",
                "departmentname": "環境部氣候變遷署",
                "announcementyear": "2025"
              }
            ]
            """;

        var result = MoenvFactorDatasetParser.Parse(json);

        var factor = Assert.Single(result.Records);
        Assert.Equal("kg", factor.DenominatorUnitCode);
        Assert.Equal(0.5661m, factor.Value);
    }

    [Theory]
    [InlineData("公斤(kg)", "kg")]
    [InlineData("公克(g)", "g")]
    [InlineData("公噸(mt)", "tonne")]
    [InlineData("度(kwh)", "kWh")]
    [InlineData("延噸公里(tkm)", "tonne-km")]
    public void Parse_CurrentOfficialCompoundUnits_MapsToControlledUnit(
        string sourceUnit,
        string expectedUnit)
    {
        var json = $$"""[{"name":"係數","coe":"1","unit":"{{sourceUnit}}"}]""";

        var result = MoenvFactorDatasetParser.Parse(json);

        Assert.Equal(expectedUnit, Assert.Single(result.Records).DenominatorUnitCode);
    }
}
