BeforeAll {
    $script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'Test-PackageContents.ps1'

    function New-FakePackage {
        param(
            [string]$Directory,
            [string]$Id,
            [string[]]$Dependencies = @(),
            [bool]$Readme = $true,
            [bool]$ReadmeMetadata = $true,
            [string]$License = '<license type="expression">MIT</license>',
            [string]$Repository = '<repository type="git" url="https://github.com/Syntax-Circus/techstrap.git" />',
            [bool]$Symbols = $true
        )

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $dependencyXml = ($Dependencies | ForEach-Object { "<dependency id=`"$_`" version=`"1.0.0`" exclude=`"Build,Analyzers`" />" }) -join ''
        $readmeXml = if ($ReadmeMetadata) { '<readme>README.md</readme>' } else { '' }
        $nuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$Id</id>
    <version>0.0.0-ci</version>
    $License
    $readmeXml
    $Repository
    <dependencies><group targetFramework="net10.0">$dependencyXml</group></dependencies>
  </metadata>
</package>
"@
        $path = Join-Path $Directory "$Id.0.0.0-ci.nupkg"
        $zip = [System.IO.Compression.ZipFile]::Open($path, 'Create')
        try {
            $entry = $zip.CreateEntry("$Id.nuspec")
            $writer = New-Object System.IO.StreamWriter($entry.Open())
            $writer.Write($nuspec)
            $writer.Dispose()
            if ($Readme) {
                $entry = $zip.CreateEntry('README.md')
                $writer = New-Object System.IO.StreamWriter($entry.Open())
                $writer.Write('# readme')
                $writer.Dispose()
            }
        }
        finally { $zip.Dispose() }

        if ($Symbols) {
            [System.IO.File]::WriteAllBytes((Join-Path $Directory "$Id.0.0.0-ci.snupkg"), [byte[]]@())
        }
    }

    $script:ClientDeps = @('TechStrap.Contracts', 'SyntaxCircus.Common')

    function New-GoodSet {
        param([string]$Directory, [hashtable]$ClientOverrides = @{}, [hashtable]$ContractsOverrides = @{})
        New-Item -ItemType Directory -Force -Path $Directory | Out-Null
        New-FakePackage -Directory $Directory -Id 'Fake.Contracts' @ContractsOverrides
        $clientArgs = @{ Directory = $Directory; Id = 'Fake.Client'; Dependencies = $script:ClientDeps }
        foreach ($key in $ClientOverrides.Keys) { $clientArgs[$key] = $ClientOverrides[$key] }
        New-FakePackage @clientArgs
    }

    function Invoke-Check {
        param([string]$Directory, [hashtable]$Expected = $null)
        if ($null -eq $Expected) { $Expected = @{ 'Fake.Contracts' = @(); 'Fake.Client' = $script:ClientDeps } }
        # -File would flatten the hashtable to a string, so the call goes through -Command with a literal.
        $literal = '@{ ' + (($Expected.Keys | ForEach-Object { "'$_' = @(" + ((@($Expected[$_]) | ForEach-Object { "'$_'" }) -join ',') + ')' }) -join '; ') + ' }'
        $command = "& '$($script:ScriptPath)' -PackageDirectory '$Directory' -Expected $literal; exit `$LASTEXITCODE"
        $output = & pwsh -NoProfile -Command $command 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
}

Describe 'Test-PackageContents.ps1' {
    BeforeEach { $script:Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString('N')) }

    It 'passes when every package is as expected' {
        New-GoodSet -Directory $script:Dir
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 0
        $result.Output | Should -Match 'Fake.Client'
        $result.Output | Should -Match 'Fake.Contracts'
    }

    It 'fails when the README entry is missing from the package' {
        New-GoodSet -Directory $script:Dir -ClientOverrides @{ Readme = $false }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Client: .*README\.md.*not in the package'
    }

    It 'fails when the nuspec does not declare the readme' {
        New-GoodSet -Directory $script:Dir -ClientOverrides @{ ReadmeMetadata = $false }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Client: .*readme'
    }

    It 'fails when the license is MIT-0' {
        New-GoodSet -Directory $script:Dir -ContractsOverrides @{ License = '<license type="expression">MIT-0</license>' }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Contracts: .*license'
    }

    It 'fails when the license is a file' {
        New-GoodSet -Directory $script:Dir -ContractsOverrides @{ License = '<license type="file">LICENSE.txt</license>' }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Contracts: .*license'
    }

    It 'fails when the repository url is missing' {
        New-GoodSet -Directory $script:Dir -ClientOverrides @{ Repository = '' }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Client: .*repository'
    }

    It 'fails when a dependency is not expected' {
        New-GoodSet -Directory $script:Dir -ClientOverrides @{ Dependencies = ($script:ClientDeps + 'Rogue.Pkg') }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Client: .*unexpected dependency.*Rogue\.Pkg'
    }

    It 'fails when an expected dependency is missing' {
        New-GoodSet -Directory $script:Dir -ClientOverrides @{ Dependencies = @('TechStrap.Contracts') }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Client: .*missing dependency.*SyntaxCircus\.Common'
    }

    It 'fails when the symbol package is missing' {
        New-GoodSet -Directory $script:Dir -ContractsOverrides @{ Symbols = $false }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Contracts: .*snupkg'
    }

    It 'fails when an expected package is not in the directory' {
        New-GoodSet -Directory $script:Dir
        $expected = @{ 'Fake.Contracts' = @(); 'Fake.Client' = $script:ClientDeps; 'Fake.Absent' = @() }
        $result = Invoke-Check -Directory $script:Dir -Expected $expected
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Absent: .*no \.nupkg'
    }

    It 'reports every failure, not just the first' {
        New-GoodSet -Directory $script:Dir -ClientOverrides @{ Readme = $false } -ContractsOverrides @{ Symbols = $false }
        $result = Invoke-Check -Directory $script:Dir
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Fake\.Client: '
        $result.Output | Should -Match 'Fake\.Contracts: '
    }
}

