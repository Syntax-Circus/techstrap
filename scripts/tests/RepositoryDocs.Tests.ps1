BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    function Get-RepoText {
        param([string]$RelativePath)
        return Get-Content -LiteralPath (Join-Path $script:RepoRoot $RelativePath) -Raw
    }
}

Describe 'open source files' {
    It 'LICENSE is the MIT license for Syntax Circus' {
        $text = Get-RepoText 'LICENSE'
        $text | Should -Match '^MIT License'
        $text | Should -Match 'Copyright \(c\) 2026 Syntax Circus'
    }

    It 'SECURITY.md uses GitHub private vulnerability reporting, with supported versions and scope' {
        $text = Get-RepoText 'SECURITY.md'
        $text | Should -Match 'Report a vulnerability'
        $text | Should -Match '## Supported versions'
        $text | Should -Match '## Scope'
        $text | Should -Not -Match '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}'
    }

    It 'CONTRIBUTING.md covers Conventional Commits, test-first and EF-tool-only migrations' {
        $text = Get-RepoText 'CONTRIBUTING.md'
        $text | Should -Match 'Conventional Commits'
        $text | Should -Match 'Test first'
        $text | Should -Match 'dotnet ef migrations add'
        $text | Should -Match 'Never write or edit a migration by hand'
    }
}

Describe 'README.md' {
    It 'shows the logo at the top, centered, 200 pixels wide' {
        $text = Get-RepoText 'README.md'
        $text | Should -Match '(?s)^<p align="center">\s*<img src="assets/brand/logo-512\.png"[^>]*width="200"'
    }

    It 'keeps the original sections and adds the compose quick start' {
        $text = Get-RepoText 'README.md'
        foreach ($heading in '## What it is', '## Tech stack', '## Documentation', '## License', '## Quick start') {
            $text | Should -Match ([regex]::Escape($heading))
        }
        $text | Should -Match 'docker compose up -d --build'
        $text | Should -Match 'docs/architecture/00-DISCOVERY-INDEX\.md'
    }

    It 'links only to files that exist' {
        $text = Get-RepoText 'README.md'
        $links = [regex]::Matches($text, '\]\((?<path>(?!https?:|#)[^)\s]+)\)') | ForEach-Object { $_.Groups['path'].Value }
        foreach ($link in $links) {
            Test-Path -LiteralPath (Join-Path $script:RepoRoot $link) | Should -BeTrue -Because "README links to $link"
        }
    }
}
