BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Node = Get-Command node -ErrorAction SilentlyContinue
}

Describe 'Portal browser scripts' {
    It 'passes the node:test suite for the KB suggestions element (debounce, stale requests, text only, clean-up)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the kb-suggestions.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'has the test file and the module it tests' {
        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'src/TechStrap.Portal/wwwroot/js/kb-suggestions.js') | Should -BeTrue
    }
}
