# The script under test deletes Docker resources and files. It is never run in place here: each scenario copies it into a
# fake repository under TestDrive and runs it in a child pwsh whose home is also under TestDrive and whose 'docker' is a
# function over an in-memory machine. The child refuses to start the script unless both fakes are in effect.
BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'Reset-LocalStack.ps1'
    $script:AppHostProject = Join-Path $script:RepoRoot 'tools' 'TechStrap.AppHost' 'TechStrap.AppHost.csproj'
    $script:SecretsId = ([xml](Get-Content -Raw $script:AppHostProject)).SelectSingleNode('//UserSecretsId').InnerText.Trim()

    $script:Harness = @'
param([string]$ScenarioDir)
$ErrorActionPreference = 'Stop'
$scenario = Get-Content -Raw (Join-Path $ScenarioDir 'scenario.json') | ConvertFrom-Json -AsHashtable
if ($HOME -ne (Join-Path $ScenarioDir 'home')) { Write-Host "HARNESS: HOME is $HOME, not the fake home"; exit 99 }

$global:Machine = $scenario.machine
$global:DockerLog = Join-Path $ScenarioDir 'docker.log'

function global:docker {
    $line = $args -join ' '
    # Inside a Where-Object block $args is the block's own.
    $target = $args[-1]
    Add-Content -LiteralPath $global:DockerLog -Value $line -WhatIf:$false
    $global:LASTEXITCODE = 0
    $m = $global:Machine
    if ($m.failOn -and $line -like $m.failOn) { $global:LASTEXITCODE = 1; return }
    switch -Wildcard ($line) {
        'volume ls --format *' { return $m.volumes }
        'network ls --format *' { return $m.networks }
        'ps -a --format *' { return @($m.containers | ForEach-Object { $_.name }) }
        'ps -a --filter volume=*' {
            $volume = ([string]$args[3]).Substring('volume='.Length)
            return @($m.containers | Where-Object { $_.mounts -ccontains $volume } | ForEach-Object { $_.name })
        }
        'ps -a --filter network=*' {
            $network = ([string]$args[3]).Substring('network='.Length)
            return @($m.containers | Where-Object { $_.networks -ccontains $network } | ForEach-Object { $_.name })
        }
        'inspect --type container --format *' {
            $container = @($m.containers | Where-Object { $_.name -ceq $target })[0]
            return ($container.mounts -join ' ')
        }
        'rm -f -v *' {
            $m.containers = @($m.containers | Where-Object { $_.name -cne $target })
            return
        }
        'network rm *' {
            $m.networks = @($m.networks | Where-Object { $_ -cne $target })
            return
        }
        'volume rm *' {
            if (@($m.containers | Where-Object { $_.mounts -ccontains $target }).Count -gt 0) {
                $global:LASTEXITCODE = 1
                return
            }
            if ($m.keepVolumes -cnotcontains $args[-1]) { $m.volumes = @($m.volumes | Where-Object { $_ -cne $target }) }
            return
        }
        default { throw "HARNESS: unexpected docker command: $line" }
    }
}
if ((Get-Command docker).CommandType -ne 'Function') { Write-Host 'HARNESS: docker is not the fake'; exit 99 }

