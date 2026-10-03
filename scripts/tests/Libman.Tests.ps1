BeforeDiscovery {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Manifests = @(
        Get-ChildItem -Path (Join-Path $root 'src') -Filter libman.json -Recurse -Depth 2 |
            ForEach-Object { @{ App = $_.Directory.Name; Path = $_.FullName } }
    )
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:MapText = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'docs/architecture/03-PACKAGE-MAP.md') -Raw

    function Get-Libraries {
        param([string]$Path)
        $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            Provider  = $manifest.defaultProvider
            Libraries = @($manifest.libraries | ForEach-Object {
                $at = $_.library.LastIndexOf('@')
                [pscustomobject]@{ Name = $_.library.Substring(0, $at); Version = $_.library.Substring($at + 1); Destination = $_.destination }
            })
        }
    }
}

Describe 'libman manifest <App>' -ForEach $script:Manifests {
    BeforeAll {
        $script:Manifest = Get-Libraries -Path $Path
        $script:AppName = $App
    }

    It 'restores from jsdelivr and pins exact versions' {
        $script:Manifest.Provider | Should -Be 'jsdelivr'
        $script:Manifest.Libraries.Count | Should -BeGreaterThan 0
        foreach ($library in $script:Manifest.Libraries) {
            $library.Version | Should -Match '^\d+\.\d+\.\d+$' -Because "$($library.Name) must not float"
        }
    }

    It 'lists every library with the same version in the 4b front-end table of 03-PACKAGE-MAP.md' {
        $section = ($script:MapText -split '(?m)^## 4b\. ')[1]
        $section | Should -Not -BeNullOrEmpty -Because 'section 4b of the package map lists libman libraries'
        $section = ($section -split '(?m)^## ')[0]
        foreach ($library in $script:Manifest.Libraries) {
            $row = ($section -split "`n") | Where-Object { $_ -match ('^\|\s*`' + [regex]::Escape($library.Name) + '`\s*\|') } | Select-Object -First 1
            $row | Should -Not -BeNullOrEmpty -Because "$($library.Name) needs a row in section 4b"
            $row | Should -Match ('\|\s*' + [regex]::Escape($library.Version) + '\s*\|') -Because "$($library.Name) version must match the map"
        }
    }

    It 'restores only into gitignored folders, so no vendor or font file is ever committed' {
        $appDirectory = Join-Path $script:RepoRoot "src/$script:AppName"
        foreach ($library in $script:Manifest.Libraries) {
            $probe = "src/$script:AppName/$($library.Destination)/probe.file"
            & git -C $script:RepoRoot check-ignore --quiet $probe
            $LASTEXITCODE | Should -Be 0 -Because "$probe must be gitignored"
        }
    }
}
