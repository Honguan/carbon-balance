# Requires Windows, Excel, PowerShell 7 on .NET 10, and a Release build.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $root 'src/CarbonFootprint.Application/bin/Release/net10.0/CarbonFootprint.Application.dll'
Add-Type -Path $assemblyPath
$outputDirectory = Join-Path $root ('artifacts/csv-excel-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory | Out-Null
$payloads = @('=1+1', '+1+1', '-1+1', '@SUM(1,1)', "`t=1+1", "`r=1+1", "`n=1+1",
    ' =1+1', '"=1+1"', 'text,comma', ([char]0x200B + '=1+1'), ([char]0xFF1D + '1+1'))
$rows = foreach ($payload in $payloads) {
    [CarbonFootprint.Application.Exports.SpreadsheetCsv]::Encode($payload) + ',' +
        [CarbonFootprint.Application.Exports.SpreadsheetCsv]::Encode([decimal]-12.5)
}
$safePath = Join-Path $outputDirectory 'safe.csv'
$controlPath = Join-Path $outputDirectory 'control.csv'
[IO.File]::WriteAllText($safePath, ($rows -join "`r`n"), [Text.UTF8Encoding]::new($false))
# Harmless arithmetic proves Excel is actually interpreting formulas, not forcing every cell to text.
[IO.File]::WriteAllText($controlPath, '=1+1', [Text.UTF8Encoding]::new($false))

$excel = $null
$ownsInstance = $false
$safe = $null
$control = $null
try {
    $excel = New-Object -ComObject Excel.Application
    if ($excel.Workbooks.Count -ne 0) { throw 'Refusing to use an Excel instance with existing workbooks.' }
    $ownsInstance = $true
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    $excel.AutomationSecurity = 3
    $control = $excel.Workbooks.Open($controlPath, 0, $true)
    if (-not $control.Worksheets.Item(1).Cells.Item(1, 1).HasFormula -or
        $control.Worksheets.Item(1).Cells.Item(1, 1).Value2 -ne 2) {
        throw 'The positive control was not evaluated; this run cannot prove spreadsheet safety.'
    }
    $safe = $excel.Workbooks.Open($safePath, 0, $true)
    $sheet = $safe.Worksheets.Item(1)
    if ($sheet.UsedRange.Rows.Count -ne $payloads.Count -or $sheet.UsedRange.Columns.Count -ne 2) {
        throw 'CSV dimensions changed during import.'
    }
    for ($row = 1; $row -le $payloads.Count; $row++) {
        if ($sheet.Cells.Item($row, 1).HasFormula) { throw "Untrusted text became a formula at row $row." }
        $number = $sheet.Cells.Item($row, 2).Value2
        if ($number -isnot [double] -or $number -ne -12.5) { throw "Numeric value changed at row $row." }
    }
    Write-Output "EXCEL_CSV=PASS; Excel=$($excel.Version); payloads=$($payloads.Count); formulas=0; numbers preserved; positive control=2"
}
finally {
    if ($safe) { $safe.Close($false) }
    if ($control) { $control.Close($false) }
    if ($ownsInstance) { $excel.Quit() }
    if ($excel) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel) | Out-Null }
}
