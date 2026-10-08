BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:WorkflowPath = Join-Path $script:RepoRoot '.github/workflows/publish-nuget.yml'

    function Get-RepoText {
        param([string]$RelativePath)
        return Get-Content -LiteralPath (Join-Path $script:RepoRoot $RelativePath) -Raw
    }

    # The text of one top-level job: from its "  <job>:" line to the next job or the end of the file.
    function Get-JobText {
        param([string]$Text, [string]$Job)
        return [regex]::Match($Text, "(?ms)^  ${Job}:\r?\n.*?(?=^  [a-z][a-z-]*:\r?\n|\z)").Value
    }
}

Describe 'publish-nuget.yml' {
    BeforeAll {
        $script:Exists = Test-Path -LiteralPath $script:WorkflowPath
        $script:Text = if ($script:Exists) { Get-Content -LiteralPath $script:WorkflowPath -Raw } else { '' }
        $script:Pack = Get-JobText $script:Text 'pack'
        $script:Publish = Get-JobText $script:Text 'publish'
    }

    It 'exists and is named Publish NuGet packages' {
        $script:Exists | Should -BeTrue
        $script:Text | Should -Match '(?m)^name: Publish NuGet packages\s*$'
    }

    It 'triggers on v* tags and a manual dry run, and never on a pull request' {
        $script:Text | Should -Match "(?ms)^on:\s*\r?\n  push:\s*\r?\n    tags: \['v\*'\]\s*\r?\n  workflow_dispatch:"
        $script:Text | Should -Not -Match 'pull_request'
        $script:Text | Should -Match '(?ms)^permissions:\s*\r?\n  contents: read\s*\r?\n\s*\r?\njobs:'
    }

    It 'never uses a long-lived NuGet API key secret' {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot '.github') -Recurse -File) {
            (Get-Content -LiteralPath $file.FullName -Raw) | Should -Not -Match 'secrets\.NUGET_API_KEY' -Because "$($file.Name) must not read a NuGet API key secret (the NuGet/login output named NUGET_API_KEY is short-lived and fine)"
        }
    }

    It 'pack: builds on ubuntu with full history, derives the version from the tag and fails on a bad tag' {
        $script:Pack | Should -Match 'runs-on: ubuntu-latest'
        $script:Pack | Should -Match 'fetch-depth: 0'
        $script:Pack | Should -Match ([regex]::Escape("'^v(\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?)$'"))
        $script:Pack | Should -Match 'throw "Tag'
        $script:Pack | Should -Match ([regex]::Escape('0.0.0-dryrun.${{ github.run_number }}'))
        $script:Pack | Should -Match 'global-json-file: global\.json'
    }

    It 'pack: runs the .NET tests, packs the three packages with the version, checks the contents and uploads them' {
        $script:Pack | Should -Match ([regex]::Escape('dotnet test --solution TechStrap.CI.slnf -c Release --no-build'))
        foreach ($project in 'TechStrap.Contracts', 'TechStrap.Client', 'TechStrap.Client.Maui') {
            $script:Pack | Should -Match ([regex]::Escape($project)) -Because "$project is packed"
        }
        $script:Pack | Should -Match 'dotnet pack .*-p:Version=\$\{\{ steps\.version\.outputs\.version \}\}'
        $script:Pack | Should -Match ([regex]::Escape('./scripts/Test-PackageContents.ps1'))
        $script:Pack | Should -Match 'actions/upload-artifact@'
        $script:Pack | Should -Match 'name: nuget-packages'
        $script:Pack | Should -Match ([regex]::Escape('version: ${{ steps.version.outputs.version }}'))
        $script:Pack | Should -Not -Match 'nuget push'
    }

    It 'pack: expects the same dependency map as ci.yml' {
        $ci = Get-RepoText '.github/workflows/ci.yml'
        $lines = [regex]::Matches($ci, "(?m)^\s*'TechStrap\.[A-Za-z.]+'\s*=\s*@\([^\r\n]*\)")
        $lines.Count | Should -Be 3
        foreach ($line in $lines) {
            $parts = $line.Value -split '=', 2
            $name = $parts[0].Trim()
            $value = $parts[1].Trim()
            ($script:Pack -replace '\s+', ' ') | Should -Match ([regex]::Escape($name + ' = ' + $value)) -Because 'the tag workflow must check what ci.yml checks'
        }
    }

    It 'publish: runs only for a tag, after pack, in the release environment with OIDC and release permissions' {
        $script:Publish | Should -Match 'needs: pack'
        $script:Publish | Should -Match "if: github\.ref_type == 'tag'"
        $script:Publish | Should -Match 'environment: release'
        $script:Publish | Should -Match '(?ms)permissions:\s*\r?\n\s+contents: write\s*\r?\n\s+id-token: write'
        $script:Publish | Should -Match 'actions/download-artifact@'
    }

    It 'publish: checks the packed version is the tag, logs in with Trusted Publishing and pushes with --skip-duplicate' {
        $script:Publish | Should -Match ([regex]::Escape('artifacts/$p.$v.nupkg'))
        $script:Publish | Should -Match 'throw .*does not match tag'
        $script:Publish | Should -Match 'uses: NuGet/login@v1'
        $script:Publish | Should -Match ([regex]::Escape('user: "${{ secrets.NUGET_USER }}"'))
        $script:Publish | Should -Match 'dotnet nuget push "artifacts/\*\.nupkg".*--source https://api\.nuget\.org/v3/index\.json.*--skip-duplicate'
        $script:Publish | Should -Match ([regex]::Escape('steps.login.outputs.NUGET_API_KEY'))
        $script:Publish.IndexOf('The packed version is the tag') | Should -BeLessThan $script:Publish.IndexOf('NuGet/login@v1')
        $script:Publish.IndexOf('NuGet/login@v1') | Should -BeLessThan $script:Publish.IndexOf('dotnet nuget push')
    }

    It 'publish: creates the GitHub Release with generated notes, prerelease when the version has a hyphen' {
        $script:Publish | Should -Match 'gh release create'
        $script:Publish | Should -Match '--generate-notes'
        $script:Publish | Should -Match '--prerelease'
        $script:Publish | Should -Match "-match '-'"
        $script:Publish | Should -Match ([regex]::Escape('GH_TOKEN: "${{ github.token }}"'))
        $script:Publish | Should -Match ([regex]::Escape('Get-ChildItem artifacts -File')) -Because 'the nupkg and snupkg files are listed explicitly (pwsh does not expand globs for native commands)'
        $script:Publish | Should -Match ([regex]::Escape('\.s?nupkg$'))
    }
}

