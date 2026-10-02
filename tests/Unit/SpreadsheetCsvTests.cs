using System.Globalization;
using CarbonFootprint.Application.Exports;

namespace CarbonFootprint.Unit.Tests;

public sealed class SpreadsheetCsvTests
{
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    [InlineData("\n=1+1")]
    [InlineData(" \t\r\n=1+1")]
    [InlineData("\0=1+1")]
    [InlineData("\u00a0=1+1")]
    [InlineData("\ufeff=1+1")]
    [InlineData("\u200b=1+1")]
    [InlineData("\t中文")]
    [InlineData("＝1+1")]
    [InlineData("＋1+1")]
    [InlineData("－1+1")]
    [InlineData("＠SUM(1,2)")]
    [InlineData("=HYPERLINK(\"https://example.test\",\"中文\")")]
    public void Encode_NeutralizesUntrustedSpreadsheetPrefixes(string value)
    {
        Assert.Equal($"\"'{value.Replace("\"", "\"\"")}\"", SpreadsheetCsv.Encode(value));
    }

    [Theory]
    [InlineData(null, "\"\"")]
    [InlineData("", "\"\"")]
    [InlineData("中文, \"測試\"", "\"中文, \"\"測試\"\"\"")]
    [InlineData("第一行\r\n第二行", "\"第一行\r\n第二行\"")]
    [InlineData("正常=1+1", "\"正常=1+1\"")]
    [InlineData("'-1", "\"'-1\"")]
    public void Encode_PreservesTextAndCsvQuoting(string? value, string expected)
    {
        Assert.Equal(expected, SpreadsheetCsv.Encode(value));
    }

    [Fact]
    public void Encode_KeepsTypedNumbersNumericAndUsesInvariantFormatting()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("\"-1.25\"", SpreadsheetCsv.Encode(-1.25m));
            Assert.Equal("\"-1.250000\"", SpreadsheetCsv.Encode(-1.25m, "F6"));
            Assert.Equal("\"-12\"", SpreadsheetCsv.Encode(-12));
            Assert.Equal("\"0\"", SpreadsheetCsv.Encode(0L));
            Assert.Equal("\"'-1.25\"", SpreadsheetCsv.Encode("-1.25"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
