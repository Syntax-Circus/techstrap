BeforeDiscovery {
    $script:DockerAvailable = $null -ne (Get-Command docker -ErrorAction SilentlyContinue) -and
        ((& docker compose version 2>&1 | Out-String) -match 'Docker Compose')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:PinnedSubnet = '172.16.31.0/24'
    $script:DeployCompose = 'deploy/docker-compose.yml'

    function Get-ComposeConfig {
        param([string]$File, [string]$EnvFile = '', [switch]$NoEnvResolution)

        $arguments = @('compose')
        if ($EnvFile) { $arguments += @('--env-file', $EnvFile) }
        $arguments += @('-f', (Join-Path $script:RepoRoot $File), 'config')
        if ($NoEnvResolution) { $arguments += '--no-env-resolution' }
        $arguments += @('--format', 'json')

        $output = & docker @arguments 2>&1 | Out-String
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output   = $output
            Config   = if ($LASTEXITCODE -eq 0) { $output | ConvertFrom-Json } else { $null }
        }
    }

    # A host directory of scoped env files (copies of the committed templates, which is what an operator starts from) and a compose inputs file that points at it.
    # The compose inputs come from deploy/.env.<env>.example with TECHSTRAP_ENV_DIR replaced; -Without drops one variable so a test can show that compose refuses.
    function New-DeployInputs {
        param([string]$Directory, [ValidateSet('uat', 'production')][string]$Environment = 'uat', [string[]]$Without = @(), [string[]]$WithoutEnvFile = @())

        $envDirectory = Join-Path $Directory "env-$Environment"
        New-Item -ItemType Directory -Path $envDirectory -Force | Out-Null
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            if ($app -in $WithoutEnvFile) { continue }
            Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$app.example") -Destination (Join-Path $envDirectory ".env.$app")
        }

        $portableDirectory = $envDirectory -replace '\\', '/'
        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$Environment.example") |
            ForEach-Object { if ($_ -like 'TECHSTRAP_ENV_DIR=*') { "TECHSTRAP_ENV_DIR=$portableDirectory" } else { $_ } } |
            Where-Object { $name = ($_ -split '=', 2)[0]; $name -notin $Without }
        $inputs = Join-Path $Directory "inputs-$Environment.env"
        Set-Content -LiteralPath $inputs -Value $lines
        return [pscustomobject]@{ Inputs = $inputs; EnvDirectory = $envDirectory; PortableDirectory = $portableDirectory }
    }

    function Get-InputValue {
        param([string]$Environment, [string]$Name)
        $line = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$Environment.example") | Where-Object { $_ -like "$Name=*" } | Select-Object -First 1
        return ($line -split '=', 2)[1]
    }

    Remove-Item Env:TECHSTRAP_SUBNET -ErrorAction SilentlyContinue
}

