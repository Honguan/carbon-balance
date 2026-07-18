param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$TargetDatabase = "carbon_footprint_restore_rehearsal",
    [string]$Container = "carbon-footprint-postgres-1",
    [string]$DatabaseUser = "carbon_app"
)

$ErrorActionPreference = "Stop"
if ($TargetDatabase -notmatch '^carbon_footprint_restore_[a-z0-9_]+$') {
    throw "TargetDatabase must start with carbon_footprint_restore_."
}

$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath).Path
docker cp $resolvedBackup "${Container}:/tmp/carbon-restore.dump"
if ($LASTEXITCODE -ne 0) {
    throw "Unable to copy backup to PostgreSQL container."
}

docker exec $Container dropdb --username=$DatabaseUser --if-exists $TargetDatabase
if ($LASTEXITCODE -ne 0) {
    throw "Unable to reset rehearsal database."
}
docker exec $Container createdb --username=$DatabaseUser $TargetDatabase
if ($LASTEXITCODE -ne 0) {
    throw "Unable to create rehearsal database."
}
docker exec $Container pg_restore --username=$DatabaseUser --dbname=$TargetDatabase --no-owner --no-acl --exit-on-error /tmp/carbon-restore.dump
if ($LASTEXITCODE -ne 0) {
    throw "Restore rehearsal failed."
}

$tableCount = docker exec $Container psql --username=$DatabaseUser --dbname=$TargetDatabase --tuples-only --no-align --command="SELECT count(*) FROM information_schema.tables WHERE table_schema IN ('app','identity','staging');"
if ($LASTEXITCODE -ne 0 -or [int]$tableCount -lt 1) {
    throw "Restored database validation failed."
}
docker exec $Container rm /tmp/carbon-restore.dump
Write-Output "RESTORE_REHEARSAL=PASS"
Write-Output "RESTORED_TABLES=$tableCount"
