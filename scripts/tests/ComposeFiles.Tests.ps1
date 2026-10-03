BeforeDiscovery {
    $script:DockerAvailable = $null -ne (Get-Command docker -ErrorAction SilentlyContinue) -and
        ((& docker compose version 2>&1 | Out-String) -match 'Docker Compose')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:PinnedSubnet = '172.16.31.0/24'

    function Get-ComposeConfig {
        param([string]$File, [string]$EnvFile = '')

        $arguments = @('compose')
        if ($EnvFile) { $arguments += @('--env-file', $EnvFile) }
        $arguments += @('-f', (Join-Path $script:RepoRoot $File), 'config', '--format', 'json')

        $output = & docker @arguments 2>&1 | Out-String
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output   = $output
            Config   = if ($LASTEXITCODE -eq 0) { $output | ConvertFrom-Json } else { $null }
        }
    }

    function New-ProductionEnvFile {
        param([string]$Path, [switch]$WithoutPostgresPassword, [switch]$WithoutSmtpHost)

        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.env.production.example')
        if ($WithoutPostgresPassword) {
            $lines = $lines | ForEach-Object { if ($_ -like 'POSTGRES_PASSWORD=*') { 'POSTGRES_PASSWORD=' } else { $_ } }
        }
        $smtpHost = if ($WithoutSmtpHost) { '' } else { 'smtp.test' }
        $lines = $lines | ForEach-Object { if ($_ -like 'SMTP_HOST=*') { "SMTP_HOST=$smtpHost" } else { $_ } }
        Set-Content -LiteralPath $Path -Value $lines
    }

    Remove-Item Env:TECHSTRAP_SUBNET -ErrorAction SilentlyContinue
}

Describe 'docker-compose files' -Skip:(-not $script:DockerAvailable) {
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

    It 'production and uat workers use outbox-safe SMTP settings and pass the Powered-by setting (<file>)' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env-smtp'
        New-ProductionEnvFile -Path $envFile
        $worker = (Get-ComposeConfig -File $file -EnvFile $envFile).Config.services.worker
        $worker.environment.Email__Smtp__MaxRetryAttempts | Should -Be '1'
        $worker.environment.Email__Smtp__RetryMode | Should -Be 'TransientOnly'
        $worker.environment.Email__Smtp__TlsMode | Should -Be 'StartTls'
        $worker.environment.PSObject.Properties.Name | Should -Contain 'TECHSTRAP_PORTAL_SHOW_POWERED_BY'
    }

    It 'production and uat workers pass the EmailOutbox settings through with defaults (<file>)' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env-outbox'
        New-ProductionEnvFile -Path $envFile
        $worker = (Get-ComposeConfig -File $file -EnvFile $envFile).Config.services.worker
        $worker.environment.EmailOutbox__Enabled | Should -Be 'true'
        $worker.environment.EmailOutbox__BatchSize | Should -Be '20'
        $worker.environment.EmailOutbox__LeaseSeconds | Should -Be '900'
        $worker.environment.EmailOutbox__PollIntervalSeconds | Should -Be '5'
    }

    It '<file> refuses to resolve without SMTP_HOST and says how to fix it' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env-nosmtphost'
        New-ProductionEnvFile -Path $envFile -WithoutSmtpHost
        $result = Get-ComposeConfig -File $file -EnvFile $envFile
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'set SMTP_HOST in the env file'
    }

    It 'local compose mounts the shared storage volume on api and worker only' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        foreach ($service in 'api', 'worker') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        }
        foreach ($service in 'admin', 'portal') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' }).Count | Should -Be 0
        }
    }

    It '<file> resolves with the example env file, trusts the proxy in Admin and Portal only, and the API also trusts the subnet' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env'
        New-ProductionEnvFile -Path $envFile
        $result = Get-ComposeConfig -File $file -EnvFile $envFile
        $result.ExitCode | Should -Be 0
        $services = $result.Config.services

        $result.Config.networks.default.ipam.config[0].subnet | Should -Be $script:PinnedSubnet
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be $script:PinnedSubnet
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__1 | Should -Be '172.16.31.1/32'
        foreach ($service in 'admin', 'portal') {
            $services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be '172.16.31.1/32'
            $services.$service.environment.PSObject.Properties.Name | Should -Not -Contain 'TRUSTEDPROXY__TRUSTEDNETWORKS__1'
        }
        $services.api.image | Should -Be 'ghcr.io/syntax-circus/techstrap-api:latest'
    }

    It '<file> refuses to resolve without POSTGRES_PASSWORD' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env-nopassword'
        New-ProductionEnvFile -Path $envFile -WithoutPostgresPassword
        $result = Get-ComposeConfig -File $file -EnvFile $envFile
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'POSTGRES_PASSWORD'
    }

    It 'UAT and production compose files stay identical apart from the project name and host ports' {
        $envFile = Join-Path $TestDrive 'env-drift'
        New-ProductionEnvFile -Path $envFile

        # Legitimate differences, removed before comparing:
        #   - top-level name (techstrap vs techstrap-uat)
        #   - every service's ports (UAT defaults to 18080-18082)
        #   - the project-derived prefix of network and volume names (techstrap_ vs techstrap-uat_)
        # Everything else (trust rules, healthchecks, env keys, volumes, depends_on) must match.
        function ConvertTo-NormalisedModel {
            param([string]$File)

            $result = Get-ComposeConfig -File $File -EnvFile $envFile
            $result.ExitCode | Should -Be 0
            $model = $result.Config
            $model.PSObject.Properties.Remove('name')
            foreach ($service in $model.services.PSObject.Properties.Value) {
                $service.PSObject.Properties.Remove('ports')
            }
            return (($model | ConvertTo-Json -Depth 30) -replace "techstrap(-uat)?_", "techstrap_")
        }

        ConvertTo-NormalisedModel -File 'docker-compose.uat.yml' | Should -BeExactly (ConvertTo-NormalisedModel -File 'docker-compose.production.yml')
    }
}