Describe 'the local docker-compose.yml' -Skip:(-not $script:DockerAvailable) {
    It 'local compose pins the subnet and the API trusts it' {
        $result = Get-ComposeConfig -File 'docker-compose.yml'
        $result.ExitCode | Should -Be 0
        $result.Config.networks.default.ipam.config[0].subnet | Should -Be $script:PinnedSubnet
        $result.Config.services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be $script:PinnedSubnet
    }

    It 'local compose has the four app services plus Postgres 17 and never trusts a wide range' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        ($config.services.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin', 'api', 'mailpit', 'portal', 'postgres', 'worker')
        $config.services.postgres.image | Should -Be 'postgres:17'
        foreach ($service in 'api', 'admin', 'portal') {
            $config.services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Not -Match '^(172\.16\.0\.0/12|0\.0\.0\.0/0)$'
        }
    }

    It 'local mailpit publishes its web UI on loopback only and no SMTP port' {
        $mailpit = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.mailpit
        $ports = @($mailpit.ports)
        $ports.Count | Should -Be 1
        [int]$ports[0].target | Should -Be 8025
        $ports[0].host_ip | Should -Be '127.0.0.1'
        [int]$ports[0].published | Should -Be 8025
        @($ports | Where-Object { [int]$_.target -eq 1025 }).Count | Should -Be 0
    }

    It 'local worker sends through mailpit without TLS and with outbox-safe retries' {
        $worker = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.worker
        $worker.environment.Email__Smtp__Host | Should -Be 'mailpit'
        $worker.environment.Email__Smtp__Port | Should -Be '1025'
        $worker.environment.Email__Smtp__TlsMode | Should -Be 'None'
        $worker.environment.Email__Smtp__MaxRetryAttempts | Should -Be '1'
        $worker.environment.Email__Smtp__RetryMode | Should -Be 'TransientOnly'
        $worker.depends_on.mailpit.condition | Should -Be 'service_healthy'
    }

    It 'local compose mounts the storage volume on the api only (the worker registers no attachment storage)' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        @($config.services.api.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        foreach ($service in 'worker', 'admin', 'portal') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' }).Count | Should -Be 0
        }
    }

    It 'local compose builds every app image from this checkout and loads an optional per-host .env.local' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml' -NoEnvResolution).Config
        foreach ($service in 'api', 'worker', 'admin', 'portal') {
            $config.services.$service.build.dockerfile | Should -Be "Dockerfile.$service"
            @($config.services.$service.env_file | Where-Object { $_.path -match "(?i)src[\\/]TechStrap\.$service[\\/]\.env\.local$" }).Count | Should -Be 1
            @($config.services.$service.env_file)[0].required | Should -BeFalse
        }
    }

    It 'local compose still resolves when every compose input is left out (the dev stack needs no .env)' {
        (Get-ComposeConfig -File 'docker-compose.yml').ExitCode | Should -Be 0
    }

    It 'local compose starts the Admin only after a healthy Api and gives it a health check on /health/live and a persistent key ring' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin

        $admin.depends_on.api.condition | Should -Be 'service_healthy'
        ($admin.healthcheck.test -join ' ') | Should -Match '/health/live'
        @($admin.volumes | Where-Object { $_.target -eq '/app/dataprotection-keys' }).Count | Should -Be 1
    }

    It 'local compose leaves the Admin sign-in and the group keys to .env.local and appsettings.Development.json, so the two no longer clash' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin
        $names = @($admin.environment.PSObject.Properties.Name)
        @($names | Where-Object { $_ -like 'Auth__*' -or $_ -like 'TECHSTRAP_*' }) | Should -BeNullOrEmpty
        $admin.environment.Api__BaseUrl | Should -Be 'http://api/'
    }
}

