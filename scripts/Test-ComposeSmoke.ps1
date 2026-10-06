<#
.SYNOPSIS
Starts the local compose stack, checks that the Api, the Admin and the Portal answer and that the rate limit sees the real client address, and stops it again.
.DESCRIPTION
The PHASE-07 T20 check: "docker compose up" gives a healthy Admin and an Api whose /health/ready answers 200. PHASE-09 T18 adds the Portal to it: its /health/ready must answer 200 too, and the
Api's rate limit must count the visitor, not the Portal's container. It is opt-in (it builds four images and needs Docker),
so it is not part of the default CI run; run it by hand before merging a change to a Dockerfile, a compose file or the host wiring, or start the "Compose smoke" workflow.

It never touches a stack you already run. It uses its own compose project name (techstrap-smoke, never the default "techstrap"), publishes every host port on a free
port chosen by Docker instead of 8080 to 8082 and 8025, and stops only that project. It never removes volumes: "docker compose down" is run without -v, so the Postgres and
key-ring volumes of the smoke project stay for the next run (they are named techstrap-smoke_*; remove them yourself when you want them gone).

The Admin starts with placeholder OIDC settings (docker-compose.yml), so nothing here signs in; it checks the container's health and its /health/ready endpoint.

The real-address check (D-019): the override below lowers the Api's public rate limit to 3 requests per 10 minutes and makes the Portal trust the compose subnet. The script then calls the Portal's
suggest adapter, GET /p/smoke/suggest, which makes one call to the Api's public KB search per request, from inside the network (docker compose exec in the api container, which has curl):
the source is a container address on the pinned subnet, so the Portal treats it as the proxy hop and takes the visitor from X-Forwarded-For. Three calls as 203.0.113.10 answer 200 and the fourth 429
(the Api counted that visitor and passed its 429 through the adapter); then 203.0.113.11 answers 200 (another visitor is unaffected). If the Api counted the Portal's address instead, the second visitor
would be refused too. A call from the host through the published port would arrive from the Docker gateway, whose address differs between Linux and Docker Desktop, so it could not be trusted portably;
the call from the api container always comes from the pinned subnet. The override changes nothing in docker-compose.yml, and the lowered limit belongs to the smoke project only.

With -CheckDeployCompose it also resolves deploy/docker-compose.yml (the image-only UAT and production stack) with the committed UAT input template and dummy scoped env files:
"docker compose config --quiet" only. It never pulls an image and never starts that stack, which needs real images and an external Postgres.
.PARAMETER ProjectName
The compose project name. "techstrap" is refused, because that is the name of the stack you may be running.
.PARAMETER NoBuild
Do not rebuild the images; use the ones that exist (techstrap-smoke-*:local, left by an earlier run).
.PARAMETER KeepRunning
Leave the stack running after the checks (for looking at it); stop it later with: docker compose -p techstrap-smoke down
.PARAMETER CheckDeployCompose
Also run "docker compose config --quiet" on deploy/docker-compose.yml against a temporary copy of the UAT input template and dummy env files. Needs no images and no network.
.PARAMETER DeployComposeOnly
Run only the deploy compose check and nothing else: no build, no stack. Implies -CheckDeployCompose.
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
    [switch] $CheckDeployCompose,
    [switch] $DeployComposeOnly,
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
    environment:
      RateLimiting__Public__PermitLimit: "3"
      RateLimiting__Public__WindowSeconds: "600"
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
    environment:
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:-172.16.31.0/24}
  mailpit:
    ports: !override
      - "127.0.0.1::8025"
'@

# The public limit the override sets (RateLimiting__Public__PermitLimit above) and the two visitors the check pretends to be (documentation addresses, RFC 5737).
$smokePermitLimit = 3
$visitorA = '203.0.113.10'
$visitorB = '203.0.113.11'
$suggestUrl = 'http://portal/p/smoke/suggest?q=printer'

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

# One call to the Portal's suggest adapter from inside the compose network, as the given visitor; returns the HTTP status the Portal answered.
function Get-SuggestStatus {
    param([Parameter(Mandatory)][string] $Visitor)

    $code = Invoke-Compose -Arguments @('exec', '-T', 'api', 'curl', '--silent', '--output', '/dev/null', '--write-out', '%{http_code}', '--max-time', '30', '--header', "X-Forwarded-For: $Visitor", $suggestUrl)
    return [int] $code.Trim()
}

function Assert-StatusCode {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][int] $Actual, [Parameter(Mandatory)][int] $Expected)

    if ($Actual -ne $Expected) {
        throw "$Name answered $Actual (expected $Expected)."
    }

    Write-Output "ok   $Name -> $Expected"
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

