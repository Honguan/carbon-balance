param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'
$env:ConnectionStrings__Database = $ConnectionString
dotnet run --project src/CarbonFootprint.Web --configuration Release --no-build -- --migrate
if ($LASTEXITCODE -ne 0) {
    throw "Migration 失敗，exit code: $LASTEXITCODE"
}

