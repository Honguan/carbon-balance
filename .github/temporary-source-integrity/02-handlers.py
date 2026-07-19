from pathlib import Path

path = Path("src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs")
text = path.read_text()


def rep(old, new):
    global text
    if old not in text:
        raise SystemExit(f"handler target not found: {old[:100]}")
    text = text.replace(old, new, 1)

rep(
'''        if (string.IsNullOrWhiteSpace(registrationNumber)
            || versionNumber < 1
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(sourceReference)
            || string.IsNullOrWhiteSpace(standardCode)
            || string.IsNullOrWhiteSpace(cccClassification)
            || string.IsNullOrWhiteSpace(pcrApplicability)
            || string.IsNullOrWhiteSpace(ruleRequirements)
            || originalDocumentSha256.Length != 64
            || validFrom > validTo)
''',
'''        var validSha = SourceDocumentIntegrity.TryNormalizeSha256(originalDocumentSha256, out var normalizedSha);
        if (string.IsNullOrWhiteSpace(registrationNumber)
            || versionNumber < 1
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(sourceReference)
            || string.IsNullOrWhiteSpace(standardCode)
            || string.IsNullOrWhiteSpace(cccClassification)
            || string.IsNullOrWhiteSpace(pcrApplicability)
            || string.IsNullOrWhiteSpace(ruleRequirements)
            || string.IsNullOrWhiteSpace(originalDocumentName)
            || !validSha
            || validFrom > validTo)
''')
rep('OriginalDocumentSha256 = originalDocumentSha256.Trim().ToLowerInvariant(),',
    'OriginalDocumentSha256 = normalizedSha,')

rep(
'''        string denominatorUnitCode,
        string sourceDatasetVersion,
        string licenseCode,
        string factorSourceName,
        string datasetName,
        string factorApplicability,
''',
'''        string denominatorUnitCode,
        string factorSourceType,
        string factorSourceTypeOther,
        string factorGeography,
        string factorGeographyOther,
        DateOnly? factorValidFrom,
        DateOnly? factorValidTo,
        string sourceDatasetVersion,
        string licenseCode,
        string factorSourceName,
        string factorSourceReference,
        string datasetName,
        string factorOriginalDocumentName,
        string factorOriginalDocumentSha256,
        string factorApplicability,
''')
rep(
'''        var organizationId = RequireOrganization();
        if (string.IsNullOrWhiteSpace(factorName)
            || factorValue is null or < 0m
            || string.IsNullOrWhiteSpace(sourceDatasetVersion)
            || string.IsNullOrWhiteSpace(licenseCode)
            || string.IsNullOrWhiteSpace(factorSourceName)
            || string.IsNullOrWhiteSpace(datasetName)
            || string.IsNullOrWhiteSpace(factorApplicability))
        {
            ModelState.AddModelError("factor", "係數名稱、非負數值、來源版本與授權識別皆為必填。");
''',
'''        var organizationId = RequireOrganization();
        var hasSourceType = TryResolveControlledValue(factorSourceType, factorSourceTypeOther, out var sourceType);
        var hasGeography = TryResolveControlledValue(factorGeography, factorGeographyOther, out var geography);
        var validSha = SourceDocumentIntegrity.TryNormalizeSha256(factorOriginalDocumentSha256, out var sourceSha);
        if (string.IsNullOrWhiteSpace(factorName)
            || factorValue is null or < 0m
            || !hasSourceType
            || !hasGeography
            || factorValidFrom > factorValidTo
            || string.IsNullOrWhiteSpace(sourceDatasetVersion)
            || string.IsNullOrWhiteSpace(licenseCode)
            || string.IsNullOrWhiteSpace(factorSourceName)
            || string.IsNullOrWhiteSpace(factorSourceReference)
            || string.IsNullOrWhiteSpace(datasetName)
            || string.IsNullOrWhiteSpace(factorOriginalDocumentName)
            || !validSha
            || string.IsNullOrWhiteSpace(factorApplicability))
        {
            ModelState.AddModelError("factor", "來源類型、地域、有效期間、原始文件與 SHA-256 皆為必填。");
''')
rep(
'''            Geography = "TW",
            ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = new DateOnly(2027, 12, 31),
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = sourceDatasetVersion.Trim(),
            LicenseCode = licenseCode.Trim(),
            SourceName = factorSourceName.Trim(),
            DatasetName = datasetName.Trim(),
''',
'''            Geography = geography,
            ValidFrom = factorValidFrom,
            ValidTo = factorValidTo,
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = sourceDatasetVersion.Trim(),
            LicenseCode = licenseCode.Trim(),
            SourceType = sourceType,
            SourceName = factorSourceName.Trim(),
            SourceReference = factorSourceReference.Trim(),
            DatasetName = datasetName.Trim(),
            OriginalDocumentName = factorOriginalDocumentName.Trim(),
            OriginalDocumentSha256 = sourceSha,
''')
rep(
'''            LicenseCode = current.LicenseCode,
            SourceName = current.SourceName,
            DatasetName = current.DatasetName,
            Applicability = current.Applicability,
''',
'''            LicenseCode = current.LicenseCode,
            SourceType = current.SourceType,
            SourceName = current.SourceName,
            SourceReference = current.SourceReference,
            DatasetName = current.DatasetName,
            OriginalDocumentName = current.OriginalDocumentName,
            OriginalDocumentSha256 = current.OriginalDocumentSha256,
            Applicability = current.Applicability,
''')

marker = '''    private static string NormalizeSection(string? section)
'''
helpers = '''    private static bool TryResolveControlledValue(string? selected, string? other, out string value)
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
if marker not in text:
    raise SystemExit("NormalizeSection marker not found")
text = text.replace(marker, helpers + marker, 1)
path.write_text(text)
