from pathlib import Path
p=Path('src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs'); t=p.read_text()
a='''            LicenseCode = current.LicenseCode,
            SourceName = current.SourceName,
            DatasetName = current.DatasetName,
            Applicability = current.Applicability,
'''
b='''            LicenseCode = current.LicenseCode,
            SourceType = current.SourceType,
            SourceName = current.SourceName,
            SourceReference = current.SourceReference,
            DatasetName = current.DatasetName,
            OriginalDocumentName = current.OriginalDocumentName,
            OriginalDocumentSha256 = current.OriginalDocumentSha256,
            Applicability = current.Applicability,
'''
if a not in t: raise SystemExit('factor supersede target not found')
p.write_text(t.replace(a,b,1))
