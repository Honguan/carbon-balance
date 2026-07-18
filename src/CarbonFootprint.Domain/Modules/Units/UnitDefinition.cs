namespace CarbonFootprint.Domain.Modules.Units;

public sealed record UnitDefinition(
    Guid Id,
    string Code,
    string Dimension,
    decimal ScaleToCanonical,
    decimal OffsetToCanonical,
    string CanonicalCode,
    string CatalogueVersion,
    IReadOnlyCollection<string>? Aliases = null,
    string? CompositeExpression = null)
{
    public decimal ToCanonical(decimal value) => (value * ScaleToCanonical) + OffsetToCanonical;
}

public sealed class UnitCatalogue
{
    private readonly IReadOnlyDictionary<string, UnitDefinition> _units;

    public UnitCatalogue(string version, IEnumerable<UnitDefinition> units)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("單位目錄版本不可為空。", nameof(version));
        }

        Version = version;
        var indexedUnits = new Dictionary<string, UnitDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units)
        {
            AddCode(indexedUnits, unit.Code, unit);
            foreach (var alias in unit.Aliases ?? [])
            {
                AddCode(indexedUnits, alias, unit);
            }
        }
        _units = indexedUnits;
    }

    public string Version { get; }

    private static void AddCode(IDictionary<string, UnitDefinition> units, string code, UnitDefinition unit)
    {
        if (string.IsNullOrWhiteSpace(code) || !units.TryAdd(code.Trim(), unit))
        {
            throw new InvalidOperationException($"單位代碼或別名不可空白或重複：{code}");
        }
    }

    public UnitDefinition Get(string code) => _units.TryGetValue(code, out var unit)
        ? unit
        : throw new InvalidOperationException($"找不到受控單位：{code}。");

    public decimal Convert(decimal value, string fromCode, string toCode)
    {
        var from = Get(fromCode);
        var to = Get(toCode);

        if (!string.Equals(from.Dimension, to.Dimension, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"不可將 {from.Dimension} 換算為 {to.Dimension}。");
        }

        if (!string.Equals(from.CanonicalCode, to.CanonicalCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("單位未使用相同 canonical reference。");
        }

        var canonical = from.ToCanonical(value);
        if (to.ScaleToCanonical == 0m)
        {
            throw new InvalidOperationException("目標單位換算比例不可為 0。");
        }

        return (canonical - to.OffsetToCanonical) / to.ScaleToCanonical;
    }
}
