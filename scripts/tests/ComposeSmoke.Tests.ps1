BeforeDiscovery {
    $script:DockerAvailable = $null -ne (Get-Command docker -ErrorAction SilentlyContinue) -and
        ((& docker compose version 2>&1 | Out-String) -match 'Docker Compose')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:SmokeScript = Join-Path $script:RepoRoot 'scripts' 'Test-ComposeSmoke.ps1'
    $script:SmokeText = Get-Content -LiteralPath $script:SmokeScript -Raw
    $script:DryRun = & $script:SmokeScript -DryRun | Out-String
}

Describe 'Test-ComposeSmoke.ps1' {
    It 'parses without errors' {
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($script:SmokeScript, [ref]$null, [ref]$errors) | Out-Null
        $errors | Should -BeNullOrEmpty
    }

    It 'under -DryRun prints the commands it would run and runs none of them' {
        $script:DryRun | Should -Match 'up -d --wait'
        $script:DryRun | Should -Match 'port api 80'
        $script:DryRun | Should -Match 'port admin 80'
        $script:DryRun | Should -Match 'health/ready'
        $script:DryRun | Should -Match ' down\r?\n'
    }

    It 'uses its own project name, so it can never stop the stack run by hand' {
        $script:DryRun | Should -Match 'compose -p techstrap-smoke '
        $script:DryRun | Should -Not -Match 'compose -p techstrap '
        { & $script:SmokeScript -ProjectName 'techstrap' -DryRun } | Should -Throw '*Refusing*'
    }

    It 'never removes volumes' {
        # The one place it stops the stack passes the single argument 'down': no -v and no --volumes after it.
        $script:SmokeText | Should -Match "Invoke-Compose -Arguments @\('down'\)"
        $script:SmokeText | Should -Not -Match "'down'\s*,"
        $script:SmokeText | Should -Not -Match "'(-v|--volumes)'"
        $script:SmokeText | Should -Not -Match '(?i)docker volume (rm|prune)'
        $script:DryRun | Should -Not -Match '(?i)\sdown\s+(-v|--volumes)'
    }

    It 'publishes every port on a free loopback port and names its own images' {
        $script:DryRun | Should -Match '(?s)api:\s+image: techstrap-smoke-api:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)admin:\s+image: techstrap-smoke-admin:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)portal:\s+image: techstrap-smoke-portal:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)mailpit:\s+ports: !override\s+- "127\.0\.0\.1::8025"'
        $ports = [regex]::Matches($script:DryRun, '(?m)^\s+- "([^"]+)"\s*$') | ForEach-Object { $_.Groups[1].Value }
        @($ports).Count | Should -Be 4
        foreach ($port in $ports) { $port | Should -Match '^127\.0\.0\.1::\d+$' }
    }

    It 'checks the Portal too: it must answer /health/ready, and its published port is read like the others' {
        $script:DryRun | Should -Match 'port portal 80'
        $script:DryRun | Should -Match 'GET /health/ready on the Api, the Admin and the Portal, expecting 200'
        $script:SmokeText | Should -Match "Get-PublishedPort -Service 'portal' -ContainerPort 80"
        $script:SmokeText | Should -Match 'Assert-Ready -Name ''Portal'' -Port \$portalPort'
    }

    It 'lowers the Api public limit to 3 and makes the Portal trust the compose subnet, in the override only' {
        $script:DryRun | Should -Match '(?s)api:\s+image: techstrap-smoke-api:local\s+ports: !override\s+- "127\.0\.0\.1::80"\s+environment:\s+RateLimiting__Public__PermitLimit: "3"\s+RateLimiting__Public__WindowSeconds: "600"'
        $script:DryRun | Should -Match '(?s)portal:\s+image: techstrap-smoke-portal:local\s+ports: !override\s+- "127\.0\.0\.1::80"\s+environment:\s+TRUSTEDPROXY__TRUSTEDNETWORKS__0: \$\{TECHSTRAP_SUBNET:-172\.16\.31\.0/24\}'
        # The lowered limit lives in the smoke override only: neither compose file may carry it.
        (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'docker-compose.yml') -Raw) | Should -Not -Match 'RateLimiting__Public'
        (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' 'docker-compose.yml') -Raw) | Should -Not -Match 'RateLimiting__Public'
    }

    It 'proves the rate limit sees the real client address through the Portal suggest adapter, from inside the compose network' {
        $script:DryRun | Should -Match 'exec -T api curl .*http://portal/p/smoke/suggest\?q=printer'
        $script:DryRun | Should -Match 'X-Forwarded-For: 203\.0\.113\.10 .* 200 three times, then 429'
        $script:DryRun | Should -Match 'X-Forwarded-For: 203\.0\.113\.11 .* 200'
        $script:SmokeText | Should -Match "'exec', '-T', 'api', 'curl'"
        $script:SmokeText | Should -Match 'Assert-StatusCode'
        $script:SmokeText | Should -Match '\$smokePermitLimit = 3'
    }

    It 'builds the four images one after another, never with up --build (parallel restores corrupt the shared NuGet cache mount)' {
        foreach ($service in 'api', 'worker', 'admin', 'portal') {
            $script:DryRun | Should -Match "build $service"
        }
        $script:DryRun | Should -Not -Match 'up [^\r\n]*--build'
        $quiet = & $script:SmokeScript -DryRun -NoBuild | Out-String
        $quiet | Should -Not -Match ' build '
    }

    It 'checks the deploy compose only on request, with config alone: no pull and no up' {
        $script:DryRun | Should -Not -Match 'deploy/docker-compose.yml'
        $checked = & $script:SmokeScript -DryRun -CheckDeployCompose | Out-String
        $checked | Should -Match 'deploy/docker-compose\.yml config --quiet'
        $checked | Should -Match 'no pull, no up'
        # The script's only docker call that names the deploy file is the config check.
        $script:SmokeText | Should -Match '& docker compose --env-file \$inputs -f \$deployFile config --quiet'
        $script:SmokeText | Should -Not -Match '\$deployFile[^\r\n]*\b(pull|up)\b'
    }

    It 'resolves the deploy compose with the UAT template and dummy env files (-DeployComposeOnly, needs Docker)' -Skip:(-not $script:DockerAvailable) {
        $output = & $script:SmokeScript -DeployComposeOnly | Out-String
        $output | Should -Match 'deploy/docker-compose\.yml resolves'
    }

    It 'honors -DryRun with -DeployComposeOnly: prints the config check and runs no Docker' {
        $output = & $script:SmokeScript -DeployComposeOnly -DryRun | Out-String
        $output | Should -Match 'deploy/docker-compose\.yml config --quiet'
        $output | Should -Match 'no pull, no up'
        $output | Should -Not -Match 'resolves'
    }

    It 'leaves the stack running only when asked to' {
        (& $script:SmokeScript -DryRun -KeepRunning | Out-String) | Should -Not -Match ' down'
    }
}

Describe 'the compose smoke in CI' {
    BeforeAll { $script:Workflow = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.github' 'workflows' 'ci.yml') -Raw }

    It 'is a manually started job, so a pull request does not build four images twice' {
        $script:Workflow | Should -Match '(?m)^  workflow_dispatch:'
        $script:Workflow | Should -Match '(?s)compose-smoke:\s+name: Compose smoke \(manual\)\s+if: github\.event_name == ''workflow_dispatch'''
        $script:Workflow | Should -Match 'scripts/Test-ComposeSmoke\.ps1'
    }
}
