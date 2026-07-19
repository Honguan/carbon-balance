from pathlib import Path


def rep(path, old, new):
    file = Path(path)
    text = file.read_text()
    if old not in text:
        raise SystemExit(f"target not found: {path}: {old[:80]}")
    file.write_text(text.replace(old, new, 1))

rep(
    "src/CarbonFootprint.Infrastructure/Persistence/Records.cs",
    """    public string SourceName { get; set; } = string.Empty;
    public string DatasetName { get; set; } = string.Empty;
    public string Applicability { get; set; } = string.Empty;
""",
    """    public string SourceType { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string DatasetName { get; set; } = string.Empty;
    public string OriginalDocumentName { get; set; } = string.Empty;
    public string OriginalDocumentSha256 { get; set; } = string.Empty;
    public string Applicability { get; set; } = string.Empty;
""")

rep(
    "src/CarbonFootprint.Infrastructure/Persistence/CarbonFootprintDbContext.cs",
    """            entity.Property(item => item.SourceName).HasMaxLength(300);
            entity.Property(item => item.DatasetName).HasMaxLength(300);
            entity.Property(item => item.Applicability).HasMaxLength(2000);
""",
    """            entity.Property(item => item.SourceType).HasMaxLength(50);
            entity.Property(item => item.SourceName).HasMaxLength(300);
            entity.Property(item => item.SourceReference).HasMaxLength(500);
            entity.Property(item => item.DatasetName).HasMaxLength(300);
            entity.Property(item => item.OriginalDocumentName).HasMaxLength(300);
            entity.Property(item => item.OriginalDocumentSha256).HasMaxLength(64);
            entity.Property(item => item.Applicability).HasMaxLength(2000);
""")

Path("src/CarbonFootprint.Domain/Modules/Standards/SourceDocumentIntegrity.cs").write_text(
'''namespace CarbonFootprint.Domain.Modules.Standards;

public static class SourceDocumentIntegrity
{
    public static bool TryNormalizeSha256(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }
}
''')

Path("tests/Unit/SourceDocumentIntegrityTests.cs").write_text(
'''using CarbonFootprint.Domain.Modules.Standards;

namespace CarbonFootprint.Tests.Unit;

public sealed class SourceDocumentIntegrityTests
{
    [Fact]
    public void Accepts_uppercase_and_normalizes_to_lowercase()
    {
        Assert.True(SourceDocumentIntegrity.TryNormalizeSha256(new string('A', 64), out var value));
        Assert.Equal(new string('a', 64), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Rejects_invalid_values(string input)
    {
        Assert.False(SourceDocumentIntegrity.TryNormalizeSha256(input, out var value));
        Assert.Empty(value);
    }
}
''')
