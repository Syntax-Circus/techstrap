<#
.SYNOPSIS
  Verifies central package management against docs/architecture/03-PACKAGE-MAP.md.
.DESCRIPTION
  Fails (exit 1) when:
    - a project file states an inline Version / VersionOverride on a PackageReference,
    - a package marked Selected in the package map is missing from Directory.Packages.props,
    - a version in Directory.Packages.props differs from the package map,
    - Directory.Packages.props contains a package that the package map does not select.
  Pester (PowerShell Gallery) and dotnet-ef (dotnet tool) are listed in the map but are not NuGet
  package versions, so they are skipped.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$MapPath = (Join-Path $RepoRoot 'docs/architecture/03-PACKAGE-MAP.md'),
    [string]$PropsPath = (Join-Path $RepoRoot 'Directory.Packages.props')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$NonNuGetItems = @('Pester', 'dotnet-ef')
$errors = New-Object System.Collections.Generic.List[string]

function Get-MapVersions {
    param([string]$Path)

    $versions = @{}
    $packageColumn = -1
    $statusColumn = -1
    $versionColumn = -1

    foreach ($line in Get-Content -LiteralPath $Path) {
        if (-not $line.TrimStart().StartsWith('|')) {
            $packageColumn = $statusColumn = $versionColumn = -1
            continue
        }

        $cells = @($line.Trim().Trim('|') -split '\|' | ForEach-Object { $_.Trim() })

        if ($cells -contains 'Package' -and $cells -contains 'Status' -and $cells -contains 'Exact version') {
            $packageColumn = [array]::IndexOf($cells, 'Package')
            $statusColumn = [array]::IndexOf($cells, 'Status')
            $versionColumn = [array]::IndexOf($cells, 'Exact version')
            continue
        }

        if ($packageColumn -lt 0 -or $cells.Count -le [Math]::Max($versionColumn, [Math]::Max($packageColumn, $statusColumn))) {
            continue
        }

        if ($cells[$statusColumn] -ne 'Selected') {
            continue
        }

        if ($cells[$versionColumn] -notmatch '^(\d+\.\d+\.\d+[^\s(]*)') {
            continue
        }
        $version = $Matches[1]

        $tokens = @([regex]::Matches($cells[$packageColumn], '`([^`]+)`') | ForEach-Object { $_.Groups[1].Value })
        if ($tokens.Count -eq 0) {
            continue
        }

        $baseName = $tokens[0]
        foreach ($token in $tokens) {
            $name = if ($token.StartsWith('.')) { "$baseName$token" } else { $token }
            if ($NonNuGetItems -contains $name) {
                continue
            }
            $versions[$name] = $version
        }
    }

    return $versions
}

if (-not (Test-Path -LiteralPath $MapPath)) { throw "Package map not found: $MapPath" }
if (-not (Test-Path -LiteralPath $PropsPath)) { throw "Directory.Packages.props not found: $PropsPath" }

$mapVersions = Get-MapVersions -Path $MapPath

[xml]$propsXml = Get-Content -LiteralPath $PropsPath -Raw
$propsVersions = @{}
foreach ($node in $propsXml.SelectNodes('//PackageVersion')) {
    $propsVersions[$node.GetAttribute('Include')] = $node.GetAttribute('Version')
}

foreach ($name in ($mapVersions.Keys | Sort-Object)) {
    if (-not $propsVersions.ContainsKey($name)) {
        $errors.Add("Package '$name' ($($mapVersions[$name])) is Selected in the package map but missing from Directory.Packages.props.")
    }
    elseif ($propsVersions[$name] -ne $mapVersions[$name]) {
        $errors.Add("Package '$name' is $($propsVersions[$name]) in Directory.Packages.props but $($mapVersions[$name]) in the package map.")
    }
}

foreach ($name in ($propsVersions.Keys | Sort-Object)) {
    if (-not $mapVersions.ContainsKey($name)) {
        $errors.Add("Package '$name' is in Directory.Packages.props but is not Selected in the package map.")
    }
}

$projectFiles = Get-ChildItem -LiteralPath $RepoRoot -Recurse -Include *.csproj, *.props, *.targets -File |
    Where-Object { $_.FullName -notmatch '[\/](bin|obj|node_modules|\.git)[\/]' -and $_.Name -ne 'Directory.Packages.props' }

foreach ($file in $projectFiles) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($reference in $xml.SelectNodes('//PackageReference')) {
        foreach ($attribute in 'Version', 'VersionOverride') {
            if ($reference.HasAttribute($attribute)) {
                $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $file.FullName)
                $errors.Add("$relative : PackageReference '$($reference.GetAttribute('Include'))' has an inline $attribute. Versions live in Directory.Packages.props.")
            }
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    Write-Host "Package version check failed with $($errors.Count) problem(s)."
    exit 1
}

Write-Host "Package version check passed: $($mapVersions.Count) packages match the package map."
exit 0
