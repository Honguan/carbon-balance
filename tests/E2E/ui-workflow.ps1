param(
    [string]$BaseUrl = 'http://127.0.0.1:5088',
    [string]$FixturePath = '.analysis/ui-workflow.local.json',
    [string]$Session = 'ui-workflow',
    [string]$BrowserExecutable = 'npx',
    [switch]$LoginOnly
)
$ErrorActionPreference = 'Stop'
# Generate the isolated fixture with tests/E2E/UiFixture; never commit its credentials.
# The local JSON contains email, password, projectA, projectB and factorId.
$fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
function Browser {
    $Command = $args
    $operation = if ($Command[0] -eq '--json') { $Command[1] } else { $Command[0] }
    Write-Host "[$Session] $operation"
    if ([IO.Path]::GetFileNameWithoutExtension($BrowserExecutable) -eq 'npx') {
        $result = & $BrowserExecutable --loglevel=error --yes agent-browser --session $Session @Command
    } else {
        $result = & $BrowserExecutable --session $Session @Command
    }
    if ($LASTEXITCODE -ne 0) { throw "Browser command failed: $($Command[0]); $(($result -join "`n").Replace($fixture.password, '[redacted]'))" }
    return $result
}
function Verify([string]$Expression, [string]$Message) {
    $response = Browser --json eval $Expression | ConvertFrom-Json
    if (-not $response.success -or $response.data.result -ne $true) { throw $Message }
    Write-Host "PASS: $Message"
}
Browser open "$BaseUrl/Identity/Account/Login" | Out-Null
Browser snapshot -i | Out-Null
$loginForm = Browser --json eval "Boolean(document.querySelector('#Input_Identifier'))" | ConvertFrom-Json
if ($loginForm.data.result) {
    Browser fill '#Input_Identifier' $fixture.email | Out-Null
    Browser fill '#Input_Password' $fixture.password | Out-Null
    Browser click 'form.auth-form button[type=submit]' | Out-Null
    Browser wait --load networkidle | Out-Null
}
Browser snapshot -i | Out-Null
Verify "!location.pathname.includes('/Login')" 'Confirmed fixture user signs in through the actual login form'
if ($LoginOnly) { return }
if (-not $fixture.projectB) { throw 'Fixture projects are not ready; complete the local fixture first.' }
$projectB = $fixture.projectB
$activityUrl = "$BaseUrl/Workspace/lifecycle/raw-material?projectVersionId=$($fixture.projectA)"
Browser open $activityUrl | Out-Null
Browser snapshot -i | Out-Null
Verify "document.querySelector('#projectVersionId').value === '$($fixture.projectA)'" 'Select inventory A'
Browser select '#projectVersionId' $projectB | Out-Null
Browser wait --load networkidle | Out-Null
Browser snapshot -i | Out-Null
Verify "document.querySelector('#projectVersionId').value === '$projectB'" 'Select inventory B'
Browser click 'nav.workspace-stage-nav a[href*="/calculation"]' | Out-Null
Browser wait --load networkidle | Out-Null
Browser snapshot -i | Out-Null
Verify "new URL(location.href).searchParams.get('projectVersionId') === '$projectB'" 'Top navigation preserves inventory B'
Browser click 'nav.workspace-stage-nav a[href*="/lifecycle"]' | Out-Null
Browser wait --load networkidle | Out-Null
Browser snapshot -i | Out-Null
Verify "document.querySelector('#projectVersionId').value === '$projectB'" 'Returning to activities preserves inventory B'

# Submit an intentionally incompatible unit conversion to exercise server-side error retention.
Browser fill '[name=sourceReference]' 'ui-retention-check' | Out-Null
Browser fill '[name=rawValue]' '12.345678901234' | Out-Null
Browser select '[name=rawUnitCode]' 'kg' | Out-Null
Browser select '[name=canonicalUnitCode]' 'kWh' | Out-Null
$factorResponse = Browser --json eval 'document.querySelector(''[data-factor-select] option[data-factor-unit="kWh"]'').value' | ConvertFrom-Json
Browser select '[data-factor-select]' $factorResponse.data.result | Out-Null
Browser click 'form[data-emission-form] button[type=submit]' | Out-Null
Browser wait --load networkidle | Out-Null
Browser snapshot -i | Out-Null
Verify "document.querySelector('[name=rawValue]').value === '12.345678901234' && document.querySelector('[name=sourceReference]').value === 'ui-retention-check'" 'Server validation retains entered activity values'
Verify "Boolean(document.querySelector('.validation-summary-errors'))" 'The incompatible unit conversion produces a visible validation error'

# Correct the error and save, then replace and retire that new activity through the UI.
Browser select '[name=canonicalUnitCode]' 'kg' | Out-Null
Browser select '[data-factor-select]' $fixture.factorId | Out-Null
Browser click 'form[data-emission-form] button[type=submit]' | Out-Null
Browser wait --load networkidle | Out-Null
Browser snapshot -i | Out-Null
$rowCount = Browser --json eval "document.querySelectorAll('form[action*=RemoveActivity]').length" | ConvertFrom-Json
$editLink = Browser --json eval "Array.from(document.querySelectorAll('a')).find(a => a.textContent.trim() === '更正活動' && a.closest('li').textContent.includes('12.345678901234'))?.getAttribute('href')" | ConvertFrom-Json
if (-not $editLink.data.result) { throw 'No edit operation available on the saved activities.' }
Browser open ([uri]::new([uri]$BaseUrl, $editLink.data.result).AbsoluteUri) | Out-Null
Browser snapshot -i | Out-Null
Verify "document.querySelector('[name=rawValue]').value === '12.345678901234'" 'GET edit retains the stored decimal precision'
Verify "Boolean(document.querySelector('form[data-emission-form] input[name=activityId]')?.value)" 'GET edit includes the activity identity'
Browser fill '[name=rawValue]' '17.25' | Out-Null
Browser click 'form[data-emission-form] button[type=submit]' | Out-Null
Browser wait --load networkidle | Out-Null
Browser snapshot -i | Out-Null
Verify "document.body.textContent.includes('17.25')" 'Editing an activity saves a replacement value'
Verify "document.querySelectorAll('form[action*=RemoveActivity]').length === $($rowCount.data.result)" 'Replacement does not increase the current activity count'
$retirement = Browser --json eval '({ count: document.querySelectorAll(''form[action*="RemoveActivity"]'').length, id: document.querySelector(''form[action*="RemoveActivity"] input[name="activityId"]'').value })' | ConvertFrom-Json
Browser click "form:has(input[name='activityId'][value='$($retirement.data.result.id)']) button[type=submit]" | Out-Null
Browser wait --load networkidle | Out-Null
Browser snapshot -i | Out-Null
Verify "document.querySelectorAll('form[action*=RemoveActivity]').length === $($retirement.data.result.count - 1)" 'Activity retirement removes exactly one current row'
Browser screenshot ([IO.Path]::GetFullPath('.analysis/ui-workflow-complete.png')) | Out-Null
