BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Load = Join-Path $script:RepoRoot 'tests/load'
    $script:Runner = Join-Path $script:RepoRoot 'scripts/Invoke-LoadTest.ps1'
    $script:Modules = 'intake-trusted', 'intake-public', 'portal-form', 'customer-view', 'kb-search'
    $script:Composers = 'sustained', 'spike'
    $script:Libs = 'auth', 'payloads', 'clients', 'checks', 'profile'
    function Read-LoadFile { param([string]$Relative) [System.IO.File]::ReadAllText((Join-Path $script:Load $Relative)) }
    function Get-AllLoadJs { Get-ChildItem -LiteralPath $script:Load -Recurse -Filter *.js | Where-Object { $_.FullName -notmatch '[\\/]results[\\/]' } }
}

Describe 'k6 load suite (static pins, PHASE-12b)' {
    It 'has every library, scenario module, composer, README and runner' {
        foreach ($name in $script:Libs) { Test-Path (Join-Path $script:Load "lib/$name.js") | Should -BeTrue -Because "lib/$name.js" }
        foreach ($name in $script:Modules) { Test-Path (Join-Path $script:Load "$name.js") | Should -BeTrue -Because "$name.js" }
        foreach ($name in $script:Composers) { Test-Path (Join-Path $script:Load "scenarios/$name.js") | Should -BeTrue -Because "scenarios/$name.js" }
        Test-Path (Join-Path $script:Load 'README.md') | Should -BeTrue
        Test-Path $script:Runner | Should -BeTrue
        Test-Path (Join-Path $script:RepoRoot 'docs/load-test-results.md') | Should -BeTrue
    }

    It 'every scenario module and composer declares thresholds' {
        foreach ($name in $script:Modules) { (Read-LoadFile "$name.js") | Should -Match 'thresholds\s*:' -Because "$name.js must declare thresholds" }
        foreach ($name in $script:Composers) { (Read-LoadFile "scenarios/$name.js") | Should -Match 'thresholds\s*:' -Because "scenarios/$name.js must declare thresholds" }
    }

    It 'encodes the 500 ms p95 budget and a zero server-error rate in code' {
        foreach ($name in $script:Modules) { (Read-LoadFile "$name.js") | Should -Match ([regex]::Escape('p(95)<500')) -Because "$name.js" }
        (Read-LoadFile 'scenarios/sustained.js') | Should -Match ([regex]::Escape('p(95)<500'))
        (Read-LoadFile 'scenarios/sustained.js') | Should -Match "server_errors'?\s*:\s*\[\s*'rate==0'"
        (Read-LoadFile 'scenarios/spike.js') | Should -Match "server_errors'?\s*:\s*\[\s*'rate==0'"
    }

    It 'the spike asserts 429s during the spike and a 500 ms p95 after it' {
        $spike = Read-LoadFile 'scenarios/spike.js'
        $spike | Should -Match ([regex]::Escape('throttled_429{phase:spike}'))
        $spike | Should -Match ([regex]::Escape('http_req_duration{phase:recovery}'))
        $spike | Should -Match 'count>0'
    }

    It 'the sustained mix is 50/10/30/10 plus the portal form at one request a minute' {
        $sustained = Read-LoadFile 'scenarios/sustained.js'
        foreach ($pattern in 'intakeTrusted.*0\.5', 'intakePublic.*0\.1', 'kbSearch.*0\.3', 'customerView.*0\.1', 'portalForm') { $sustained | Should -Match $pattern }
        $sustained | Should -Match "timeUnit:\s*'1m'"
    }

    It 'sends the Portal form with the antiforgery token, the handler, the submit id and a blank honeypot' {
        $form = Read-LoadFile 'portal-form.js'
        foreach ($needle in '__RequestVerificationToken', '_handler', 'contact', 'Form.SubmitId', 'Form.Website') { $form | Should -Match ([regex]::Escape($needle)) }
    }

    It 'rotates X-Forwarded-For and reads the viewUrl token from the intake response' {
        (Read-LoadFile 'lib/clients.js') | Should -Match 'X-Forwarded-For'
        (Read-LoadFile 'customer-view.js') | Should -Match 'viewUrl'
        (Read-LoadFile 'customer-view.js') | Should -Match 'X-Ticket-Token'
    }

    It 'the README documents every environment variable the scripts read' {
        $readme = Read-LoadFile 'README.md'
        $names = foreach ($file in (Get-AllLoadJs)) {
            [regex]::Matches([System.IO.File]::ReadAllText($file.FullName), '__ENV(?:\.|\[\x27)(?<n>[A-Z][A-Z0-9_]*)') | ForEach-Object { $_.Groups['n'].Value }
        }
        $names = @($names | Sort-Object -Unique)
        $names.Count | Should -BeGreaterOrEqual 9
        foreach ($name in $names) { $readme | Should -Match ([regex]::Escape($name)) -Because "README.md must document $name" }
    }

    It 'the README covers proxy trust, outbox and dead-letter counts, results and the Verified note' {
        $readme = Read-LoadFile 'README.md'
        foreach ($needle in 'REVERSE_PROXY_CIDR', 'X-Forwarded-For', 'email_outbox', 'dead', 'psql', 'tests/load/results', 'Verified', 'Invoke-LoadTest.ps1') { $readme | Should -Match ([regex]::Escape($needle)) -Because $needle }
    }

    It 'contains no key value except the NotASecret dev keys' {
        $files = @(Get-AllLoadJs) + (Get-Item (Join-Path $script:Load 'README.md')) + (Get-Item $script:Runner) + (Get-Item (Join-Path $script:RepoRoot 'docs/load-test-results.md'))
        foreach ($file in $files) {
            foreach ($match in [regex]::Matches([System.IO.File]::ReadAllText($file.FullName), '\b(?:tsk|tsp)_[A-Za-z0-9]{16,}')) {
                $match.Value | Should -Match 'NotASecret' -Because "$($file.Name) holds a key-like value"
            }
        }
    }

    It 'all load files are LF and ASCII' {
        $files = @(Get-AllLoadJs) + (Get-Item (Join-Path $script:Load 'README.md')) + (Get-Item $script:Runner)
        foreach ($file in $files) {
            $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
            ($bytes -contains 13) | Should -BeFalse -Because "$($file.Name) must be LF"
            @($bytes | Where-Object { $_ -gt 127 }) | Should -BeNullOrEmpty -Because "$($file.Name) must be ASCII"
        }
    }

    It 'keeps the results folder out of git' {
        [System.IO.File]::ReadAllText((Join-Path $script:RepoRoot '.gitignore')) | Should -Match '(?m)^tests/load/results/\s*$'
    }
}

