param(
    [string]$TargetDatabase = "carbon_footprint_restore_upgrade",
    [string]$PreviousMigration = "20260718095107_AddOrganizationInventoryFoundation",
    [string]$Container = "carbon-footprint-postgres-1",
    [string]$DatabaseUser = "carbon_app",
    [string]$DatabasePassword = "change-this-local-password"
)

$ErrorActionPreference = "Stop"
if ($TargetDatabase -notmatch '^carbon_footprint_restore_[a-z0-9_]+$') {
    throw "TargetDatabase must start with carbon_footprint_restore_."
}
$dotnet = if (Test-Path ".dotnet/dotnet.exe") { ".dotnet/dotnet.exe" } else { "dotnet" }

docker exec $Container dropdb --username=$DatabaseUser --if-exists $TargetDatabase
if ($LASTEXITCODE -ne 0) { throw "Unable to reset upgrade rehearsal database." }
docker exec $Container createdb --username=$DatabaseUser $TargetDatabase
if ($LASTEXITCODE -ne 0) { throw "Unable to create upgrade rehearsal database." }

$connectionString = "Host=127.0.0.1;Port=5432;Database=$TargetDatabase;Username=$DatabaseUser;Password=$DatabasePassword;SSL Mode=Disable;GSS Encryption Mode=Disable"
$env:CARBON_DB_CONNECTION = $connectionString
$env:ConnectionStrings__Database = $connectionString
& $dotnet ef database update $PreviousMigration --project src/CarbonFootprint.Infrastructure --startup-project src/CarbonFootprint.Web --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Unable to build previous schema." }
& $dotnet ef database update --project src/CarbonFootprint.Infrastructure --startup-project src/CarbonFootprint.Web --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Unable to upgrade previous schema to current." }

$tableCount = docker exec $Container psql --username=$DatabaseUser --dbname=$TargetDatabase --tuples-only --no-align --command="SELECT count(*) FROM information_schema.tables WHERE table_schema IN ('app','identity','staging');"
if ($LASTEXITCODE -ne 0 -or [int]$tableCount -lt 1) {
    throw "Upgrade rehearsal validation failed."
}
Write-Output "MIGRATION_UPGRADE_REHEARSAL=PASS"
Write-Output "UPGRADED_TABLES=$tableCount"
