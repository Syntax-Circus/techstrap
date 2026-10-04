BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Node = Get-Command node -ErrorAction SilentlyContinue
}

Describe 'Admin browser scripts' {
    It 'passes the node:test suite for the keyboard layer' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the shortcuts.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }
}
