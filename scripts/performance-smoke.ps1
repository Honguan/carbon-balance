param(
    [string]$BaseUrl = "http://127.0.0.1:8088",
    [int]$Requests = 100,
    [int]$WarmupRequests = 5,
    [int]$MaximumP95Milliseconds = 500
)

$ErrorActionPreference = "Stop"
1..$WarmupRequests | ForEach-Object {
    Invoke-WebRequest -UseBasicParsing "$BaseUrl/health/ready" | Out-Null
}

$samples = New-Object 'System.Collections.Generic.List[double]'
1..$Requests | ForEach-Object {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-WebRequest -UseBasicParsing "$BaseUrl/health/ready"
    $stopwatch.Stop()
    if ($response.StatusCode -ne 200) { throw "Unexpected status code $($response.StatusCode)." }
    $samples.Add($stopwatch.Elapsed.TotalMilliseconds)
}

$ordered = @($samples | Sort-Object)
$p50 = $ordered[[Math]::Ceiling($ordered.Count * 0.50) - 1]
$p95 = $ordered[[Math]::Ceiling($ordered.Count * 0.95) - 1]
$p99 = $ordered[[Math]::Ceiling($ordered.Count * 0.99) - 1]
Write-Output ("REQUESTS={0} P50_MS={1:N2} P95_MS={2:N2} P99_MS={3:N2}" -f $Requests, $p50, $p95, $p99)
if ($p95 -gt $MaximumP95Milliseconds) {
    throw "P95 exceeded $MaximumP95Milliseconds ms."
}
Write-Output "PERFORMANCE_SMOKE=PASS"
