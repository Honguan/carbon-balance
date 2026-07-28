using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarbonFootprint.Application.Factors;

public sealed record MoenvFactorRecord(
    string Name,
    decimal Value,
    string DenominatorUnitCode,
    string DepartmentName,
    int? AnnouncementYear,
    string SourceRecordSha256);

public sealed record MoenvFactorDataset(
    IReadOnlyList<MoenvFactorRecord> Records,
    int SkippedCount,
    int SourceRecordCount);

public static class MoenvFactorDatasetParser
{
    public static MoenvFactorDataset Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var recordsElement = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : FindProperty(document.RootElement, "records");
        if (recordsElement is null || recordsElement.Value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("環境部係數資料缺少 records 陣列。");
        }

        var records = new List<MoenvFactorRecord>();
        var skippedCount = 0;
        foreach (var item in recordsElement.Value.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var coefficient = ReadString(item, "coe");
            var unit = NormalizeUnit(ReadString(item, "unit"));
            if (string.IsNullOrWhiteSpace(name)
                || unit is null
                || !decimal.TryParse(coefficient, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value)
                || value < 0m)
            {
                skippedCount++;
                continue;
            }

            var announcementYearText = ReadString(item, "announcementyear");
            var announcementYear = int.TryParse(announcementYearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 1900 and <= 9999
                ? year
                : (int?)null;
            records.Add(new MoenvFactorRecord(
                name.Trim(),
                value,
                unit,
                ReadString(item, "departmentname").Trim(),
                announcementYear,
                Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText())))));
        }

        return new MoenvFactorDataset(records, skippedCount, recordsElement.Value.GetArrayLength());
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string ReadString(JsonElement element, string name)
    {
        var value = FindProperty(element, name);
        return value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => string.Empty
        };
    }

    private static string? NormalizeUnit(string unit)
    {
        var normalized = unit
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace('（', '(')
            .Replace('）', ')')
            .ToLowerInvariant();
        return normalized switch
        {
            "kg" or "公斤" or "千克" or "公斤(kg)" => "kg",
            "g" or "公克" or "克" or "公克(g)" => "g",
            "tonne" or "ton" or "t" or "mt" or "公噸" or "公噸(mt)" => "tonne",
            "kwh" or "度" or "千瓦小時" or "度(kwh)" => "kWh",
            "tonne-km" or "t-km" or "tkm" or "公噸公里" or "延噸公里(tkm)" => "tonne-km",
            _ => null
        };
    }
}
