BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Check-PackageVersions.ps1'

    function New-FakeRepo {
        param(
            [string]$Root,
            [string]$PropsVersion = '1.2.3',
            [string]$ProjectReference = '<PackageReference Include="Foo.Bar" />',
            [string]$ExtraProps = ''
        )

        New-Item -ItemType Directory -Force -Path (Join-Path $Root 'docs/architecture') | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $Root 'src/App') | Out-Null

        @'
| Concern | Status | Package | Exact version | Source |
| --- | --- | --- | --- | --- |
| Thing | Selected | `Foo.Bar` | 1.2.3 | link |
| Other | Not applicable | `Skipped.Pkg` | n/a | link |

| Package | Status | Exact version | Source |
| --- | --- | --- | --- |
| `Base.Pkg` (+ `.Design`) | Selected | 10.0.12 (older repo 10.0.11) | link |
| `Pester` (module) | Selected | 6.2.0 | link |
'@ | Set-Content -LiteralPath (Join-Path $Root 'docs/architecture/03-PACKAGE-MAP.md')

        @"
<Project>
  <ItemGroup>
    <PackageVersion Include="Foo.Bar" Version="$PropsVersion" />
    <PackageVersion Include="Base.Pkg" Version="10.0.12" />
    <PackageVersion Include="Base.Pkg.Design" Version="10.0.12" />
    $ExtraProps
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $Root 'Directory.Packages.props')

        @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    $ProjectReference
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $Root 'src/App/App.csproj')
    }

    function Invoke-Check {
        param([string]$Root)
        $output = & pwsh -NoProfile -File $script:ScriptPath -RepoRoot $Root 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
}

Describe 'Check-PackageVersions.ps1' {
    It 'passes when props, map and projects agree' {
        New-FakeRepo -Root $TestDrive
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 0
    }

    It 'fails when a project has an inline Version' {
        New-FakeRepo -Root $TestDrive -ProjectReference '<PackageReference Include="Foo.Bar" Version="1.2.3" />'
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'inline Version'
    }

    It 'fails when a props version differs from the map' {
        New-FakeRepo -Root $TestDrive -PropsVersion '9.9.9'
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match "Foo.Bar.*9.9.9.*1.2.3"
    }

    It 'fails when the props file has a package the map does not select' {
        New-FakeRepo -Root $TestDrive -ExtraProps '<PackageVersion Include="Rogue.Pkg" Version="1.0.0" />'
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Rogue.Pkg'
    }

    It 'fails when a Selected package is missing from props' {
        New-FakeRepo -Root $TestDrive
        $props = Join-Path $TestDrive 'Directory.Packages.props'
        (Get-Content -LiteralPath $props) | Where-Object { $_ -notmatch 'Base.Pkg.Design' } | Set-Content -LiteralPath $props
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Base.Pkg.Design'
    }
}

Describe 'the real repository' {
    It 'passes against the checked-in package map' {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
        $result = Invoke-Check -Root $repoRoot
        $result.ExitCode | Should -Be 0
    }
}