# The deploy compose is only ever resolved here, never started: it needs real GHCR images and an external Postgres. The scoped env files are dummy copies of the committed
# templates in a temporary directory, which is also what makes "required: true" resolve.
function Test-DeployCompose {
    $deployFile = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..' 'deploy' 'docker-compose.yml')).Path
    $deployDirectory = Split-Path -Parent $deployFile
    $envDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "techstrap-deploy-check-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $envDirectory | Out-Null
    try {
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            Copy-Item -LiteralPath (Join-Path $deployDirectory ".env.$app.example") -Destination (Join-Path $envDirectory ".env.$app")
        }

        $portable = $envDirectory -replace '\\', '/'
        $inputs = Join-Path $envDirectory 'inputs.env'
        Get-Content -LiteralPath (Join-Path $deployDirectory '.env.uat.example') |
            ForEach-Object { if ($_ -like 'TECHSTRAP_ENV_DIR=*') { "TECHSTRAP_ENV_DIR=$portable" } else { $_ } } |
            Set-Content -LiteralPath $inputs
        $output = & docker compose --env-file $inputs -f $deployFile config --quiet 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "deploy/docker-compose.yml does not resolve with the UAT input template ($LASTEXITCODE):`n$output"
        }

        Write-Output 'ok   deploy/docker-compose.yml resolves (config only; nothing pulled or started)'
    }
    finally {
        Remove-Item -LiteralPath $envDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($DeployComposeOnly) {
    if ($DryRun) {
        Write-Output 'docker compose --env-file <UAT input template with dummy env files> -f deploy/docker-compose.yml config --quiet   (config only: no pull, no up)'
        return
    }

    if (-not (Get-Command docker -ErrorAction SilentlyContinue) -or ((& docker compose version 2>&1 | Out-String) -notmatch 'Docker Compose')) {
        throw 'Docker with the compose plugin is required.'
    }

    Test-DeployCompose
    return
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
    if ($CheckDeployCompose) {
        Write-Output "docker compose --env-file <UAT input template with dummy env files> -f deploy/docker-compose.yml config --quiet   (config only: no pull, no up)"
    }

    Write-Output "docker $($composeArguments -join ' ') port api 80"
    Write-Output "docker $($composeArguments -join ' ') port admin 80"
    Write-Output "docker $($composeArguments -join ' ') port portal 80"
    Write-Output "GET /health/ready on the Api, the Admin and the Portal, expecting 200"
    Write-Output "docker $($composeArguments -join ' ') ps admin --format json   (Health must be healthy)"
    $curl = "docker $($composeArguments -join ' ') exec -T api curl --silent --output /dev/null --write-out %{http_code} --header"
    Write-Output "$curl 'X-Forwarded-For: $visitorA' $suggestUrl   (X-Forwarded-For: $visitorA -> 200 three times, then 429: the Api counts the visitor)"
    Write-Output "$curl 'X-Forwarded-For: $visitorB' $suggestUrl   (X-Forwarded-For: $visitorB -> 200: another visitor is unaffected)"
    if (-not $KeepRunning) {
        Write-Output "docker $($composeArguments -join ' ') down"
    }

    return
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue) -or ((& docker compose version 2>&1 | Out-String) -notmatch 'Docker Compose')) {
    throw 'Docker with the compose plugin is required.'
}

if ($CheckDeployCompose) {
    Test-DeployCompose
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
    $portalPort = Get-PublishedPort -Service 'portal' -ContainerPort 80
    Assert-Ready -Name 'Api' -Port $apiPort
    Assert-Ready -Name 'Admin' -Port $adminPort
    Assert-Ready -Name 'Portal' -Port $portalPort

    $admin = (Invoke-Compose -Arguments @('ps', 'admin', '--format', 'json') | ConvertFrom-Json)
    if ($admin.Health -ne 'healthy') {
        throw "The Admin container reports health '$($admin.Health)' (expected healthy)."
    }

    Write-Output 'ok   Admin container is healthy'

    # The Api's rate limit must see the visitor behind the Portal (D-019): the limit is 3, so visitor A is refused on the fourth call and visitor B is not.
    for ($call = 1; $call -le $smokePermitLimit; $call++) {
        Assert-StatusCode -Name "suggest $call of $smokePermitLimit as $visitorA" -Actual (Get-SuggestStatus -Visitor $visitorA) -Expected 200
    }

    Assert-StatusCode -Name "suggest $($smokePermitLimit + 1) as $visitorA (over the limit)" -Actual (Get-SuggestStatus -Visitor $visitorA) -Expected 429
    Assert-StatusCode -Name "suggest 1 as $visitorB (another visitor)" -Actual (Get-SuggestStatus -Visitor $visitorB) -Expected 200
    $failed = $false
}
finally {
    if ($failed) {
        Write-Output (Invoke-Compose -Arguments @('logs', '--tail', '60', 'api', 'admin', 'portal') -AllowFailure)
    }

    if (-not $KeepRunning) {
        # Never -v: the volumes are not this script's to delete.
        Invoke-Compose -Arguments @('down') -AllowFailure | Out-Null
    }

    Remove-Item -LiteralPath $overridePath -Force -ErrorAction SilentlyContinue
}

Write-Output 'Compose smoke passed.'
