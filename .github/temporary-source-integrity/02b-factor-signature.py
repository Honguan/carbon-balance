from pathlib import Path
p=Path('src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs'); t=p.read_text()
a='''        string denominatorUnitCode,
        string sourceDatasetVersion,
        string licenseCode,
        string factorSourceName,
        string datasetName,
        string factorApplicability,
'''
b='''        string denominatorUnitCode,
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
'''
if a not in t: raise SystemExit('factor signature target not found')
p.write_text(t.replace(a,b,1))
