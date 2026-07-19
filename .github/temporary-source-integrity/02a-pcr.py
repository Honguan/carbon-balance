from pathlib import Path
p=Path('src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs'); t=p.read_text()
a='''        if (string.IsNullOrWhiteSpace(registrationNumber)
            || versionNumber < 1
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(sourceReference)
            || string.IsNullOrWhiteSpace(standardCode)
            || string.IsNullOrWhiteSpace(cccClassification)
            || string.IsNullOrWhiteSpace(pcrApplicability)
            || string.IsNullOrWhiteSpace(ruleRequirements)
            || originalDocumentSha256.Length != 64
            || validFrom > validTo)
'''
b='''        var validSha = SourceDocumentIntegrity.TryNormalizeSha256(originalDocumentSha256, out var normalizedSha);
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
'''
if a not in t: raise SystemExit('PCR validation target not found')
t=t.replace(a,b,1)
a='OriginalDocumentSha256 = originalDocumentSha256.Trim().ToLowerInvariant(),'
if a not in t: raise SystemExit('PCR SHA assignment target not found')
p.write_text(t.replace(a,'OriginalDocumentSha256 = normalizedSha,',1))
