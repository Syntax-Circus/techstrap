# Windows PowerShell 5.1's Remove-Item follows junctions into their targets.
#Requires -Version 7.2

<#
.SYNOPSIS
    Resets the local Aspire stack to a clean slate.
.DESCRIPTION
    Removes this repository's persistent containers (postgres, mailpit, authentik-server, authentik-worker), their
    persistent Aspire network, the volumes techstrap-local-postgres and techstrap-local-authentik-media, and
    local/.state/. All local data goes: the TechStrap database, Authentik's database and media, stored attachments and
    the key rings. Captured mail goes with the mailpit container.

    The generated secrets in ~/.aspire/secrets/<UserSecretsId>/ are kept unless -Secrets is passed. They are deleted
    only after the database volume is gone: a new Postgres password does not open an old volume, and Authentik's database
    (in that volume) belongs to the old secret key and client secret. Your own AppHost user secrets (set with
    'dotnet user-secrets', for example LocalStack:SeedDevData) are stored elsewhere and are never touched.
    Nothing else is touched either: not other projects' containers, networks or volumes, not the docker compose stacks
    (the techstrap_* and techstrap-smoke_* volumes), and nothing else under local/.

    Containers are selected by the 8-hex suffix of this stack's network or of a container that mounts one of its
    volumes, and each must then look like this stack's: postgres mounts techstrap-local-postgres, the Authentik server
    and worker mount techstrap-local-authentik-media, mailpit shares its suffix with a container that mounts one of
    them, and none mounts any other named volume.

    The script stops before it removes anything when a selected container fails those rules, when there is more than
    one suffix, when something else uses one of the volumes or is attached to the network, or when local/.state is a
    link. A half-created stack can fail the rules (for example only mailpit is left); the message says what to remove
    by hand.

    Stop the AppHost first (aspire stop, or Ctrl+C in aspire run); its persistent containers keep running, and this
    script removes them. Supports -WhatIf. Each removal asks for confirmation (ConfirmImpact High); pass -Force or
    -Confirm:$false to skip the prompts, which a non-interactive host requires.
.EXAMPLE
    ./scripts/Reset-LocalStack.ps1 -WhatIf
