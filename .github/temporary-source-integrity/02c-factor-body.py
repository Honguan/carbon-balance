from pathlib import Path
p=Path('src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs'); t=p.read_text()
a='''        var organizationId = RequireOrganization();
        if (string.IsNullOrWhiteSpace(factorName)
            || factorValue is null or < 0m
            || string.IsNullOrWhiteSpace(sourceDatasetVersion)
            || string.IsNullOrWhiteSpace(licenseCode)
            || string.IsNullOrWhiteSpace(factorSourceName)
            || string.IsNullOrWhiteSpace(datasetName)
            || string.IsNullOrWhiteSpace(factorApplicability))
        {
            ModelState.AddModelError("factor", "係數名稱、非負數值、來源版本與授權識別皆為必填。");
'''
b='''        var organizationId = RequireOrganization();
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
'''
if a not in t: raise SystemExit('factor validation target not found')
t=t.replace(a,b,1)
a='''            Geography = "TW",
            ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = new DateOnly(2027, 12, 31),
            PublicationStatus = FactorPublicationStatus.Draft.ToString(),
            SourceDatasetVersion = sourceDatasetVersion.Trim(),
            LicenseCode = licenseCode.Trim(),
            SourceName = factorSourceName.Trim(),
            DatasetName = datasetName.Trim(),
'''
b='''            Geography = geography,
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
'''
if a not in t: raise SystemExit('factor initializer target not found')
p.write_text(t.replace(a,b,1))
