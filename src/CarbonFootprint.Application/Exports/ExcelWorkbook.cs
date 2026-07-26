using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CarbonFootprint.Application.Exports;

public sealed record ExcelSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows);

public static class ExcelWorkbook
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Create(IReadOnlyList<ExcelSheet> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new InvalidOperationException("Excel 至少需要一個工作表。");
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteContentTypes(archive, sheets.Count);
            WriteRootRelationships(archive);
            WriteWorkbook(archive, sheets);
            WriteWorkbookRelationships(archive, sheets.Count);
            WriteStyles(archive);
            for (var index = 0; index < sheets.Count; index++)
            {
                WriteWorksheet(archive, index + 1, sheets[index]);
            }
        }

        return stream.ToArray();
    }

    private static void WriteContentTypes(ZipArchive archive, int sheetCount) =>
        WriteXml(archive, "[Content_Types].xml", writer =>
        {
            writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
            writer.WriteStartElement("Default");
            writer.WriteAttributeString("Extension", "rels");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
            writer.WriteEndElement();
            writer.WriteStartElement("Default");
            writer.WriteAttributeString("Extension", "xml");
            writer.WriteAttributeString("ContentType", "application/xml");
            writer.WriteEndElement();
            writer.WriteStartElement("Override");
            writer.WriteAttributeString("PartName", "/xl/workbook.xml");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            writer.WriteEndElement();
            writer.WriteStartElement("Override");
            writer.WriteAttributeString("PartName", "/xl/styles.xml");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            writer.WriteEndElement();
            for (var index = 1; index <= sheetCount; index++)
            {
                writer.WriteStartElement("Override");
                writer.WriteAttributeString("PartName", $"/xl/worksheets/sheet{index}.xml");
                writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        });

    private static void WriteRootRelationships(ZipArchive archive) =>
        WriteXml(archive, "_rels/.rels", writer =>
        {
            writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
            writer.WriteStartElement("Relationship");
            writer.WriteAttributeString("Id", "rId1");
            writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
            writer.WriteAttributeString("Target", "xl/workbook.xml");
            writer.WriteEndElement();
            writer.WriteEndElement();
        });

    private static void WriteWorkbook(ZipArchive archive, IReadOnlyList<ExcelSheet> sheets) =>
        WriteXml(archive, "xl/workbook.xml", writer =>
        {
            writer.WriteStartElement("workbook", SpreadsheetNamespace);
            writer.WriteAttributeString("xmlns", "r", null, OfficeRelationshipNamespace);
            writer.WriteStartElement("sheets", SpreadsheetNamespace);
            for (var index = 0; index < sheets.Count; index++)
            {
                writer.WriteStartElement("sheet", SpreadsheetNamespace);
                writer.WriteAttributeString("name", NormalizeSheetName(sheets[index].Name, index + 1));
                writer.WriteAttributeString("sheetId", (index + 1).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("r", "id", OfficeRelationshipNamespace, $"rId{index + 1}");
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        });

    private static void WriteWorkbookRelationships(ZipArchive archive, int sheetCount) =>
        WriteXml(archive, "xl/_rels/workbook.xml.rels", writer =>
        {
            writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
            for (var index = 1; index <= sheetCount; index++)
            {
                writer.WriteStartElement("Relationship");
                writer.WriteAttributeString("Id", $"rId{index}");
                writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
                writer.WriteAttributeString("Target", $"worksheets/sheet{index}.xml");
                writer.WriteEndElement();
            }

            writer.WriteStartElement("Relationship");
            writer.WriteAttributeString("Id", $"rId{sheetCount + 1}");
            writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles");
            writer.WriteAttributeString("Target", "styles.xml");
            writer.WriteEndElement();
            writer.WriteEndElement();
        });

    private static void WriteStyles(ZipArchive archive) =>
        WriteXml(archive, "xl/styles.xml", writer =>
        {
            writer.WriteStartElement("styleSheet", SpreadsheetNamespace);
            writer.WriteStartElement("fonts", SpreadsheetNamespace);
            writer.WriteAttributeString("count", "2");
            writer.WriteStartElement("font", SpreadsheetNamespace);
            writer.WriteEndElement();
            writer.WriteStartElement("font", SpreadsheetNamespace);
            writer.WriteElementString("b", SpreadsheetNamespace, string.Empty);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("fills", SpreadsheetNamespace);
            writer.WriteAttributeString("count", "2");
            writer.WriteStartElement("fill", SpreadsheetNamespace);
            writer.WriteStartElement("patternFill", SpreadsheetNamespace);
            writer.WriteAttributeString("patternType", "none");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("fill", SpreadsheetNamespace);
            writer.WriteStartElement("patternFill", SpreadsheetNamespace);
            writer.WriteAttributeString("patternType", "gray125");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("borders", SpreadsheetNamespace);
            writer.WriteAttributeString("count", "1");
            writer.WriteStartElement("border", SpreadsheetNamespace);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("cellStyleXfs", SpreadsheetNamespace);
            writer.WriteAttributeString("count", "1");
            writer.WriteStartElement("xf", SpreadsheetNamespace);
            writer.WriteAttributeString("numFmtId", "0");
            writer.WriteAttributeString("fontId", "0");
            writer.WriteAttributeString("fillId", "0");
            writer.WriteAttributeString("borderId", "0");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("cellXfs", SpreadsheetNamespace);
            writer.WriteAttributeString("count", "2");
            WriteCellStyle(writer, fontId: 0);
            WriteCellStyle(writer, fontId: 1);
            writer.WriteEndElement();
            writer.WriteStartElement("cellStyles", SpreadsheetNamespace);
            writer.WriteAttributeString("count", "1");
            writer.WriteStartElement("cellStyle", SpreadsheetNamespace);
            writer.WriteAttributeString("name", "Normal");
            writer.WriteAttributeString("xfId", "0");
            writer.WriteAttributeString("builtinId", "0");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        });

    private static void WriteCellStyle(XmlWriter writer, int fontId)
    {
        writer.WriteStartElement("xf", SpreadsheetNamespace);
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fillId", "0");
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteEndElement();
    }

    private static void WriteWorksheet(ZipArchive archive, int sheetIndex, ExcelSheet sheet) =>
        WriteXml(archive, $"xl/worksheets/sheet{sheetIndex}.xml", writer =>
        {
            writer.WriteStartElement("worksheet", SpreadsheetNamespace);
            writer.WriteStartElement("sheetViews", SpreadsheetNamespace);
            writer.WriteStartElement("sheetView", SpreadsheetNamespace);
            writer.WriteAttributeString("workbookViewId", "0");
            writer.WriteStartElement("pane", SpreadsheetNamespace);
            writer.WriteAttributeString("ySplit", "1");
            writer.WriteAttributeString("topLeftCell", "A2");
            writer.WriteAttributeString("activePane", "bottomLeft");
            writer.WriteAttributeString("state", "frozen");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("sheetData", SpreadsheetNamespace);
            for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
            {
                writer.WriteStartElement("row", SpreadsheetNamespace);
                writer.WriteAttributeString("r", (rowIndex + 1).ToString(CultureInfo.InvariantCulture));
                var row = sheet.Rows[rowIndex];
                for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
                {
                    WriteCell(writer, rowIndex + 1, columnIndex + 1, row[columnIndex], rowIndex == 0);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        });

    private static void WriteCell(XmlWriter writer, int row, int column, object? value, bool header)
    {
        writer.WriteStartElement("c", SpreadsheetNamespace);
        writer.WriteAttributeString("r", $"{GetColumnName(column)}{row}");
        if (header)
        {
            writer.WriteAttributeString("s", "1");
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            writer.WriteElementString("v", SpreadsheetNamespace, Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is", SpreadsheetNamespace);
            writer.WriteStartElement("t", SpreadsheetNamespace);
            writer.WriteAttributeString("xml", "space", null, "preserve");
            writer.WriteString(value?.ToString() ?? string.Empty);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteXml(ZipArchive archive, string path, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var writer = XmlWriter.Create(entryStream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            CloseOutput = false
        });
        write(writer);
    }

    private static string NormalizeSheetName(string name, int index)
    {
        var invalidCharacters = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var normalized = invalidCharacters.Aggregate(name, (current, character) => current.Replace(character, '－'));
        return string.IsNullOrWhiteSpace(normalized) ? $"工作表{index}" : normalized[..Math.Min(normalized.Length, 31)];
    }

    private static string GetColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }
}