Describe 'the image-only deploy compose (<envName>)' -Skip:(-not $script:DockerAvailable) -ForEach @(
    @{ envName = 'uat' }
    @{ envName = 'production' }
) {
    BeforeAll {
        $script:Run = New-DeployInputs -Directory $TestDrive -Environment $envName
        $script:Result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $script:Run.Inputs
        $script:Config = $script:Result.Config
    }

    It 'resolves with the committed input template and dummy env files' {
        $script:Result.ExitCode | Should -Be 0 -Because $script:Result.Output
    }

    It 'has exactly the four app services, builds nothing and runs no database or mail catcher' {
        ($script:Config.services.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin', 'api', 'portal', 'worker')
        foreach ($service in $script:Config.services.PSObject.Properties.Value) {
            $service.PSObject.Properties.Name | Should -Not -Contain 'build'
        }
        (Get-Content -LiteralPath (Join-Path $script:RepoRoot $script:DeployCompose) -Raw) | Should -Not -Match '(?im)^\s*(build:|image:\s*(postgres|axllent))'
    }

    It 'runs the images the inputs name and pulls them on every deploy' {
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            $script:Config.services.$app.image | Should -Be (Get-InputValue -Environment $envName -Name "TECHSTRAP_$($app.ToUpperInvariant())_IMAGE")
            $script:Config.services.$app.image | Should -Not -Match ':latest$'
            $script:Config.services.$app.pull_policy | Should -Be 'always'
        }
    }

    It 'loads every service from its own scoped env file under TECHSTRAP_ENV_DIR, raw and required' {
        $unresolved = (Get-ComposeConfig -File $script:DeployCompose -EnvFile $script:Run.Inputs -NoEnvResolution).Config
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            $files = @($unresolved.services.$app.env_file)
            $files.Count | Should -Be 1
            $files[0].path | Should -Be "$($script:Run.PortableDirectory)/.env.$app"
            $files[0].format | Should -Be 'raw'
        }
        ([regex]::Matches((Get-Content -LiteralPath (Join-Path $script:RepoRoot $script:DeployCompose) -Raw), '(?m)^        required: true\r?$')).Count | Should -Be 4
    }

    It 'passes the template values into each container, and the values compose owns override the env file' {
        $api = $script:Config.services.api.environment
        $api.TECHSTRAP_AUTOCLOSE_DAYS | Should -Be '7'
        $api.ASPNETCORE_ENVIRONMENT | Should -Be 'Production'
        $api.DOTENV__ENABLED | Should -Be 'false'
        $script:Config.services.worker.environment.EMAIL__SMTP__RETRYMODE | Should -Be 'TransientOnly'
        $script:Config.services.worker.environment.EMAIL__SMTP__TLSMODE | Should -Be 'StartTls'
        $script:Config.services.admin.environment.API__BASEURL | Should -Be 'http://api/'

        # An operator who lists a compose-owned key in the env file does not win: environment: is applied last.
        # Written to a copy of the env directory, so the shared fixture stays as the templates left it.
        $copy = New-DeployInputs -Directory (Join-Path $TestDrive 'precedence') -Environment $envName
        Add-Content -LiteralPath (Join-Path $copy.EnvDirectory '.env.api') -Value 'STORAGE__LOCAL__ROOTPATH=/somewhere/else'
        $overridden = (Get-ComposeConfig -File $script:DeployCompose -EnvFile $copy.Inputs).Config
        $overridden.services.api.environment.STORAGE__LOCAL__ROOTPATH | Should -Be '/app/storage'
    }

    It 'trusts the subnet and the proxy in the Api, and only the proxy in the Admin and the Portal, never a wide range' {
        $services = $script:Config.services
        $script:Config.networks.default.ipam.config[0].subnet | Should -Be (Get-InputValue -Environment $envName -Name 'TECHSTRAP_SUBNET')
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be (Get-InputValue -Environment $envName -Name 'TECHSTRAP_SUBNET')
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__1 | Should -Be (Get-InputValue -Environment $envName -Name 'REVERSE_PROXY_CIDR')
        foreach ($service in 'admin', 'portal') {
            $services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be (Get-InputValue -Environment $envName -Name 'REVERSE_PROXY_CIDR')
            $services.$service.environment.PSObject.Properties.Name | Should -Not -Contain 'TRUSTEDPROXY__TRUSTEDNETWORKS__1'
        }
        foreach ($service in 'api', 'admin', 'portal') {
            $services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Not -Match '^(172\.16\.0\.0/12|0\.0\.0\.0/0)$'
        }
    }

    It 'joins the external database network from the api and the worker only' {
        $script:Config.networks.db.external | Should -BeTrue
        $script:Config.networks.db.name | Should -Be (Get-InputValue -Environment $envName -Name 'TECHSTRAP_DB_NETWORK')
        foreach ($service in 'api', 'worker') {
            ($script:Config.services.$service.networks.PSObject.Properties.Name | Sort-Object) | Should -Be @('db', 'default')
        }
        foreach ($service in 'admin', 'portal') {
            $script:Config.services.$service.networks.PSObject.Properties.Name | Should -Not -Contain 'db'
        }
    }

    It 'gives the api and the worker the default network as the gateway, so the published port and the peer address stay inside the trusted subnet' {
        foreach ($service in 'api', 'worker') {
            $networks = $script:Config.services.$service.networks
            [int]$networks.default.gw_priority | Should -BeGreaterThan ([int]$networks.db.gw_priority)
            [int]$networks.default.gw_priority | Should -Be 1
        }
        foreach ($service in 'admin', 'portal') {
            $script:Config.services.$service.networks.PSObject.Properties.Name | Should -Not -Contain 'db'
        }
        (Get-Content -LiteralPath (Join-Path $script:RepoRoot $script:DeployCompose) -Raw) | Should -Match '(?s)gw_priority'
    }

    It 'publishes the api, admin and portal on loopback only, on the input ports, and the worker nowhere' {
        foreach ($app in 'api', 'admin', 'portal') {
            $ports = @($script:Config.services.$app.ports)
            $ports.Count | Should -Be 1
            $ports[0].host_ip | Should -Be '127.0.0.1'
            [int]$ports[0].target | Should -Be 80
            [string]$ports[0].published | Should -Be (Get-InputValue -Environment $envName -Name "TECHSTRAP_$($app.ToUpperInvariant())_PORT")
        }
        $script:Config.services.worker.PSObject.Properties.Name | Should -Not -Contain 'ports'
    }

    It 'keeps the storage volume on the api and a key ring on each Blazor host' {
        $services = $script:Config.services
        @($services.api.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        @($services.admin.volumes | Where-Object { $_.target -eq '/app/dataprotection-keys' -and $_.source -eq 'admin-keys' }).Count | Should -Be 1
        @($services.portal.volumes | Where-Object { $_.target -eq '/app/dataprotection-keys' -and $_.source -eq 'portal-keys' }).Count | Should -Be 1
        $services.worker.PSObject.Properties.Name | Should -Not -Contain 'volumes'
        ($script:Config.volumes.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin-keys', 'portal-keys', 'techstrap-storage')
    }

    It 'restarts unless stopped, starts the others only after a healthy api and checks health' {
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            $service = $script:Config.services.$app
            $service.restart | Should -Be 'unless-stopped'
            ($service.healthcheck.test -join ' ') | Should -Match '/health/(ready|live)'
        }
        foreach ($app in 'admin', 'portal', 'worker') {
            $script:Config.services.$app.depends_on.api.condition | Should -Be 'service_healthy'
        }
        ($script:Config.services.api.healthcheck.test -join ' ') | Should -Match '/health/ready'
        ($script:Config.services.admin.healthcheck.test -join ' ') | Should -Match '/health/live'
    }

    It 'refuses to resolve without <variable>, and says which one' -ForEach @(
        @{ variable = 'TECHSTRAP_PROJECT' }
        @{ variable = 'TECHSTRAP_API_IMAGE' }
        @{ variable = 'TECHSTRAP_WORKER_IMAGE' }
        @{ variable = 'TECHSTRAP_ADMIN_IMAGE' }
        @{ variable = 'TECHSTRAP_PORTAL_IMAGE' }
        @{ variable = 'TECHSTRAP_ENV_DIR' }
        @{ variable = 'TECHSTRAP_SUBNET' }
        @{ variable = 'REVERSE_PROXY_CIDR' }
        @{ variable = 'TECHSTRAP_DB_NETWORK' }
        @{ variable = 'TECHSTRAP_API_PORT' }
        @{ variable = 'TECHSTRAP_ADMIN_PORT' }
        @{ variable = 'TECHSTRAP_PORTAL_PORT' }
    ) {
        $run = New-DeployInputs -Directory (Join-Path $TestDrive "without-$variable") -Environment $envName -Without @($variable)
        $result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $run.Inputs
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match $variable
    }

    It 'refuses to resolve while a scoped env file is missing' {
        $run = New-DeployInputs -Directory (Join-Path $TestDrive 'without-worker-file') -Environment $envName -WithoutEnvFile @('worker')
        $result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $run.Inputs
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match '\.env\.worker'
    }
}

