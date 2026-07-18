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
        throw "$Name 必須為 16～128 字元，且只能包含英文字母、數字、句點、底線或連字號。"
    }
}

function Read-LocalPassword([string]$Label) {
    while ($true) {
        $first = ConvertTo-PlainText (Read-Host "$Label（輸入時不會顯示）" -AsSecureString)
        $second = ConvertTo-PlainText (Read-Host "再次輸入 $Label" -AsSecureString)

        if ($first -ne $second) {
            Write-Warning '兩次輸入不一致，請重新輸入。'
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
    if (-not (Test-Path $envPath)) {
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
        throw "docker compose $($Arguments -join ' ') 執行失敗，結束碼：$LASTEXITCODE"
    }
}

if ($NoStart -and $ResetData) {
    throw '-NoStart 與 -ResetData 不可同時使用。'
}

Push-Location $repositoryRoot
try {
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker 尚未啟動，請先開啟 Docker Desktop 或 Docker Engine。'
    }

    & docker compose version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw '找不到 Docker Compose，請確認 docker compose 可正常執行。'
    }

    & docker volume inspect $postgresVolumeName *> $null
    $hasExistingPostgresVolume = $LASTEXITCODE -eq 0

    $createdEnv = $false
    if (-not (Test-Path $envPath)) {
        if ($hasExistingPostgresVolume -and -not $ResetData) {
            throw @"
偵測到既有 PostgreSQL 資料 Volume，但找不到 .env。
自動產生新密碼將無法登入舊資料庫。
請還原原本的 .env；若舊資料可刪除，請改執行：
  .\scripts\setup-local.ps1 -ResetData
"@
        }

        if ($Manual) {
            $postgresPassword = Read-LocalPassword 'PostgreSQL 密碼'
            $minioPassword = Read-LocalPassword 'MinIO 密碼'
        }
        else {
            $postgresPassword = New-LocalPassword
            $minioPassword = New-LocalPassword
        }

        if ($postgresPassword -eq $minioPassword) {
            throw 'PostgreSQL 與 MinIO 必須使用不同密碼。'
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
        Write-Host '已建立 .env，本機密碼如下：' -ForegroundColor Green
        Write-Host '  PostgreSQL 帳號：carbon_app'
        Write-Host "  PostgreSQL 密碼：$postgresPassword"
        Write-Host '  MinIO 帳號：carbon_minio'
        Write-Host "  MinIO 密碼：$minioPassword"
        Write-Host "  設定檔位置：$envPath"
        Write-Warning '請妥善保存 .env，不要上傳 GitHub、貼到聊天或傳給其他人。'
    }
    else {
        Write-Host "已找到既有 .env，將沿用原本設定，不會覆寫密碼：$envPath" -ForegroundColor Cyan
    }

    $postgresPassword = Get-EnvValue 'POSTGRES_PASSWORD'
    $minioPassword = Get-EnvValue 'MINIO_ROOT_PASSWORD'

    if ([string]::IsNullOrWhiteSpace($postgresPassword) -or
        $postgresPassword -match 'change-this|replace-with|請替換') {
        throw 'POSTGRES_PASSWORD 尚未設定。請編輯 .env，或刪除未使用的 .env 後重新執行本腳本。'
    }

    if ([string]::IsNullOrWhiteSpace($minioPassword) -or
        $minioPassword -match 'change-this|replace-with|請替換') {
        throw 'MINIO_ROOT_PASSWORD 尚未設定。請編輯 .env，或刪除未使用的 .env 後重新執行本腳本。'
    }

    if ($postgresPassword -eq $minioPassword) {
        throw 'POSTGRES_PASSWORD 與 MINIO_ROOT_PASSWORD 不可相同。'
    }

    if ($NoStart) {
        Write-Host '設定完成；因指定 -NoStart，尚未啟動 Docker 服務。' -ForegroundColor Yellow
        return
    }

    if ($ResetData) {
        Write-Warning '即將刪除本專案所有 Docker 容器與 Volume 資料。'
        Invoke-Compose -Arguments @('down', '-v', '--remove-orphans')
    }

    Invoke-Compose -Arguments @('config', '--quiet')
    Invoke-Compose -Arguments @('up', '-d', '--build')
    Invoke-Compose -Arguments @('ps', '-a')

    Write-Host ''
    Write-Host '啟動指令已完成。ClamAV 第一次初始化可能需要較長時間。' -ForegroundColor Green
    Write-Host '  系統：http://127.0.0.1:8088'
    Write-Host '  Mailpit：http://127.0.0.1:8025'
    Write-Host '  MinIO：http://127.0.0.1:9001'
    Write-Host '  狀態：docker compose ps -a'
    Write-Host '  記錄：docker compose logs --tail=200'
    Write-Host 'migrate 顯示 Exited (0) 代表資料庫更新成功，屬於正常狀態。'

    if (-not $createdEnv) {
        Write-Host '既有密碼可在本機 .env 中查看。'
    }
}
finally {
    Pop-Location
}
