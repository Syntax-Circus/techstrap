<#
.SYNOPSIS
  Verifies the contents of packed .nupkg files (pack dry run).
.DESCRIPTION
  For every expected package id, finds <id>.<version>.nupkg in -PackageDirectory and fails (exit 1) when:
    - no such .nupkg exists,
    - README.md is not at the package root or the nuspec does not declare <readme>README.md</readme>,
    - the license is not <license type="expression">MIT</license>,
    - the nuspec has no repository url,
    - the dependency ids (all target framework groups, case-insensitive) differ from the expected set,
    - the matching .snupkg symbol package is missing,
    - the XML documentation file lib/net10.0/<id>.xml is not in the package.
  All problems are reported, one line each, as "<id>: <what is wrong>".
.PARAMETER PackageDirectory
  Directory holding the packed .nupkg / .snupkg files.
.PARAMETER Expected
  Hashtable of package id to the string[] of dependency ids that package must have.
.EXAMPLE
  ./scripts/Test-PackageContents.ps1 -PackageDirectory ./pack -Expected @{ 'TechStrap.Contracts' = @() }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,
    [Parameter(Mandatory)]
    [hashtable]$Expected
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$errors = New-Object System.Collections.Generic.List[string]
$passed = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) { throw "Package directory not found: $PackageDirectory" }

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' -File)

foreach ($id in ($Expected.Keys | Sort-Object)) {
    $problems = New-Object System.Collections.Generic.List[string]
    $pattern = '^' + [regex]::Escape($id) + '\.\d+\.\d+\.\d+.*\.nupkg$'
    $matching = @($packages | Where-Object { $_.Name -match $pattern })

    if ($matching.Count -eq 0) {
        $errors.Add("${id}: no .nupkg found in $PackageDirectory.")
        continue
    }

    $file = $matching[0]
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspecEntry) {
            $errors.Add("${id}: the package has no .nuspec at its root.")
            continue
        }
        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }

    if ($entryNames -cnotcontains 'README.md') {
        $problems.Add('README.md is not in the package root.')
    }

    $readme = @($nuspec.GetElementsByTagName('readme') | ForEach-Object { $_.InnerText.Trim() })
    if ($readme -cnotcontains 'README.md') {
        $problems.Add('the nuspec does not declare <readme>README.md</readme>.')
    }

    $licenses = @($nuspec.GetElementsByTagName('license'))
    $licenseOk = $licenses.Count -eq 1 -and $licenses[0].GetAttribute('type') -eq 'expression' -and $licenses[0].InnerText.Trim() -ceq 'MIT'
    if (-not $licenseOk) {
        $found = if ($licenses.Count -eq 0) { 'none' } else { "$($licenses[0].GetAttribute('type')) '$($licenses[0].InnerText.Trim())'" }
        $problems.Add("the license is not an MIT expression (found: $found).")
    }

    $repositories = @($nuspec.GetElementsByTagName('repository'))
    if ($repositories.Count -eq 0 -or [string]::IsNullOrWhiteSpace($repositories[0].GetAttribute('url'))) {
        $problems.Add('the nuspec has no repository url.')
    }

    $actual = @($nuspec.GetElementsByTagName('dependency') | ForEach-Object { $_.GetAttribute('id') } | Sort-Object -Unique)
    $wanted = @($Expected[$id] | Where-Object { $_ } | Sort-Object -Unique)
    $actualSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$actual, [System.StringComparer]::OrdinalIgnoreCase)
    $wantedSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$wanted, [System.StringComparer]::OrdinalIgnoreCase)
    $extra = @($actualSet | Where-Object { -not $wantedSet.Contains($_) } | Sort-Object)
    $missing = @($wantedSet | Where-Object { -not $actualSet.Contains($_) } | Sort-Object)
    if ($extra.Count -gt 0) { $problems.Add("unexpected dependency: $($extra -join ', ').") }
    if ($missing.Count -gt 0) { $problems.Add("missing dependency: $($missing -join ', ').") }

    $symbols = [System.IO.Path]::ChangeExtension($file.FullName, '.snupkg')
    if (-not (Test-Path -LiteralPath $symbols -PathType Leaf)) {
        $problems.Add("the symbol package $([System.IO.Path]::GetFileName($symbols)) is missing (no snupkg).")
    }

    if ($entryNames -cnotcontains "lib/net10.0/$id.xml") {
        $problems.Add("the XML documentation file lib/net10.0/$id.xml is missing.")
    }

    if ($problems.Count -gt 0) {
        foreach ($problem in $problems) { $errors.Add("${id}: $problem") }
    }
    else {
        $passed.Add("${id}: $($file.Name) ok ($($actualSet.Count) dependencies).")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    Write-Host "Package contents check failed with $($errors.Count) problem(s)."
    exit 1
}

$passed | ForEach-Object { Write-Host $_ }
exit 0
