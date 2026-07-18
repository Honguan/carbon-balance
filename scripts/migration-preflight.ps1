param(
    [string]$Configuration = "Release",
    [string]$ConnectionString = $env:ConnectionStrings__Database
)

$ErrorActionPreference = "Stop"
$dotnet = if (Test-Path ".dotnet/dotnet.exe") { ".dotnet/dotnet.exe" } else { "dotnet" }
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ConnectionString or ConnectionStrings__Database is required."
}
$env:ConnectionStrings__Database = $ConnectionString
$env:CARBON_DB_CONNECTION = $ConnectionString

& $dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Tool restore failed." }
& $dotnet ef migrations has-pending-model-changes --project src/CarbonFootprint.Infrastructure --startup-project src/CarbonFootprint.Web --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "The EF model differs from committed migrations." }
& $dotnet ef migrations list --project src/CarbonFootprint.Infrastructure --startup-project src/CarbonFootprint.Web --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "Unable to inspect database migrations." }

Write-Output "MIGRATION_PREFLIGHT=PASS"
