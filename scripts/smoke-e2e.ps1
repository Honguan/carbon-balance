param(
    [string]$BaseUrl = 'http://127.0.0.1:8088',
    [string]$MailpitUrl = 'http://127.0.0.1:8025',
    [Parameter(Mandatory = $true)]
    [string]$Password
)

$ErrorActionPreference = 'Stop'
$suffix = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$email = "p0-smoke-$suffix@example.test"
$namePrefix = "P0-$suffix"
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

function Get-PageToken([string]$Url) {
    $response = Invoke-WebRequest -UseBasicParsing -WebSession $session $Url
    $match = [regex]::Match($response.Content, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"')
    if (-not $match.Success) {
        throw "Antiforgery token was not found: $Url"
    }

    return @{ Token = $match.Groups[1].Value; Content = $response.Content }
}

function Post-Workspace([string]$Handler, [hashtable]$Fields) {
    $page = Get-PageToken "$BaseUrl/Workspace"
    $body = @{ __RequestVerificationToken = $page.Token }
    foreach ($key in $Fields.Keys) {
        $body[$key] = $Fields[$key]
    }

    try {
        return Invoke-WebRequest -UseBasicParsing -WebSession $session -Method Post -Uri "$BaseUrl/Workspace?handler=$Handler" -Body $body
    }
    catch {
        throw "Workspace handler failed: $Handler. $($_.Exception.Message)"
    }
}

function Get-OptionValue([string]$Html, [string]$VisibleTextPattern) {
    $pattern = '<option value="([0-9a-f-]{{36}})">[^<]*{0}[^<]*</option>' -f $VisibleTextPattern
    $match = [regex]::Match($Html, $pattern, 'IgnoreCase')
    if (-not $match.Success) {
        throw "Option was not found: $VisibleTextPattern"
    }

    return $match.Groups[1].Value
}

function Get-HiddenValue([string]$Html, [string]$Name) {
    $pattern = '<input type="hidden" name="{0}" value="([0-9a-f-]{{36}})"' -f [regex]::Escape($Name)
    $match = [regex]::Match($Html, $pattern, 'IgnoreCase')
    if (-not $match.Success) {
        throw "Hidden value was not found: $Name"
    }

    return $match.Groups[1].Value
}

function Post-Evidence([string]$ActivityDataId) {
    $page = Get-PageToken "$BaseUrl/Workspace"
    $boundary = "----------------p0smoke$suffix"
    $encoding = [System.Text.Encoding]::UTF8
    $prefix = "--$boundary`r`nContent-Disposition: form-data; name=`"__RequestVerificationToken`"`r`n`r`n$($page.Token)`r`n" +
        "--$boundary`r`nContent-Disposition: form-data; name=`"activityDataId`"`r`n`r`n$ActivityDataId`r`n" +
        "--$boundary`r`nContent-Disposition: form-data; name=`"evidenceFile`"; filename=`"evidence.txt`"`r`n" +
        "Content-Type: text/plain`r`n`r`n"
    $fileBytes = $encoding.GetBytes("P0 evidence $suffix")
    $suffixBytes = $encoding.GetBytes("`r`n--$boundary--`r`n")
    $prefixBytes = $encoding.GetBytes($prefix)
    $body = New-Object System.IO.MemoryStream
    $body.Write($prefixBytes, 0, $prefixBytes.Length)
    $body.Write($fileBytes, 0, $fileBytes.Length)
    $body.Write($suffixBytes, 0, $suffixBytes.Length)

    return Invoke-WebRequest -UseBasicParsing -WebSession $session -Method Post `
        -Uri "$BaseUrl/Workspace?handler=UploadEvidence" `
        -ContentType "multipart/form-data; boundary=$boundary" `
        -Body $body.ToArray()
}

$register = Get-PageToken "$BaseUrl/Identity/Account/Register"
Invoke-WebRequest -UseBasicParsing -WebSession $session -Method Post -Uri "$BaseUrl/Identity/Account/Register" -Body @{
    'Input.Email' = $email
    'Input.Password' = $Password
    'Input.ConfirmPassword' = $Password
    '__RequestVerificationToken' = $register.Token
} | Out-Null

$confirmationLink = $null
$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Seconds 1
    $messages = Invoke-RestMethod "$MailpitUrl/api/v1/messages"
    $message = $messages.messages | Where-Object { @($_.To)[0].Address -eq $email } | Select-Object -First 1
    if ($message) {
        $detail = Invoke-RestMethod "$MailpitUrl/api/v1/message/$($message.ID)"
        $decodedHtml = [System.Net.WebUtility]::HtmlDecode(
            [System.Net.WebUtility]::HtmlDecode($detail.HTML))
        $linkMatch = [regex]::Match($decodedHtml, 'href="([^"]+/Identity/Account/ConfirmEmail[^"]+)"')
        if ($linkMatch.Success) {
            $confirmationLink = $linkMatch.Groups[1].Value
        }
    }
} while (-not $confirmationLink -and (Get-Date) -lt $deadline)

if (-not $confirmationLink) {
    throw 'Mailpit did not receive an email confirmation link.'
}

Invoke-WebRequest -UseBasicParsing -WebSession $session $confirmationLink | Out-Null

$login = Get-PageToken "$BaseUrl/Identity/Account/Login"
Invoke-WebRequest -UseBasicParsing -WebSession $session -Method Post -Uri "$BaseUrl/Identity/Account/Login" -Body @{
    'Input.Email' = $email
    'Input.Password' = $Password
    'Input.RememberMe' = 'false'
    '__RequestVerificationToken' = $login.Token
} | Out-Null

Post-Workspace 'CreateOrganization' @{ organizationName = "$namePrefix Organization" } | Out-Null
Post-Workspace 'CreateProduct' @{ productName = "$namePrefix Product" } | Out-Null
$workspace = (Invoke-WebRequest -UseBasicParsing -WebSession $session "$BaseUrl/Workspace").Content
$productVersionId = Get-OptionValue $workspace ([regex]::Escape("$namePrefix Product"))

Post-Workspace 'CreatePcr' @{
    registrationNumber = "$namePrefix-PCR"
    versionNumber = '1'
    title = "$namePrefix PCR"
    validFrom = '2025-01-01'
    validTo = '2027-12-31'
    sourceReference = 'golden-pcr-source-1'
} | Out-Null
$workspace = (Invoke-WebRequest -UseBasicParsing -WebSession $session "$BaseUrl/Workspace").Content
$pcrVersionId = Get-HiddenValue $workspace 'pcrVersionId'
Post-Workspace 'PublishPcr' @{ pcrVersionId = $pcrVersionId } | Out-Null

Post-Workspace 'CreateInventory' @{
    productVersionId = $productVersionId
    periodStart = '2026-01-01'
    periodEnd = '2026-12-31'
    functionalUnit = '1 product'
    pcrVersionId = $pcrVersionId
} | Out-Null
$workspace = (Invoke-WebRequest -UseBasicParsing -WebSession $session "$BaseUrl/Workspace").Content
$inventoryId = Get-OptionValue $workspace ([regex]::Escape('1 product'))

$factorInputs = @(
    @{ Name = "$namePrefix Raw factor"; Value = '2'; Unit = 'kg' },
    @{ Name = "$namePrefix Energy factor"; Value = '0.5'; Unit = 'kWh' },
    @{ Name = "$namePrefix Transport factor"; Value = '0.1'; Unit = 'tonne-km' },
    @{ Name = "$namePrefix Waste factor"; Value = '0.25'; Unit = 'kg' }
)
foreach ($factor in $factorInputs) {
    Post-Workspace 'CreateFactor' @{
        factorName = $factor.Name
        factorValue = $factor.Value
        denominatorUnitCode = $factor.Unit
        sourceDatasetVersion = 'golden-dataset-1'
        licenseCode = 'test-fixture'
    } | Out-Null
}

$workspace = (Invoke-WebRequest -UseBasicParsing -WebSession $session "$BaseUrl/Workspace").Content
$rawFactorId = Get-OptionValue $workspace ([regex]::Escape("$namePrefix Raw factor / kg"))
$energyFactorId = Get-OptionValue $workspace ([regex]::Escape("$namePrefix Energy factor / kWh"))
$transportFactorId = Get-OptionValue $workspace ([regex]::Escape("$namePrefix Transport factor / tonne-km"))
$wasteFactorId = Get-OptionValue $workspace ([regex]::Escape("$namePrefix Waste factor / kg"))

foreach ($factorId in @($rawFactorId, $energyFactorId, $transportFactorId, $wasteFactorId)) {
    Post-Workspace 'PublishFactor' @{ factorVersionId = $factorId } | Out-Null
}

$activities = @(
    @{ Stage = 'RawMaterial'; Name = 'Raw material'; Raw = '1000'; RawUnit = 'g'; CanonicalUnit = 'kg'; Factor = $rawFactorId },
    @{ Stage = 'Manufacturing'; Name = 'Manufacturing electricity'; Raw = '3'; RawUnit = 'kWh'; CanonicalUnit = 'kWh'; Factor = $energyFactorId },
    @{ Stage = 'Distribution'; Name = 'Distribution'; Raw = '10'; RawUnit = 'tonne-km'; CanonicalUnit = 'tonne-km'; Factor = $transportFactorId },
    @{ Stage = 'Use'; Name = 'Use electricity'; Raw = '4'; RawUnit = 'kWh'; CanonicalUnit = 'kWh'; Factor = $energyFactorId },
    @{ Stage = 'EndOfLife'; Name = 'End of life'; Raw = '2'; RawUnit = 'kg'; CanonicalUnit = 'kg'; Factor = $wasteFactorId }
)
foreach ($activity in $activities) {
    Post-Workspace 'AddActivity' @{
        inventoryProjectVersionId = $inventoryId
        lifecycleStage = $activity.Stage
        activityName = $activity.Name
        rawValue = $activity.Raw
        rawUnitCode = $activity.RawUnit
        canonicalUnitCode = $activity.CanonicalUnit
        factorVersionId = $activity.Factor
    } | Out-Null
}

$workspace = (Invoke-WebRequest -UseBasicParsing -WebSession $session "$BaseUrl/Workspace").Content
$activityDataId = Get-HiddenValue $workspace 'activityDataId'
$evidenceResult = Post-Evidence $activityDataId
if ($evidenceResult.Content -notmatch 'Clean' -or $evidenceResult.Content -notmatch '[0-9a-f]{64}') {
    throw 'Evidence upload did not produce a clean SHA-256 record.'
}

$result = Post-Workspace 'Calculate' @{ inventoryProjectVersionId = $inventoryId }
if ($result.Content -notmatch '<dt[^>]*>[^<]+</dt>\s*<dd[^>]*>7(?:\.0+)? kgCO2e') {
    throw 'Golden E2E total is not 7 kgCO2e.'
}

if ($result.Content -notmatch '[0-9a-f]{64}') {
    throw 'Golden E2E input SHA-256 is missing.'
}

$repeatResult = Post-Workspace 'Calculate' @{ inventoryProjectVersionId = $inventoryId }
if ($repeatResult.Content -notmatch '<p>[^<]+0(?:\.0+)? kgCO2e</p>') {
    throw 'Golden E2E repeat run did not produce a zero product delta.'
}

Write-Output "E2E_SMOKE=PASS"
Write-Output "EMAIL=$email"
Write-Output "INVENTORY_VERSION_ID=$inventoryId"
