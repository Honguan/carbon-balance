using System.IO.Compression;
using System.Xml;
using CarbonFootprint.Application.Exports;

namespace CarbonFootprint.Unit.Tests;

public sealed class ExcelWorkbookTests
{
    [Fact]
    public void Create_ProducesWellFormedOpenXmlPackageWithRequestedSheets()
    {
        var bytes = ExcelWorkbook.Create(
        [
            new ExcelSheet("盤查摘要", [["欄位", "內容"], ["功能單位", "1 件"]]),
            new ExcelSheet("活動數據", [["名稱", "排放量"], ["電力", 1.25m]])
        ]);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var workbook = ReadEntry(archive, "xl/workbook.xml");
        var activitySheet = ReadEntry(archive, "xl/worksheets/sheet2.xml");
        Assert.All(
            archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.Ordinal)),
            entry =>
            {
                var document = new XmlDocument();
                using var stream = entry.Open();
                document.Load(stream);
            });

        Assert.Contains("盤查摘要", workbook, StringComparison.Ordinal);
        Assert.Contains("活動數據", workbook, StringComparison.Ordinal);
        Assert.Contains("電力", activitySheet, StringComparison.Ordinal);
        Assert.Contains(">1.25<", activitySheet, StringComparison.Ordinal);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var reader = new StreamReader(archive.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }
}
