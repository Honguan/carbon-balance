using System.Globalization;

namespace CarbonFootprint.Application.Exports;

public static class SpreadsheetCsv
{
    public static string Encode(object? value, string? format = null)
    {
        var text = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        var numeric = value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
        // Protect the presentation only; never apply this encoding to canonical manifests.
        if (!numeric && text.Length > 0 &&
            (text[0] is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠'
                || char.IsWhiteSpace(text[0]) || char.IsControl(text[0])
                || char.GetUnicodeCategory(text[0]) == UnicodeCategory.Format))
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