Describe 'the pack dry run in CI' {
    BeforeAll {
        $script:Workflow = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.github' 'workflows' 'ci.yml') -Raw
        $script:Slnf = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'TechStrap.CI.slnf') -Raw
    }

    It 'packs all three packages and checks their contents between Test and the EF check' {
        $script:Workflow | Should -Match '(?s)- name: Test\s.*- name: Pack dry run \(TechStrap\.Contracts, TechStrap\.Client, TechStrap\.Client\.Maui\).*scripts/Test-PackageContents\.ps1.*- name: Check for pending EF model changes'
        $script:Workflow | Should -Match 'dotnet pack src/TechStrap\.Contracts -c Release --no-build -p:Version=0\.0\.0-ci'
        $script:Workflow | Should -Match 'dotnet pack src/TechStrap\.Client -c Release --no-build -p:Version=0\.0\.0-ci'
        $script:Workflow | Should -Match 'dotnet pack src/TechStrap\.Client\.Maui -c Release --no-build -p:Version=0\.0\.0-ci'
        $script:Workflow | Should -Match "'TechStrap\.Client\.Maui'\s*=\s*@\("
    }

    It 'lists the client tests in the CI solution filter' {
        $script:Slnf | Should -Match 'tests/TechStrap\.Client\.Tests/TechStrap\.Client\.Tests\.csproj'
    }

    It 'lists the Client.Maui project and its tests in the CI solution filter' {
        $script:Slnf | Should -Match 'src/TechStrap\.Client\.Maui/TechStrap\.Client\.Maui\.csproj'
        $script:Slnf | Should -Match 'tests/TechStrap\.Client\.Maui\.Tests/TechStrap\.Client\.Maui\.Tests\.csproj'
    }
}