.EXAMPLE
    ./scripts/Reset-LocalStack.ps1 -Secrets -Force
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    # Also delete the generated secrets, so new passwords and keys are generated on the next run.
    [switch]$Secrets,

    # Skip the per-item confirmation prompts (for non-interactive hosts, or shells that mangle -Confirm:$false).
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# An explicit -Confirm wins over -Force.
if ($Force -and -not $PSBoundParameters.ContainsKey('Confirm')) { $ConfirmPreference = 'None' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$appHostProject = Join-Path $repoRoot 'tools/TechStrap.AppHost/TechStrap.AppHost.csproj'
$stateDir = Join-Path $repoRoot 'local/.state'
# LocalStackPorts.Dashboard.
$dashboardPort = 55200

# Aspire adds an 8-hex suffix, one per AppHost, to persistent container names (for example postgres-0123abcd). Other
# Aspire projects on this machine have containers with the same base names, so a name alone never selects anything.
$containerPattern = '^(postgres|mailpit|authentik-server|authentik-worker)-[0-9a-f]{8}$'
# The same suffix, followed by this AppHost's name. The whole name must match.
$networkPattern = '^aspire-persistent-network-[0-9a-f]{8}-TechStrap$'
# Exact names, never a prefix: docker compose's techstrap_* and techstrap-smoke_* volumes are not this stack's.
$databaseVolume = 'techstrap-local-postgres'
$mediaVolume = 'techstrap-local-authentik-media'
$volumes = @($databaseVolume, $mediaVolume)
# What the AppHost makes each container mount. Mailpit mounts none of them.
$requiredVolume = @{ 'postgres' = $databaseVolume; 'authentik-server' = $mediaVolume; 'authentik-worker' = $mediaVolume }
# Docker names an anonymous volume with 64 hex digits.
$anonymousVolumePattern = '^[0-9a-f]{64}$'

$userSecretsIds = @(([xml](Get-Content -Raw $appHostProject)).SelectNodes('//UserSecretsId') |
    ForEach-Object { $_.InnerText.Trim() })
# An empty or odd value here would point -Secrets at every project's secrets.
$parsedId = [guid]::Empty
if ($userSecretsIds.Count -ne 1 -or -not [guid]::TryParseExact($userSecretsIds[0], 'D', [ref]$parsedId)) {
    throw "Expected exactly one UserSecretsId GUID in $appHostProject. Nothing was removed."
}
$secretsDir = Join-Path $HOME ".aspire/secrets/$($userSecretsIds[0])"

# Removing volumes under a running stack would leave it broken. Both loopbacks are probed at once, with a bounded wait
# (Windows takes about two seconds to refuse a closed port): no answer in time counts as not running.
$probes = @(foreach ($address in [System.Net.IPAddress]::Loopback, [System.Net.IPAddress]::IPv6Loopback) {
    try {
        $client = [System.Net.Sockets.TcpClient]::new($address.AddressFamily)
        [pscustomobject]@{ Client = $client; Task = $client.ConnectAsync($address, $dashboardPort) }
    }
    catch [System.Net.Sockets.SocketException] {
        # No such loopback on this machine, so nothing listens there.
    }
})
try {
    $waited = [System.Diagnostics.Stopwatch]::StartNew()
    foreach ($probe in $probes) {
        # Wait throws when the connection fails; the outcome is read from the task below.
        try { $null = $probe.Task.Wait([int][Math]::Max(0, 1000 - $waited.ElapsedMilliseconds)) }
        catch [System.AggregateException] { }
        if ($probe.Task.IsFaulted -and $probe.Task.Exception.GetBaseException() -isnot [System.Net.Sockets.SocketException]) {
            throw "Could not check port $dashboardPort`: $($probe.Task.Exception.GetBaseException().Message) Nothing was removed."
        }
    }
    $stackRunning = @($probes | Where-Object { $_.Task.IsCompletedSuccessfully }).Count -gt 0
}
finally {
    foreach ($probe in $probes) { $probe.Client.Dispose() }
}
if ($stackRunning) {
    if (-not $WhatIfPreference) {
        throw "The local stack appears to be running (port $dashboardPort is in use). Stop it and retry. Nothing was removed."
    }
    Write-Warning "The local stack appears to be running (port $dashboardPort is in use). Stop it before a real reset."
}

# Deleting through a link would delete whatever it points to.
$stateItem = Get-Item -LiteralPath $stateDir -Force -ErrorAction Ignore
if ($stateItem -and $stateItem.LinkType) {
    throw "$stateDir is a link ($($stateItem.LinkType)), which this script does not follow. Nothing was removed. Remove the link itself, then run this script again."
}

# A failed query must stop the script: an empty answer would look like "nothing to remove".
function Get-DockerLine {
    param([string[]]$Arguments)

    $lines = @(& docker @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed (exit $LASTEXITCODE). The reset stopped here. Docker's own error above says why: for example, Docker is not running, or a container you chose to keep still uses it."
    }
    return $lines | Where-Object { $_ }
}

# Everything is selected and checked before anything is removed.
$existingVolumes = @(Get-DockerLine @('volume', 'ls', '--format', '{{.Name}}'))
$ourVolumes = @($volumes | Where-Object { $existingVolumes -ccontains $_ })
$candidates = @(Get-DockerLine @('ps', '-a', '--format', '{{.Names}}') | Where-Object { $_ -cmatch $containerPattern })
$ourNetworks = @(Get-DockerLine @('network', 'ls', '--format', '{{.Name}}') | Where-Object { $_ -cmatch $networkPattern })

# Our suffix is the one of a candidate that mounts one of our volumes, or of our network. The network alone only picks
# which containers to check: the mount rules below decide whether they are ours.
$mountsByName = @{}
$mountingSuffixes = @(foreach ($name in $candidates) {
    # A bind mount has no name.
    $mounts = @((@(Get-DockerLine @('inspect', '--type', 'container', '--format', '{{range .Mounts}}{{.Name}} {{end}}', $name)) -join ' ') -split '\s+' |
        Where-Object { $_ })
    $mountsByName[$name] = $mounts
    if (@($mounts | Where-Object { $volumes -ccontains $_ }).Count -gt 0) { $name -replace '^.*-([0-9a-f]{8})$', '$1' }
})
$networkSuffixes = @($ourNetworks | ForEach-Object { $_ -replace '^aspire-persistent-network-([0-9a-f]{8})-TechStrap$', '$1' })
$suffixes = @(@($mountingSuffixes + $networkSuffixes) | Sort-Object -Unique)

# Two suffixes mean two AppHosts (for example a second clone or a worktree) and no way to tell whose data this is.
if ($suffixes.Count -gt 1) {
    throw "Found more than one AppHost suffix for this stack ($($suffixes -join ', ')). Nothing was removed; inspect 'docker ps -a' and 'docker network ls' before retrying."
}

$containers = @(if ($suffixes.Count -eq 1) { $candidates | Where-Object { $_.EndsWith("-$($suffixes[0])", [StringComparison]::Ordinal) } })
if ($containers.Count -eq 0) {
    Write-Host 'No local stack containers found.'
}

# Another Aspire app that is also called TechStrap has the same names, and 'rm -v' would delete its anonymous volumes.
# One container that fails a rule stops everything, under -WhatIf too.
$rejected = [ordered]@{}
$problems = @(foreach ($name in $containers) {
    $role = $name -replace '-[0-9a-f]{8}$', ''
    $suffix = $name.Substring($name.Length - 8)
    $mounts = @($mountsByName[$name])
    $reasons = @(
        $foreign = @($mounts | Where-Object { $_ -cnotmatch $anonymousVolumePattern -and $volumes -cnotcontains $_ })
        if ($foreign.Count -gt 0) { "mounts $($foreign -join ', '), which is not this stack's" }
        if ($requiredVolume.ContainsKey($role)) {
            if ($mounts -cnotcontains $requiredVolume[$role]) { "does not mount $($requiredVolume[$role])" }
        }
        elseif ($mountingSuffixes -cnotcontains $suffix) {
            "no container with the suffix $suffix mounts a volume of this stack"
        }
    )
    foreach ($reason in $reasons) {
        $rejected[$name] = $true
        "  ${name}: $reason"
    }
})
if ($problems.Count -gt 0) {
    $names = @($rejected.Keys) -join ' '
    throw (@(
        "Nothing was removed: these containers cannot be shown to be this stack's."
        $problems
        'A first start that created only part of the stack, or volumes removed by hand, can cause this. So can another Aspire app that is also called TechStrap.'
        "Check each one with 'docker inspect --type container <name>'."
        "If they are leftovers of this stack, remove them with 'docker rm -f $names' (without -v, so anonymous volumes survive) and run this script again."
        "If any belongs to another project, leave it alone and remove this stack's own containers, network and volumes ($($volumes -join ', ')) by hand."
    ) -join [Environment]::NewLine)
}

# A volume that something else uses is not only ours, and finding that out halfway would leave half a stack.
foreach ($volume in $ourVolumes) {
    $others = @(Get-DockerLine @('ps', '-a', '--filter', "volume=$volume", '--format', '{{.Names}}') |
        Where-Object { $containers -cnotcontains $_ })
    if ($others.Count -eq 0) { continue }
    $message = "The volume $volume is also used by: $($others -join ', '), which this script does not remove."
    if (-not $WhatIfPreference) { throw "$message Nothing was removed." }
    Write-Warning "$message A real reset would stop here."
}

# 'ps -a' because 'network inspect' lists only running containers.
foreach ($network in $ourNetworks) {
    $others = @(Get-DockerLine @('ps', '-a', '--filter', "network=$network", '--format', '{{.Names}}') |
        Where-Object { $containers -cnotcontains $_ })
    if ($others.Count -eq 0) { continue }
    $message = "The network $network also has: $($others -join ', '), which this script does not remove. Check them, disconnect or remove them yourself, and run this script again."
    if (-not $WhatIfPreference) { throw "$message Nothing was removed." }
    Write-Warning "$message A real reset would stop here."
}

foreach ($name in $containers) {
    if ($PSCmdlet.ShouldProcess($name, 'Stop and remove container')) {
        # -v removes only the container's anonymous volumes; named volumes are handled below.
        Get-DockerLine @('rm', '-f', '-v', $name) | Out-Null
    }
}

foreach ($network in $ourNetworks) {
    if ($PSCmdlet.ShouldProcess($network, 'Remove network')) {
        Get-DockerLine @('network', 'rm', $network) | Out-Null
    }
}

foreach ($volume in $ourVolumes) {
    if ($PSCmdlet.ShouldProcess($volume, 'Remove volume')) {
        # No -f: a volume that something still uses must not disappear.
        Get-DockerLine @('volume', 'rm', $volume) | Out-Null
    }
}

# Only local/.state: the rest of local/ is not this stack's.
if (Test-Path -LiteralPath $stateDir) {
    if ($PSCmdlet.ShouldProcess($stateDir, 'Delete local state folder')) {
        Remove-Item -LiteralPath $stateDir -Recurse -Force
    }
}

if (-not (Test-Path -LiteralPath $secretsDir)) { return }
if (-not $Secrets) {
    Write-Host "Keeping generated secrets in $secretsDir (pass -Secrets to delete them)."
    return
}
# Asked again: the volume's removal may have been declined at its prompt.
if (-not $WhatIfPreference -and (@(Get-DockerLine @('volume', 'ls', '--format', '{{.Name}}')) -ccontains $databaseVolume)) {
    Write-Warning "Keeping the generated secrets: the volume $databaseVolume still exists, and new passwords would not open it. Remove the volume too."
    return
}
if ($PSCmdlet.ShouldProcess($secretsDir, 'Delete generated secrets')) {
    Remove-Item -LiteralPath $secretsDir -Recurse -Force
}