Describe 'release.yml and RELEASING.md' {
    It 'release.yml points to publish-nuget.yml for the packages and the GitHub Release' {
        (Get-RepoText '.github/workflows/release.yml') | Should -Match 'NuGet packages and the GitHub Release come from publish-nuget\.yml on the same tag'
    }

    It 'RELEASING.md has its sections and the exact Trusted Publishing values, and no secret values' {
        $doc = Get-RepoText 'docs/development/RELEASING.md'
        foreach ($heading in 'What a v* tag does', 'Versioning', 'One-time nuget.org setup', 'Dry run', 'Cutting a release', 'Post-publish check', 'Rollback') {
            $doc | Should -Match ('(?m)^## ' + [regex]::Escape($heading)) -Because "RELEASING.md needs a $heading section"
        }
        foreach ($phrase in 'Syntax-Circus/techstrap', 'publish-nuget.yml', 'release.yml', 'NUGET_USER', 'release', 'unlist') {
            $doc | Should -Match ([regex]::Escape($phrase)) -Because "RELEASING.md must mention $phrase"
        }
        $doc | Should -Not -Match 'NUGET_API_KEY'
    }

    It 'CLIENT-SDK.md Local pack links to RELEASING.md' {
        $sdk = Get-RepoText 'docs/development/CLIENT-SDK.md'
        $section = [regex]::Match($sdk, '(?ms)^## Local pack.*?(?=^## )').Value
        $section | Should -Match ([regex]::Escape('(RELEASING.md)'))
    }
}
