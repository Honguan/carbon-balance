[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][uri]$SourceEndpoint,
    [Parameter(Mandatory = $true)][uri]$TargetEndpoint,
    [ValidatePattern('^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$')][string]$SourceBucket = 'carbon-evidence',
    [ValidatePattern('^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$')][string]$TargetBucket = 'carbon-evidence',
    [string]$Network = 'bridge',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
foreach ($endpoint in @($SourceEndpoint, $TargetEndpoint)) {
    if (-not $endpoint.IsAbsoluteUri -or $endpoint.Scheme -notin @('http', 'https') -or $endpoint.UserInfo) {
        throw 'Endpoints must be absolute HTTP(S) URLs without embedded credentials.'
    }
}
if ($SourceEndpoint -eq $TargetEndpoint -and $SourceBucket -eq $TargetBucket) {
    throw 'Source and target must be different storage locations.'
}

$credentialNames = @(
    'RCLONE_CONFIG_SOURCE_ACCESS_KEY_ID', 'RCLONE_CONFIG_SOURCE_SECRET_ACCESS_KEY',
    'RCLONE_CONFIG_TARGET_ACCESS_KEY_ID', 'RCLONE_CONFIG_TARGET_SECRET_ACCESS_KEY'
)
foreach ($name in $credentialNames) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Missing environment variable: $name"
    }
}

$dockerArguments = @('run', '--rm', '--network', $Network)
foreach ($name in $credentialNames) {
    $dockerArguments += @('--env', $name)
}
$dockerArguments += @(
    '--env', 'RCLONE_CONFIG_SOURCE_TYPE=s3', '--env', 'RCLONE_CONFIG_SOURCE_PROVIDER=Other',
    '--env', "RCLONE_CONFIG_SOURCE_ENDPOINT=$($SourceEndpoint.AbsoluteUri.TrimEnd('/'))",
    '--env', 'RCLONE_CONFIG_TARGET_TYPE=s3', '--env', 'RCLONE_CONFIG_TARGET_PROVIDER=Other',
    '--env', "RCLONE_CONFIG_TARGET_ENDPOINT=$($TargetEndpoint.AbsoluteUri.TrimEnd('/'))",
    'rclone/rclone:beta@sha256:75f55eab503b6f7fd8e48559c8459cc652e6911f6d6d2eed9db4f156f5e71c1c'
)
$sourceSize = & docker @dockerArguments size "source:$SourceBucket" --json
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect source objects; migration was not started.'
}
$sourceCount = ($sourceSize | ConvertFrom-Json).count
if ($sourceCount -eq 0) {
    throw 'Source bucket is empty. Verify the endpoint and database evidence references before any storage switch.'
}
$copyArguments = @('copy', "source:$SourceBucket", "target:$TargetBucket", '--immutable', '--checksum')
if (-not $Apply) {
    $copyArguments += '--dry-run'
}
& docker @dockerArguments @copyArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Object copy failed. Source objects were not deleted; do not switch the application endpoint.'
}
if (-not $Apply) {
    Write-Output 'PREVIEW_ONLY: no objects copied. Stop application writes and rerun with -Apply to copy and verify.'
    return
}

& docker @dockerArguments check "source:$SourceBucket" "target:$TargetBucket" --download --one-way
if ($LASTEXITCODE -ne 0) {
    throw 'Byte verification failed. Keep the application on the source endpoint and investigate.'
}
Write-Output "OBJECT_STORAGE_MIGRATION=PASS; objects=$sourceCount; source retained; database and application settings unchanged."
