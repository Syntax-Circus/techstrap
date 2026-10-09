BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Backup = Join-Path $script:RepoRoot 'deploy/backup.sh'
    $script:Restore = Join-Path $script:RepoRoot 'deploy/restore.sh'
    $script:Volumes = 'techstrap-storage', 'admin-keys', 'portal-keys'
}

Describe 'deploy/backup.sh and deploy/restore.sh (static pins, PHASE-12b)' {
    It 'both scripts exist, are LF and ASCII and start with the bash shebang' {
        foreach ($path in $script:Backup, $script:Restore) {
            Test-Path -LiteralPath $path | Should -BeTrue -Because $path
            $bytes = [System.IO.File]::ReadAllBytes($path)
            ($bytes -contains 13) | Should -BeFalse -Because "$path must be LF"
            $text = [System.IO.File]::ReadAllText($path)
            $text.StartsWith("#!/usr/bin/env bash`n") | Should -BeTrue
            ([regex]::IsMatch($text, '[^\x00-\x7F]')) | Should -BeFalse -Because "$path must be ASCII"
        }
    }

    It 'both scripts fail fast with set -euo pipefail' {
        foreach ($path in $script:Backup, $script:Restore) {
            ([System.IO.File]::ReadAllText($path)) | Should -Match '(?m)^set -euo pipefail\s*$'
        }
    }

    It 'backup.sh dumps with pg_dump -Fc inside a postgres:17 container' {
        $text = [System.IO.File]::ReadAllText($script:Backup)
        $text | Should -Match 'pg_dump -Fc'
        $text | Should -Match 'postgres:17'
        $text | Should -Match 'docker run --rm'
    }

    It 'backup.sh encrypts with openssl aes-256-cbc pbkdf2 from a passphrase file' {
        $text = [System.IO.File]::ReadAllText($script:Backup)
        $text | Should -Match 'openssl enc -aes-256-cbc -pbkdf2 -salt'
        $text | Should -Match '-pass "?file:'
        $text | Should -Match '--passphrase-file'
        $text | Should -Match '--no-encrypt'
    }

    It 'backup.sh has retention and dry-run flags' {
        $text = [System.IO.File]::ReadAllText($script:Backup)
        $text | Should -Match '--keep-days'
        $text | Should -Match '--dry-run'
    }

    It 'backup.sh dumps the database before it archives the volumes' {
        $text = [System.IO.File]::ReadAllText($script:Backup)
        $text.IndexOf('pg_dump -Fc') | Should -BeGreaterThan -1
        $text.IndexOf('pg_dump -Fc') | Should -BeLessThan $text.IndexOf('tar czf -') -Because 'dump first, then volumes (runbook consistency caveat)'
    }

    It 'both scripts name all three volumes' {
        foreach ($path in $script:Backup, $script:Restore) {
            $text = [System.IO.File]::ReadAllText($path)
            foreach ($volume in $script:Volumes) { $text | Should -Match ([regex]::Escape($volume)) -Because "$path must handle $volume" }
        }
    }

    It 'backup.sh archives volumes read-only through alpine tar' {
        $text = [System.IO.File]::ReadAllText($script:Backup)
        $text | Should -Match ':/v:ro'
        $text | Should -Match 'alpine'
        $text | Should -Match 'tar czf -'
    }

    It 'restore.sh restores all three volumes and refuses a manifest that lacks one' {
        $text = [System.IO.File]::ReadAllText($script:Restore)
        $text | Should -Match 'manifest is missing volume'
        $text | Should -Match '--skip-volume'
        $text | Should -Match 'WARNING: skipping volume'
        $text | Should -Match 'pg_restore'
        $text | Should -Match 'postgres:17'
    }

    It 'restore.sh verifies the migration history count against the manifest and prints the ticket and attachment counts' {
        $text = [System.IO.File]::ReadAllText($script:Restore)
        $text | Should -Match '__EFMigrationsHistory'
        $text | Should -Match 'migrations_count'
        $text | Should -Match 'ticket_count'
        $text | Should -Match 'attachment_count'
    }

    It 'restore.sh refuses to overwrite an existing stack and has a scoped teardown' {
        $text = [System.IO.File]::ReadAllText($script:Restore)
        $text | Should -Match '--overwrite'
        $text | Should -Match '--teardown'
        $text | Should -Match 'refusing'
    }

    It 'neither script removes volumes wholesale' {
        foreach ($path in $script:Backup, $script:Restore) {
            $text = [System.IO.File]::ReadAllText($path)
            $text | Should -Not -Match 'down\s+-v'
            $text | Should -Not -Match '--volumes'
            $text | Should -Not -Match 'volume prune'
        }
    }

    It 'neither script puts a passphrase or password value on a command line' {
        foreach ($path in $script:Backup, $script:Restore) {
            $text = [System.IO.File]::ReadAllText($path)
            $text | Should -Not -Match '-pass pass:'
            $text | Should -Not -Match '--passphrase\s+\S'
        }
    }
}