$arguments = $scenario.arguments
try {
    & (Join-Path $ScenarioDir 'repo/scripts/Reset-LocalStack.ps1') @arguments
}
catch {
    Write-Host "SCRIPT ERROR: $($_.Exception.Message)"
    exit 1
}
'@

    $script:Network = 'aspire-persistent-network-0a1b2c3d-TechStrap'
    $script:AnonymousVolume = 'a1' * 32

    function New-Container([string]$Name, [string[]]$Mounts = @(), [string[]]$Networks = @()) {
        @{ name = $Name; mounts = @($Mounts); networks = @($Networks) }
    }

    # The neighbors, no volume of ours, and of our own stack only the containers given.
    function New-MachineWithoutOurVolumes([hashtable[]]$Left = @()) {
        $machine = New-Machine
        $machine.containers = @($machine.containers | Where-Object { $_.networks -cnotcontains $script:Network }) + $Left
        $machine.volumes = @($machine.volumes | Where-Object { $_ -cnotin 'techstrap-local-postgres', 'techstrap-local-authentik-media' })
        $machine
    }

    function Set-Mount([hashtable]$Machine, [string]$Name, [string[]]$Mounts) {
        @($Machine.containers | Where-Object { $_.name -ceq $Name })[0].mounts = @($Mounts)
    }

    # This stack plus the neighbors that share a developer machine with it. The suffix is made up on purpose.
    function New-Machine {
        @{
            containers = @(
                New-Container 'postgres-0a1b2c3d' @('techstrap-local-postgres') @($script:Network)
                New-Container 'mailpit-0a1b2c3d' @() @($script:Network)
                New-Container 'authentik-server-0a1b2c3d' @('techstrap-local-authentik-media') @($script:Network)
                New-Container 'authentik-worker-0a1b2c3d' @('techstrap-local-authentik-media', '') @($script:Network)
                # docker compose
                New-Container 'techstrap-api-1' @('techstrap_techstrap-storage')
                New-Container 'techstrap-postgres-1' @('techstrap_pgdata')
                New-Container 'techstrap-mailpit-1'
                # other Aspire stacks
                New-Container 'postgres-deadbeef' @('otherapp-local-postgres')
                New-Container 'mailpit-deadbeef'
                New-Container 'authentik-server-cafef00d' @('thirdapp-authentik-media')
                New-Container 'authentik-worker-cafef00d' @('thirdapp-authentik-media')
                New-Container 'otherapp-postgres-1' @('otherapp_postgres_data')
                # right suffix, wrong name
                New-Container 'redis-0a1b2c3d'
                New-Container 'postgres-0a1b2c3d-copy'
                New-Container 'Postgres-0a1b2c3d'
            )
            volumes = @(
                'techstrap-local-postgres', 'techstrap-local-authentik-media', 'techstrap_pgdata', 'techstrap_techstrap-storage',
                'techstrap_admin-keys', 'techstrap-smoke_pgdata', 'techstrap-local-postgres-old', 'thirdapp-local-postgres',
                'otherapp-local-postgres', 'thirdapp-authentik-media'
            )
            networks = @(
                'aspire-persistent-network-0a1b2c3d-TechStrap', 'aspire-persistent-network-deadbeef-OtherApp',
                'aspire-persistent-network-cafef00d-ThirdApp', 'aspire-persistent-network-badc0ffe-TechStrapTwo',
                'techstrap_default', 'bridge'
            )
            keepVolumes = @()
            failOn = $null
        }
    }

    function Invoke-Reset {
        param(
            [hashtable]$Machine = (New-Machine),
            [hashtable]$Arguments = @{ Force = $true },
            [ValidateSet('', 'IPv4', 'IPv6')]
            [string]$PortInUse = '',
            [switch]$StateIsLink
        )

        $dir = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $repo = Join-Path $dir 'repo'
        $fakeHome = Join-Path $dir 'home'
        $secrets = Join-Path $fakeHome ".aspire/secrets/$script:SecretsId"
        $otherSecrets = Join-Path $fakeHome '.aspire/secrets/11111111-2222-3333-4444-555555555555'
        foreach ($folder in @("$repo/scripts", "$repo/tools/TechStrap.AppHost", "$repo/local/.state/api", "$repo/local/other", $secrets, $otherSecrets)) {
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
        }
        Set-Content "$repo/local/.state/api/file.txt" 'state'
        Set-Content "$repo/local/other/keep.txt" 'keep'
        Set-Content "$secrets/Development.json" '{}'
        Set-Content "$otherSecrets/Development.json" '{}'
        Copy-Item $script:AppHostProject "$repo/tools/TechStrap.AppHost/"
        $linkTarget = Join-Path $dir 'elsewhere'
        if ($StateIsLink) {
            Move-Item "$repo/local/.state" $linkTarget
            # A junction needs no elevation on Windows; elsewhere it is a symbolic link.
            New-Item -ItemType ($IsWindows ? 'Junction' : 'SymbolicLink') -Path "$repo/local/.state" -Target $linkTarget | Out-Null
        }

        # The copy asks about a port this test owns, so a stack running on this machine cannot change the result.
        $address = $PortInUse -eq 'IPv6' ? [System.Net.IPAddress]::IPv6Loopback : [System.Net.IPAddress]::Loopback
        $listener = [System.Net.Sockets.TcpListener]::new($address, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        if (-not $PortInUse) { $listener.Stop() }

        $source = Get-Content -Raw $script:ScriptPath
        $source | Should -Match '\$dashboardPort = 55200\b'
        $source = $source -replace '\$dashboardPort = 55200\b', "`$dashboardPort = $port"
        if (-not $PortInUse) {
            # Windows takes about two seconds to refuse a closed port, so with nothing listening the script's probe would
            # spend its whole wait in every such scenario. With a listener the copy keeps the full wait.
            $probeWait = '(?<=\[Math\]::Max\(0, )1000(?= - \$waited\.ElapsedMilliseconds\))'
            $source | Should -Match $probeWait
            $source = $source -replace $probeWait, '50'
        }
        Set-Content "$repo/scripts/Reset-LocalStack.ps1" $source

        Set-Content "$dir/harness.ps1" $script:Harness
        @{ machine = $Machine; arguments = $Arguments } | ConvertTo-Json -Depth 6 | Set-Content "$dir/scenario.json"

        $saved = @{ HOME = $env:HOME; USERPROFILE = $env:USERPROFILE }
        try {
            $env:HOME = $fakeHome
            $env:USERPROFILE = $fakeHome
            $output = & (Get-Process -Id $PID).Path -NoProfile -NonInteractive -File "$dir/harness.ps1" $dir 2>&1 | Out-String
            $code = $LASTEXITCODE
        }
        finally {
            $env:HOME = $saved.HOME
            $env:USERPROFILE = $saved.USERPROFILE
            if ($PortInUse) { $listener.Stop() }
        }
        $code | Should -Not -Be 99 -Because $output

        $log = @(if (Test-Path "$dir/docker.log") { Get-Content "$dir/docker.log" })
        [pscustomobject]@{
            Code = $code
            Output = $output
            Log = $log
            # Every call that changes the fake machine.
            Removals = @($log | Where-Object { $_ -cmatch '^(rm|volume rm|network rm) ' })
            StateExists = Test-Path "$repo/local/.state"
            LinkTargetKept = Test-Path "$linkTarget/api/file.txt"
            OtherLocalExists = Test-Path "$repo/local/other/keep.txt"
            SecretsExist = Test-Path $secrets
            OtherSecretsExist = Test-Path "$otherSecrets/Development.json"
        }
    }

    $script:ExpectedRemovals = @(
        'rm -f -v postgres-0a1b2c3d'
        'rm -f -v mailpit-0a1b2c3d'
        'rm -f -v authentik-server-0a1b2c3d'
        'rm -f -v authentik-worker-0a1b2c3d'
        'network rm aspire-persistent-network-0a1b2c3d-TechStrap'
        'volume rm techstrap-local-postgres'
        'volume rm techstrap-local-authentik-media'
    )
}

