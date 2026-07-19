from pathlib import Path
p=Path('src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs'); t=p.read_text()
marker='    private static string NormalizeSection(string? section) =>'
helpers='''    private static bool TryResolveControlledValue(string? selected, string? other, out string value)
    {
        value = selected?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        if (!string.Equals(value, "__other__", StringComparison.Ordinal))
        {
            return true;
        }
        value = other?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryResolveOptionalControlledValue(string? selected, string? other, out string value)
    {
        value = string.Empty;
        return string.IsNullOrWhiteSpace(selected) || TryResolveControlledValue(selected, other, out value);
    }

'''
if marker not in t: raise SystemExit('NormalizeSection target not found')
p.write_text(t.replace(marker,helpers+marker,1))
