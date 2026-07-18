param(
    [string]$OutputDirectory = "backups",
    [string]$Database = "carbon_footprint",
    [string]$Container = "carbon-footprint-postgres-1",
    [string]$DatabaseUser = "carbon_app"
)

$ErrorActionPreference = "Stop"
if ($Database -notmatch '^[a-zA-Z0-9_]+$') {
    throw "Database name contains unsupported characters."
}

$resolvedOutput = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$backupPath = Join-Path $resolvedOutput "$Database-$timestamp.dump"

docker exec $Container pg_dump --username=$DatabaseUser --dbname=$Database --format=custom --no-owner --no-acl --file=/tmp/carbon-backup.dump
if ($LASTEXITCODE -ne 0) {
    throw "pg_dump failed."
}
docker cp "${Container}:/tmp/carbon-backup.dump" $backupPath
if ($LASTEXITCODE -ne 0) {
    throw "Unable to copy backup from PostgreSQL container."
}
docker exec $Container rm /tmp/carbon-backup.dump
if ($LASTEXITCODE -ne 0) {
    throw "Unable to remove the temporary container backup."
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $backupPath).Hash.ToLowerInvariant()
Write-Output "BACKUP_PATH=$backupPath"
Write-Output "BACKUP_SHA256=$hash"