Describe 'Reset-LocalStack.ps1 selection' {
    It 'removes exactly this stack and nothing else' {
        $result = Invoke-Reset
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -Be $script:ExpectedRemovals
        $result.StateExists | Should -BeFalse
        $result.OtherLocalExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
        $result.OtherSecretsExist | Should -BeTrue
        $result.Output | Should -Match 'Keeping generated secrets'
    }

    It 'never forces a volume out and never prunes' {
        $result = Invoke-Reset -Arguments @{ Force = $true; Secrets = $true }
        @($result.Log | Where-Object { $_ -match 'prune|^volume rm -|compose' }) | Should -BeNullOrEmpty
    }

    It 'selects no container when neither a volume nor the network proves a suffix' {
        $machine = New-Machine
        $machine.containers = @($machine.containers | Where-Object { $_.name -notmatch '^(postgres|authentik-(server|worker))-0a1b2c3d$' })
        $machine.volumes = @($machine.volumes | Where-Object { $_ -cnotin 'techstrap-local-postgres', 'techstrap-local-authentik-media' })
        $machine.networks = @($machine.networks | Where-Object { $_ -cne 'aspire-persistent-network-0a1b2c3d-TechStrap' })
        $result = Invoke-Reset -Machine $machine
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -BeNullOrEmpty
    }

    It 'removes an orphan network when no container carries its suffix' {
        $result = Invoke-Reset -Machine (New-MachineWithoutOurVolumes)
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -Be @("network rm $script:Network")
    }

    It 'refuses when only the network vouches for a mailpit, and says how to clean up' {
        $machine = New-MachineWithoutOurVolumes @(New-Container 'mailpit-0a1b2c3d' @() @($script:Network))
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match 'mailpit-0a1b2c3d: no container with the suffix 0a1b2c3d mounts a volume of this stack'
        $result.Output | Should -Match ([regex]::Escape('docker rm -f mailpit-0a1b2c3d'))
        $result.Output | Should -Match 'run this script again'
        $result.Output | Should -Match 'Nothing was removed'
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }

    It 'refuses a postgres that only the network vouches for (<Why>)' -ForEach @(
        @{ Why = 'foreign volume'; Mounts = @('other-app-data') }
        @{ Why = 'anonymous volume'; Mounts = @('a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1') }
        @{ Why = 'no volume'; Mounts = @() }
    ) {
        $machine = New-MachineWithoutOurVolumes @(New-Container 'postgres-0a1b2c3d' $Mounts @($script:Network))
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match 'postgres-0a1b2c3d: does not mount techstrap-local-postgres'
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }

    It 'removes nothing when a container of our suffix breaks its mount rule (<Name>: <Problem>)' -ForEach @(
        @{ Name = 'postgres-0a1b2c3d'; Mounts = @('techstrap-local-authentik-media'); Problem = 'does not mount techstrap-local-postgres' }
        @{ Name = 'authentik-server-0a1b2c3d'; Mounts = @(); Problem = 'does not mount techstrap-local-authentik-media' }
        @{ Name = 'authentik-worker-0a1b2c3d'; Mounts = @('techstrap-local-postgres'); Problem = 'does not mount techstrap-local-authentik-media' }
        @{ Name = 'postgres-0a1b2c3d'; Mounts = @('techstrap-local-postgres', 'other-app-data'); Problem = 'mounts other-app-data' }
        @{ Name = 'mailpit-0a1b2c3d'; Mounts = @('other-mail-data'); Problem = 'mounts other-mail-data' }
        @{ Name = 'authentik-worker-0a1b2c3d'; Mounts = @('techstrap-local-authentik-media', 'techstrap_pgdata'); Problem = 'mounts techstrap_pgdata' }
    ) {
        $machine = New-Machine
        Set-Mount $machine $Name $Mounts
        foreach ($arguments in @(@{ Force = $true; Secrets = $true }, @{ WhatIf = $true })) {
            $result = Invoke-Reset -Machine $machine -Arguments $arguments
            $result.Code | Should -Be 1 -Because $result.Output
            $result.Output | Should -Match "${Name}: $Problem"
            $result.Removals | Should -BeNullOrEmpty
            $result.StateExists | Should -BeTrue
            $result.SecretsExist | Should -BeTrue
        }
    }

    It 'accepts anonymous volumes and binds beside the required mounts' {
        $machine = New-Machine
        Set-Mount $machine 'postgres-0a1b2c3d' @('techstrap-local-postgres', $script:AnonymousVolume)
        Set-Mount $machine 'mailpit-0a1b2c3d' @($script:AnonymousVolume, '')
        $result = Invoke-Reset -Machine $machine
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -Be $script:ExpectedRemovals
    }

    It 'accepts a half-started stack in which one volume proves the mailpit' {
        $machine = New-Machine
        $machine.containers = @($machine.containers | Where-Object { $_.name -notlike 'authentik-*-0a1b2c3d' })
        $machine.volumes = @($machine.volumes | Where-Object { $_ -cne 'techstrap-local-authentik-media' })
        $result = Invoke-Reset -Machine $machine
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -Be @(
            'rm -f -v postgres-0a1b2c3d', 'rm -f -v mailpit-0a1b2c3d', "network rm $script:Network", 'volume rm techstrap-local-postgres')
    }

    It 'removes nothing when a container outside this stack is attached to its network (<Name>)' -ForEach @(
        @{ Name = 'my-proxy' }
        @{ Name = 'postgres-deadbeef' }
    ) {
        $machine = New-Machine
        $machine.containers = @($machine.containers | Where-Object { $_.name -cne $Name }) +
            (New-Container $Name @() @($script:Network))
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match "The network $script:Network also has: $Name"
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }

    It 'only warns about an attached outsider under -WhatIf' {
        $machine = New-Machine
        $machine.containers += New-Container 'my-proxy' @() @($script:Network)
        $result = Invoke-Reset -Machine $machine -Arguments @{ WhatIf = $true }
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Output | Should -Match 'also has: my-proxy'
        $result.Removals | Should -BeNullOrEmpty
    }

    It 'removes nothing, and follows no link, when local/.state is a link' {
        $result = Invoke-Reset -StateIsLink -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1 -Because $result.Output
        $result.Output | Should -Match 'is a link'
        $result.Removals | Should -BeNullOrEmpty
        $result.LinkTargetKept | Should -BeTrue
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }

    It 'takes the suffix from a mounted volume when the network is gone' {
        $machine = New-Machine
        $machine.networks = @($machine.networks | Where-Object { $_ -cne 'aspire-persistent-network-0a1b2c3d-TechStrap' })
        $result = Invoke-Reset -Machine $machine
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -Be @($script:ExpectedRemovals | Where-Object { $_ -notlike 'network *' })
    }

    It 'removes nothing when two suffixes claim to be this stack (<Why>)' -ForEach @(
        @{ Why = 'a second network'; Network = 'aspire-persistent-network-99999999-TechStrap'; Container = $null }
        @{ Why = 'a second container on our volume'; Network = $null; Container = 'postgres-99999999' }
    ) {
        $machine = New-Machine
        if ($Network) { $machine.networks += $Network }
        if ($Container) { $machine.containers += New-Container $Container @('techstrap-local-postgres') }
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1
        $result.Output | Should -Match 'more than one AppHost suffix'
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }

    It 'removes nothing when a container outside this stack uses one of its volumes' {
        $machine = New-Machine
        $machine.containers += New-Container 'my-debug-shell' @('techstrap-local-postgres')
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1
        $result.Output | Should -Match 'my-debug-shell'
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }

    It 'removes nothing when docker cannot answer (<FailOn>)' -ForEach @(
        @{ FailOn = 'volume ls*' }
        @{ FailOn = 'ps -a --format*' }
        @{ FailOn = 'network ls*' }
        @{ FailOn = 'inspect *' }
        @{ FailOn = 'ps -a --filter volume=*' }
        @{ FailOn = 'ps -a --filter network=*' }
    ) {
        $machine = New-Machine
        $machine.failOn = $FailOn
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }
}