Describe 'the deploy compose is one file for both environments' -Skip:(-not $script:DockerAvailable) {
    It 'resolves UAT and production inputs to the same service graph' {
        $script:ProjectNames = @{}
        function ConvertTo-NormalisedModel {
            param([string]$Environment)

            $run = New-DeployInputs -Directory (Join-Path $TestDrive "parity-$Environment") -Environment $Environment
            $result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $run.Inputs
            $result.ExitCode | Should -Be 0 -Because $result.Output
            $model = $result.Config
            $script:ProjectNames[$Environment] = $model.name

            # Legitimate differences, removed before comparing: the project name, the published host ports, the image tags and the project-derived
            # prefix of the network and volume names. Everything else (env, volumes, networks, healthchecks, depends_on) must match.
            $model.PSObject.Properties.Remove('name')
            foreach ($service in $model.services.PSObject.Properties.Value) {
                $service.PSObject.Properties.Remove('ports')
                $service.image = ($service.image -replace ':[^:]+$', ':tag')
            }
            $text = $model | ConvertTo-Json -Depth 30
            return ($text -replace 'techstrap(-uat)?_', 'techstrap_')
        }

        ConvertTo-NormalisedModel -Environment 'uat' | Should -BeExactly (ConvertTo-NormalisedModel -Environment 'production')
        # The project name is stripped above, so pin that it differs: one name for both would let UAT and production share containers and volumes.
        $script:ProjectNames['uat'] | Should -Not -Be $script:ProjectNames['production']
    }

    It 'is the only deploy compose file' {
        Test-Path (Join-Path $script:RepoRoot 'docker-compose.production.yml') | Should -BeFalse
        Test-Path (Join-Path $script:RepoRoot 'docker-compose.uat.yml') | Should -BeFalse
        (Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'deploy') -Filter 'docker-compose*.yml').Name | Should -Be @('docker-compose.yml')
    }
}
