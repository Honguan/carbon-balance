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
}
