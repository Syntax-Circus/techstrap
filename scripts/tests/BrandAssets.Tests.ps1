BeforeDiscovery {
    # File, size budget in bytes. Budgets are about 1.5x the traced size so a re-trace that explodes the file fails.
    $script:Svgs = @(
        @{ Name = 'mark.svg'; Budget = 50KB }
        @{ Name = 'logo.svg'; Budget = 120KB }
        @{ Name = 'wordmark.svg'; Budget = 12KB }
    )
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:BrandDir = Join-Path $script:RepoRoot 'assets/brand'
}

Describe 'brand SVG <Name>' -ForEach $script:Svgs {
    BeforeAll {
        $script:Path = Join-Path $script:BrandDir $Name
        $script:Budget = $Budget
    }

    It 'exists and is under its size budget' {
        $script:Path | Should -Exist
        (Get-Item -LiteralPath $script:Path).Length | Should -BeLessThan $script:Budget
    }

    It 'is well-formed XML with an svg root and a four-number viewBox' {
        $xml = [xml](Get-Content -LiteralPath $script:Path -Raw)
        $xml.DocumentElement.LocalName | Should -Be 'svg'
        $parts = @($xml.DocumentElement.GetAttribute('viewBox') -split '[ ,]+' | Where-Object { $_ })
        $parts.Count | Should -Be 4
        foreach ($part in $parts) { { [double]$part } | Should -Not -Throw }
        [double]$parts[2] | Should -BeGreaterThan 0
        [double]$parts[3] | Should -BeGreaterThan 0
    }

    It 'carries no script, external reference or embedded raster' {
        $text = Get-Content -LiteralPath $script:Path -Raw
        $text | Should -Not -Match '<script'
        $text | Should -Not -Match 'href\s*='
        $text | Should -Not -Match '<image'
        $text | Should -Not -Match 'data:image'
        $text | Should -Not -Match 'onload|onclick'
    }

    It 'is marked provisional in assets/brand/README.md' {
        (Get-Content -LiteralPath (Join-Path $script:BrandDir 'README.md') -Raw) | Should -Match ('(?s)' + [regex]::Escape($Name) + '.*provisional')
    }
}

Describe 'brand SVG copies in the apps' {
    It '<App> wwwroot/brand/<File> is byte-identical to assets/brand/<File>' -ForEach @(
        @{ App = 'TechStrap.Admin'; File = 'mark.svg' }
        @{ App = 'TechStrap.Admin'; File = 'logo.svg' }
        @{ App = 'TechStrap.Admin'; File = 'wordmark.svg' }
        @{ App = 'TechStrap.Portal'; File = 'mark.svg' }
    ) {
        $copy = Join-Path $script:RepoRoot "src/$App/wwwroot/brand/$File"
        $copy | Should -Exist
        (Get-FileHash -LiteralPath $copy).Hash | Should -Be (Get-FileHash -LiteralPath (Join-Path $script:BrandDir $File)).Hash
    }

    It 'the Portal carries only the head mark (the 16px Powered-by mark), never the logo or wordmark' {
        Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'src/TechStrap.Portal/wwwroot/brand') -File | ForEach-Object Name | Should -Be @('mark.svg')
    }
}

Describe 'wordmark' {
    It 'is outlined text filled with currentColor, so it needs no font at runtime and follows the theme' {
        $text = Get-Content -LiteralPath (Join-Path $script:BrandDir 'wordmark.svg') -Raw
        $text | Should -Not -Match '<text'
        $text | Should -Not -Match 'font-family'
        $text | Should -Match 'fill="currentColor"'
    }
}
