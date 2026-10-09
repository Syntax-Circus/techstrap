<#
.SYNOPSIS
Runs a TechStrap k6 load scenario (sustained or spike) against the local dev stack or a UAT stack.
.DESCRIPTION
Wraps tests/load/scenarios/<scenario>.js. Uses the k6 binary when it is on PATH, otherwise (or with -UseDocker) the pinned
grafana/k6 Docker image. The k6 summary is written to tests/load/results/<timestamp>-<scenario>.json (gitignored).
After a local run it prints the email_outbox counts by status and the dead-letter count through the compose Postgres.
API keys come from TS_TRUSTED_KEY and TS_PUBLIC_KEY in the environment (required for -Target uat; the local target falls
back to the public dev keys built into the k6 scripts). Key values are never printed and are handed to Docker by name only.
Not run in CI. See tests/load/README.md.
.PARAMETER Target
local (compose stack on localhost) or uat (requires -BaseUrl, -PortalUrl and both key variables).
.PARAMETER Scenario
sustained (mixed traffic at -Rate requests per second) or spike (one-IP ramp to TS_SPIKE_PEAK plus a recovery phase).
.PARAMETER Rate
Total requests per second of the sustained mix (TS_RATE). Default 20.
.PARAMETER Duration
Length of the sustained run (TS_DURATION), for example 30s or 10m. Default 10m.
.PARAMETER BaseUrl
Api base URL. Default for local: http://localhost:8080.
.PARAMETER PortalUrl
Portal base URL. Default for local: http://localhost:8082.
.PARAMETER UseDocker
Run k6 from the grafana/k6 image even when a k6 binary is on PATH.
.PARAMETER SkipDbCounts
Skip the post-run outbox and dead-letter counts of a local run.
.PARAMETER DryRun
Print the plan and the command line, then return without creating anything or starting any process.
.EXAMPLE
pwsh scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained -Rate 2 -Duration 30s
.EXAMPLE
pwsh scripts/Invoke-LoadTest.ps1 -Target uat -Scenario spike -BaseUrl https://api.uat.example -PortalUrl https://uat.example -UseDocker
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('local', 'uat')][string] $Target,
    [Parameter(Mandatory)][ValidateSet('sustained', 'spike')][string] $Scenario,
    [int] $Rate = 20,
    [string] $Duration = '10m',
    [string] $BaseUrl,
    [string] $PortalUrl,
    [switch] $UseDocker,
    [switch] $SkipDbCounts,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Newest grafana/k6 tag that `docker manifest inspect` resolved when this script was written; never use latest.
$K6Image = 'grafana/k6:2.1.0'

# Post-run SQL (verified against \d email_outbox; dead letters are rows with status 'DeadLettered').
$OutboxTable = 'email_outbox'
$StatusColumn = 'status'
$DeadLetterStatus = 'DeadLettered'
$OutboxSql = "select $StatusColumn, count(*) from $OutboxTable group by $StatusColumn order by $StatusColumn"
$DeadLetterSql = "select count(*) from $OutboxTable where $StatusColumn = '$DeadLetterStatus'"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$loadDir = Join-Path $repoRoot 'tests/load'
$resultsDir = Join-Path $loadDir 'results'

if ($Target -eq 'uat') {
    if (-not $BaseUrl -or -not $PortalUrl) { throw '-BaseUrl and -PortalUrl are required for -Target uat' }
    if (-not ((Test-Path Env:TS_TRUSTED_KEY) -and (Test-Path Env:TS_PUBLIC_KEY))) { throw 'TS_TRUSTED_KEY and TS_PUBLIC_KEY must be set for -Target uat' }
} else {
    if (-not $BaseUrl) { $BaseUrl = 'http://localhost:8080' }
    if (-not $PortalUrl) { $PortalUrl = 'http://localhost:8082' }
}

$haveK6 = [bool](Get-Command k6 -ErrorAction SilentlyContinue)
$viaDocker = $UseDocker.IsPresent -or -not $haveK6

$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")
$resultName = "$stamp-$Scenario.json"
$resultPath = Join-Path $resultsDir $resultName
$resultDisplay = "tests/load/results/$resultName"

$k6BaseUrl = $BaseUrl
$k6PortalUrl = $PortalUrl
if ($viaDocker) {
    $k6BaseUrl = $BaseUrl -replace '//(localhost|127\.0\.0\.1)', '//host.docker.internal'
    $k6PortalUrl = $PortalUrl -replace '//(localhost|127\.0\.0\.1)', '//host.docker.internal'
}

if ($viaDocker) {
    $exe = 'docker'
    $k6Args = @(
        'run', '--rm', '-i', '--add-host=host.docker.internal:host-gateway',
        '-v', "${loadDir}:/load",
        '-e', 'TS_BASE_URL', '-e', 'TS_PORTAL_URL', '-e', 'TS_RATE', '-e', 'TS_DURATION',
        '-e', 'TS_TRUSTED_KEY', '-e', 'TS_PUBLIC_KEY',
        '-e', 'TS_FORWARDED_PREFIX', '-e', 'TS_IP_COUNT', '-e', 'TS_SPIKE_PEAK', '-e', 'TS_PRODUCT_KEY',
        $K6Image, 'run', '--summary-export', "/load/results/$resultName", "/load/scenarios/$Scenario.js"
    )
} else {
    $exe = 'k6'
    $k6Args = @('run', '--summary-export', $resultPath, (Join-Path $loadDir "scenarios/$Scenario.js"))
}
$commandLine = "$exe " + ($k6Args -join ' ')

Write-Output "Target:    $Target ($k6BaseUrl, $k6PortalUrl)"
Write-Output "Scenario:  $Scenario (rate $Rate/s, duration $Duration)"
Write-Output "Runner:    $(if ($viaDocker) { $K6Image } else { 'local k6 binary' })"
Write-Output "Results:   $resultDisplay"

if ($DryRun) {
    Write-Output "DRY-RUN: $commandLine"
    return
}

$env:TS_BASE_URL = $k6BaseUrl
$env:TS_PORTAL_URL = $k6PortalUrl
$env:TS_RATE = [string]$Rate
$env:TS_DURATION = $Duration

New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
Write-Output "Running:   $commandLine"
& $exe @k6Args
$k6Exit = $LASTEXITCODE

if ($Target -eq 'local' -and -not $SkipDbCounts) {
    Push-Location $repoRoot
    try {
        Write-Output "email_outbox rows by status ($StatusColumn|count):"
        docker compose exec -T postgres psql -U techstrap -d techstrap -At -c $OutboxSql
        Write-Output "Dead-lettered outbox rows (status $DeadLetterStatus):"
        docker compose exec -T postgres psql -U techstrap -d techstrap -At -c $DeadLetterSql
    } finally {
        Pop-Location
    }
} elseif ($Target -eq 'uat') {
    Write-Output 'Run the SQL from tests/load/README.md with your psql to check the outbox drain and dead letters.'
}

exit $k6Exit
