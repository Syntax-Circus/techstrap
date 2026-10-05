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

    It 'passes the node:test suite for the browser preferences (storage never throws, theme values)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the preferences.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/preferences.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the early theme script (never throws, same key and meaning as preferences.js)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the theme-init.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/theme-init.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the clipboard helpers' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the clipboard.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/clipboard.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the time zone module (the zone is read without ever throwing)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the tz.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/tz.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the dialog module (Esc and a stray native close can never dismiss a locked dialog)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the dialog.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/dialog.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }
}
