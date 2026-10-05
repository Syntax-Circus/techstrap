<#
.SYNOPSIS
Starts the local compose stack, checks that the Api and the Admin answer, and stops it again.
.DESCRIPTION
The PHASE-07 T20 check: "docker compose up" gives a healthy Admin and an Api whose /health/ready answers 200. It is opt-in (it builds four images and needs Docker),
so it is not part of the default CI run; run it by hand before merging a change to a Dockerfile, a compose file or the host wiring, or start the "Compose smoke" workflow.

It never touches a stack you already run. It uses its own compose project name (techstrap-smoke, never the default "techstrap"), publishes every host port on a free
port chosen by Docker instead of 8080 to 8082 and 8025, and stops only that project. It never removes volumes: "docker compose down" is run without -v, so the Postgres and
key-ring volumes of the smoke project stay for the next run (they are named techstrap-smoke_*; remove them yourself when you want them gone).

The Admin starts with placeholder OIDC settings (docker-compose.yml), so nothing here signs in; it checks the container's health and its /health/ready endpoint.
.PARAMETER ProjectName
The compose project name. "techstrap" is refused, because that is the name of the stack you may be running.
.PARAMETER NoBuild
Do not rebuild the images; use the ones that exist (techstrap-smoke-*:local, left by an earlier run).
.PARAMETER KeepRunning
Leave the stack running after the checks (for looking at it); stop it later with: docker compose -p techstrap-smoke down
.PARAMETER DryRun
Print the commands and the override file and run nothing.
.EXAMPLE
pwsh ./scripts/Test-ComposeSmoke.ps1
.EXAMPLE
pwsh ./scripts/Test-ComposeSmoke.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [string] $ComposeFile = (Join-Path $PSScriptRoot '..' 'docker-compose.yml'),
    [string] $ProjectName = 'techstrap-smoke',
    [int] $TimeoutSeconds = 900,
    [switch] $NoBuild,
    [switch] $KeepRunning,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ProjectName -eq 'techstrap') {
    throw "Refusing to use the project name 'techstrap': it is the name of the stack you run by hand, and this script stops its project when it finishes."
}

# Every published port becomes "a free port on loopback", so a stack that already holds 8080 to 8082 or 8025 is not in the way. "!override" replaces the list instead of adding to it.
# The images get their own names too (techstrap-smoke-*:local), so building them never replaces the techstrap-*:local images of the stack you run by hand.
$overrideText = @'
services:
  api:
    image: techstrap-smoke-api:local
    ports: !override
      - "127.0.0.1::80"
  worker:
    image: techstrap-smoke-worker:local
  admin:
    image: techstrap-smoke-admin:local
    ports: !override
      - "127.0.0.1::80"
  portal:
    image: techstrap-smoke-portal:local
    ports: !override
      - "127.0.0.1::80"
  mailpit:
    ports: !override
      - "127.0.0.1::8025"
'@

$overridePath = Join-Path ([System.IO.Path]::GetTempPath()) "techstrap-smoke-$([guid]::NewGuid().ToString('N')).override.yml"
$composeArguments = @('compose', '-p', $ProjectName, '-f', (Resolve-Path -LiteralPath $ComposeFile).Path, '-f', $overridePath)

function Invoke-Compose {
    param([Parameter(Mandatory)][string[]] $Arguments, [switch] $AllowFailure)

    $output = & docker @composeArguments @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "docker $($composeArguments -join ' ') $($Arguments -join ' ') failed ($LASTEXITCODE):`n$output"
    }

    return $output
}

function Get-PublishedPort {
    param([Parameter(Mandatory)][string] $Service, [Parameter(Mandatory)][int] $ContainerPort)

    $mapping = (Invoke-Compose -Arguments @('port', $Service, "$ContainerPort")).Trim()
    if ($mapping -notmatch ':(?<port>\d+)\s*$') {
        throw "Could not read the published port of $Service from '$mapping'."
    }

    return [int] $Matches['port']
}

function Assert-Ready {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][int] $Port)

    $uri = "http://127.0.0.1:$Port/health/ready"
    $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) {
        throw "$Name answered $($response.StatusCode) at $uri (expected 200)."
    }

    Write-Output "ok   $Name $uri -> 200"
}

# The four images are built one after another, never by "up --build": compose builds them in parallel, and four restores into the one shared NuGet cache mount
# (Dockerfile --mount=type=cache,id=techstrap-nuget) can corrupt each other ("Could not find file .../markdig/...").
$services = @('api', 'worker', 'admin', 'portal')
$upArguments = @('up', '-d', '--wait', '--wait-timeout', "$TimeoutSeconds")

if ($DryRun) {
    Write-Output "# override file ($overridePath)"
    Write-Output $overrideText
    if (-not $NoBuild) {
        foreach ($service in $services) {
            Write-Output "docker $($composeArguments -join ' ') build $service"
        }
    }

    Write-Output "docker $($composeArguments -join ' ') $($upArguments -join ' ')"
    Write-Output "docker $($composeArguments -join ' ') port api 80"
    Write-Output "docker $($composeArguments -join ' ') port admin 80"
    Write-Output "GET /health/ready on the Api and on the Admin, expecting 200"
    Write-Output "docker $($composeArguments -join ' ') ps admin --format json   (Health must be healthy)"
    if (-not $KeepRunning) {
        Write-Output "docker $($composeArguments -join ' ') down"
    }

    return
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue) -or ((& docker compose version 2>&1 | Out-String) -notmatch 'Docker Compose')) {
    throw 'Docker with the compose plugin is required.'
}

Set-Content -LiteralPath $overridePath -Value $overrideText -Encoding utf8
$failed = $true
try {
    Write-Output "Starting project $ProjectName (this builds the images unless -NoBuild is given)..."
    if (-not $NoBuild) {
        foreach ($service in $services) {
            Write-Output "Building $service..."
            Invoke-Compose -Arguments @('build', $service) | Out-Null
        }
    }

    Invoke-Compose -Arguments $upArguments | Out-Null

    $apiPort = Get-PublishedPort -Service 'api' -ContainerPort 80
    $adminPort = Get-PublishedPort -Service 'admin' -ContainerPort 80
    Assert-Ready -Name 'Api' -Port $apiPort
    Assert-Ready -Name 'Admin' -Port $adminPort

    $admin = (Invoke-Compose -Arguments @('ps', 'admin', '--format', 'json') | ConvertFrom-Json)
    if ($admin.Health -ne 'healthy') {
        throw "The Admin container reports health '$($admin.Health)' (expected healthy)."
    }

    Write-Output 'ok   Admin container is healthy'
    $failed = $false
}
finally {
    if ($failed) {
        Write-Output (Invoke-Compose -Arguments @('logs', '--tail', '60', 'api', 'admin') -AllowFailure)
    }

    if (-not $KeepRunning) {
        # Never -v: the volumes are not this script's to delete.
        Invoke-Compose -Arguments @('down') -AllowFailure | Out-Null
    }

    Remove-Item -LiteralPath $overridePath -Force -ErrorAction SilentlyContinue
}

Write-Output 'Compose smoke passed.'
