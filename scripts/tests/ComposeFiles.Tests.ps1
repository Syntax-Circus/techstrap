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
        param([string]$Path, [switch]$WithoutPostgresPassword)

        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.env.production.example')
        if ($WithoutPostgresPassword) {
            $lines = $lines | ForEach-Object { if ($_ -like 'POSTGRES_PASSWORD=*') { 'POSTGRES_PASSWORD=' } else { $_ } }
        }
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
        ($config.services.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin', 'api', 'portal', 'postgres', 'worker')
        $config.services.postgres.image | Should -Be 'postgres:17'
        foreach ($service in 'api', 'admin', 'portal') {
            $config.services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Not -Match '^(172\.16\.0\.0/12|0\.0\.0\.0/0)$'
        }
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
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__1 | Should -Be '192.168.1.55/32'
        foreach ($service in 'admin', 'portal') {
            $services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be '192.168.1.55/32'
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
}