Describe 'scripts/Invoke-LoadTest.ps1 (static and dry-run)' {
    It 'never writes a key variable to any output stream' {
        foreach ($line in [System.IO.File]::ReadAllLines($script:Runner)) {
            if ($line -match 'TS_TRUSTED_KEY|TS_PUBLIC_KEY') { $line | Should -Not -Match 'Write-(Host|Output|Verbose|Warning|Information)|echo|\|\s*Out-' -Because $line }
        }
    }

    It 'passes keys to docker by name, never by value' {
        $text = [System.IO.File]::ReadAllText($script:Runner)
        $text | Should -Match "'-e', 'TS_TRUSTED_KEY'"
        $text | Should -Not -Match "TS_TRUSTED_KEY='"
    }

    It 'under -DryRun prints the plan, calls nothing and never prints a key' {
        $env:TS_TRUSTED_KEY = 'tsk_canaryLoadKeyValue00000000'
        $env:TS_PUBLIC_KEY = 'tsp_canaryLoadKeyValue00000000'
        try {
            $output = & $script:Runner -Target local -Scenario sustained -Rate 2 -Duration 30s -DryRun | Out-String
            $output | Should -Match 'sustained'
            $output | Should -Match 'DRY-RUN'
            $output | Should -Match ([regex]::Escape('tests/load/results/'))
            $output | Should -Not -Match 'canaryLoadKeyValue'
        } finally {
            Remove-Item Env:TS_TRUSTED_KEY, Env:TS_PUBLIC_KEY -ErrorAction SilentlyContinue
        }
    }

    It 'requires explicit keys for the uat target' {
        Remove-Item Env:TS_TRUSTED_KEY, Env:TS_PUBLIC_KEY -ErrorAction SilentlyContinue
        { & $script:Runner -Target uat -Scenario spike -BaseUrl 'https://api.uat.example' -PortalUrl 'https://uat.example' -DryRun } | Should -Throw '*TS_TRUSTED_KEY*'
    }
}
