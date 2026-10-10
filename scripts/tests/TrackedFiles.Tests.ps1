BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Tracked = @(& git -C $script:RepoRoot ls-files)
}

Describe 'files that are restored or generated at build are never committed' {
    It 'tracks no Bootstrap vendor files' {
        $script:Tracked | Where-Object { $_ -match '/Styles/Vendor/' } | Should -BeNullOrEmpty
    }

    It 'tracks no compiled CSS' {
        $script:Tracked | Where-Object { $_ -match '/wwwroot/css/' } | Should -BeNullOrEmpty
    }

    It 'tracks no font binaries or restored font licenses' {
        $script:Tracked | Where-Object { $_ -match '/wwwroot/fonts/' -or $_ -match '\.(woff2?|ttf|otf)$' } | Should -BeNullOrEmpty
    }

    It 'loads no font, script or style from a CDN at runtime' {
        $pattern = 'fonts\.googleapis|fonts\.gstatic|cdn\.jsdelivr|unpkg\.com|cdnjs\.cloudflare|code\.jquery'
        $sources = $script:Tracked | Where-Object { $_ -match '^src/.*\.(razor|cshtml|html|scss|css|js|cs)$' }
        foreach ($file in $sources) {
            (Get-Content -LiteralPath (Join-Path $script:RepoRoot $file) -Raw) | Should -Not -Match $pattern -Because "$file must not call a third-party host"
        }
    }
}