Describe 'Reset-LocalStack.ps1 switches' {
    It 'removes nothing under -WhatIf, and names what it would remove' {
        $result = Invoke-Reset -Arguments @{ WhatIf = $true; Secrets = $true; Force = $true }
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
        $targets = @([regex]::Matches($result.Output, 'on target "([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
        $targets.Count | Should -Be 9 -Because $result.Output
        foreach ($removal in $script:ExpectedRemovals) { $targets | Should -Contain ($removal -split ' ')[-1] }
        @($targets | Where-Object { $_ -match '[\\/]local[\\/]\.state$' }).Count | Should -Be 1
        @($targets | Where-Object { $_.EndsWith($script:SecretsId) }).Count | Should -Be 1
    }

    It 'asks before removing, so a non-interactive host without -Force removes nothing' {
        $result = Invoke-Reset -Arguments @{}
        $result.Code | Should -Be 1 -Because $result.Output
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
    }

    It 'still asks when -Confirm is given with -Force' {
        $result = Invoke-Reset -Arguments @{ Force = $true; Confirm = $true }
        $result.Code | Should -Be 1 -Because $result.Output
        $result.Removals | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
    }

    It 'does not ask under -Confirm:$false' {
        $result = Invoke-Reset -Arguments @{ Confirm = $false }
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Removals | Should -Be $script:ExpectedRemovals
    }

    It 'refuses while the dashboard port is in use on <Family>' -ForEach @(@{ Family = 'IPv4' }, @{ Family = 'IPv6' }) {
        $result = Invoke-Reset -PortInUse $Family -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1
        $result.Output | Should -Match 'appears to be running'
        $result.Log | Should -BeNullOrEmpty
        $result.StateExists | Should -BeTrue
        $result.SecretsExist | Should -BeTrue
    }

    It 'only warns about a running stack under -WhatIf' {
        $result = Invoke-Reset -PortInUse IPv4 -Arguments @{ WhatIf = $true }
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Output | Should -Match 'appears to be running'
        $result.Removals | Should -BeNullOrEmpty
    }

    It 'deletes only this AppHost''s secrets with -Secrets' {
        $result = Invoke-Reset -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 0 -Because $result.Output
        $result.SecretsExist | Should -BeFalse
        $result.OtherSecretsExist | Should -BeTrue
    }

    It 'keeps the secrets when the database volume survives, because a new password would not open it' {
        $machine = New-Machine
        $machine.keepVolumes = @('techstrap-local-postgres')
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 0 -Because $result.Output
        $result.SecretsExist | Should -BeTrue
        $result.Output | Should -Match 'Keeping the generated secrets: the volume techstrap-local-postgres still exists'
    }

    It 'points to docker''s own error when a removal fails, and does not only blame a stopped Docker' {
        $machine = New-Machine
        $machine.failOn = 'volume rm techstrap-local-postgres'
        $result = Invoke-Reset -Machine $machine
        $result.Code | Should -Be 1 -Because $result.Output
        $output = $result.Output -replace '\s+', ' '
        $output | Should -Match 'docker volume rm techstrap-local-postgres failed'
        $output | Should -Match "Docker's own error above says why"
        $output | Should -Match 'a container you chose to keep still uses it'
    }

    It 'keeps the secrets when a volume cannot be removed' {
        $machine = New-Machine
        $machine.failOn = 'volume rm techstrap-local-postgres'
        $result = Invoke-Reset -Machine $machine -Arguments @{ Force = $true; Secrets = $true }
        $result.Code | Should -Be 1
        $result.SecretsExist | Should -BeTrue
    }
}

Describe 'Reset-LocalStack.ps1 inputs' {
    It 'requires PowerShell 7.2, because Windows PowerShell deletes through junctions' {
        Get-Content $script:ScriptPath | Should -Contain '#Requires -Version 7.2'
    }

    It 'checks the port of the AppHost dashboard' {
        $ports = Get-Content -Raw (Join-Path $script:RepoRoot 'tools/TechStrap.AppHost/LocalStackPorts.cs')
        $ports | Should -Match 'Dashboard = 55200;'
        Get-Content -Raw $script:ScriptPath | Should -Match '\$dashboardPort = 55200\b'
    }

    It 'names the volumes the AppHost creates and no others' {
        $sources = @('LocalStackComposition.cs', 'Authentik/AuthentikResources.cs') |
            ForEach-Object { Get-Content -Raw (Join-Path $script:RepoRoot "tools/TechStrap.AppHost/$_") }
        $created = @([regex]::Matches(($sources -join "`n"), 'With(?:Data)?Volume\("([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $created | Should -Be @('techstrap-local-authentik-media', 'techstrap-local-postgres')
        $text = Get-Content -Raw $script:ScriptPath
        $named = @([regex]::Matches($text, "'(techstrap-local-[a-z-]+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $named | Should -Be $created
    }

    It 'has the AppHost tooling outside the container build context' {
        Get-Content (Join-Path $script:RepoRoot '.dockerignore') | Should -Contain 'tools/'
    }
}
