[CmdletBinding()]
param(
    [switch]$Manual,
    [switch]$NoStart,
    [switch]$ResetData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repositoryRoot '.env'
$postgresVolumeName = 'carbon-footprint_postgres-data'

function ConvertTo-PlainText([Security.SecureString]$SecureString) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Assert-LocalPassword([string]$Password, [string]$Name) {
    if ($Password -notmatch '^[A-Za-z0-9._-]{16,128}$') {
        throw "$Name must be 16-128 characters and contain only letters, numbers, dots, underscores, or hyphens."
    }
}

function Read-LocalPassword([string]$Label) {
    while ($true) {
        $first = ConvertTo-PlainText (Read-Host "$Label (input is hidden)" -AsSecureString)
        $second = ConvertTo-PlainText (Read-Host "Enter $Label again" -AsSecureString)

        if ($first -ne $second) {
            Write-Warning 'The two values do not match. Try again.'
            continue
        }

        try {
            Assert-LocalPassword -Password $first -Name $Label
            return $first
        }
        catch {
            Write-Warning $_.Exception.Message
        }
    }
}

function New-LocalPassword {
    $value = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    return $value.Substring(0, 48)
}

function Get-EnvValue([string]$Name) {
    if (-not (Test-Path -LiteralPath $envPath)) {
        return $null
    }

    $escapedName = [Regex]::Escape($Name)
    $matchingLine = Get-Content -LiteralPath $envPath |
        Where-Object { $_ -match "^$escapedName=" } |
        Select-Object -Last 1

    if ($null -eq $matchingLine) {
        return $null
    }

    return ($matchingLine -replace "^$escapedName=", '')
}

function Invoke-Compose([string[]]$Arguments) {
    & docker compose @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if ($NoStart -and $ResetData) {
    throw '-NoStart and -ResetData cannot be used together.'
}

Push-Location $repositoryRoot
try {
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop or Docker Engine first.'
    }

    & docker compose version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Compose was not found. Confirm that docker compose works.'
    }

    & docker volume inspect $postgresVolumeName *> $null
    $hasExistingPostgresVolume = $LASTEXITCODE -eq 0

    $createdEnv = $false
    if (-not (Test-Path -LiteralPath $envPath)) {
        if ($hasExistingPostgresVolume -and -not $ResetData) {
            throw @"
An existing PostgreSQL data volume was found, but .env is missing.
Generating a new password would not unlock the existing database.
Restore the original .env, or delete disposable local data with:
  .\scripts\setup-local.ps1 -ResetData
"@
        }

        if ($Manual) {
            $postgresPassword = Read-LocalPassword 'PostgreSQL password'
            $minioPassword = Read-LocalPassword 'MinIO password'
        }
        else {
            $postgresPassword = New-LocalPassword
            $minioPassword = New-LocalPassword
        }

        if ($postgresPassword -eq $minioPassword) {
            throw 'PostgreSQL and MinIO must use different passwords.'
        }

        $content = @"
ASPNETCORE_ENVIRONMENT=Development
POSTGRES_DB=carbon_footprint
POSTGRES_USER=carbon_app
POSTGRES_PASSWORD=$postgresPassword
MINIO_ROOT_USER=carbon_minio
MINIO_ROOT_PASSWORD=$minioPassword
OBJECTSTORAGE__ENDPOINT=http://minio:9000
OBJECTSTORAGE__BUCKET=carbon-evidence
MAIL__HOST=mailpit
MAIL__PORT=1025
"@

        [IO.File]::WriteAllText(
            $envPath,
            $content,
            [Text.UTF8Encoding]::new($false))
        $createdEnv = $true

        Write-Host ''
        Write-Host 'Created .env with these local credentials:' -ForegroundColor Green
        Write-Host '  PostgreSQL user: carbon_app'
        Write-Host "  PostgreSQL password: $postgresPassword"
        Write-Host '  MinIO user: carbon_minio'
        Write-Host "  MinIO password: $minioPassword"
        Write-Host "  Settings file: $envPath"
        Write-Warning 'Keep .env private. Do not commit it, paste it into chat, or share it.'
    }
    else {
        Write-Host "Found existing .env. Existing passwords will be preserved: $envPath" -ForegroundColor Cyan
    }

    $postgresPassword = Get-EnvValue 'POSTGRES_PASSWORD'
    $minioPassword = Get-EnvValue 'MINIO_ROOT_PASSWORD'

    if ([string]::IsNullOrWhiteSpace($postgresPassword) -or
        $postgresPassword -match 'change-this|replace-with') {
        throw 'POSTGRES_PASSWORD is not configured. Edit .env, or remove an unused .env and rerun this script.'
    }

    if ([string]::IsNullOrWhiteSpace($minioPassword) -or
        $minioPassword -match 'change-this|replace-with') {
        throw 'MINIO_ROOT_PASSWORD is not configured. Edit .env, or remove an unused .env and rerun this script.'
    }

    Assert-LocalPassword -Password $postgresPassword -Name 'POSTGRES_PASSWORD'
    Assert-LocalPassword -Password $minioPassword -Name 'MINIO_ROOT_PASSWORD'

    if ($postgresPassword -eq $minioPassword) {
        throw 'POSTGRES_PASSWORD and MINIO_ROOT_PASSWORD must be different.'
    }

    if ($NoStart) {
        Write-Host 'Configuration is ready. Docker services were not started because -NoStart was specified.' -ForegroundColor Yellow
        return
    }

    if ($ResetData) {
        Write-Warning 'Deleting this project Docker containers and volume data.'
        Invoke-Compose -Arguments @('down', '-v', '--remove-orphans')
    }

    Invoke-Compose -Arguments @('config', '--quiet')
    Invoke-Compose -Arguments @('up', '-d', '--build')
    Invoke-Compose -Arguments @('ps', '-a')

    Write-Host ''
    Write-Host 'Startup command completed. ClamAV can take longer on the first run.' -ForegroundColor Green
    Write-Host '  Application: http://127.0.0.1:8088'
    Write-Host '  Mailpit:     http://127.0.0.1:8025'
    Write-Host '  MinIO:       http://127.0.0.1:9001'
    Write-Host '  Status:      docker compose ps -a'
    Write-Host '  Logs:        docker compose logs --tail=200'
    Write-Host 'migrate showing Exited (0) means the database migration completed successfully.'

    if (-not $createdEnv) {
        Write-Host 'Existing passwords remain available in the local .env file.'
    }
}
finally {
    Pop-Location
}
