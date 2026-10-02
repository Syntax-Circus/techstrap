<#
.SYNOPSIS
  Runs the Pester tests under scripts/tests with the pinned Pester version and fails (exit 1) on any failure.
.EXAMPLE
  pwsh ./scripts/Invoke-ScriptTests.ps1
.EXAMPLE
  pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Check-PackageVersions.Tests.ps1
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path $PSScriptRoot 'tests'),
    [ValidateSet('Normal', 'Detailed', 'Minimal')]
    [string]$Output = 'Detailed'
)

$ErrorActionPreference = 'Stop'

# Pinned in docs/architecture/03-PACKAGE-MAP.md (Pester is a PowerShell Gallery module, not a NuGet package).
$PesterVersion = '6.2.0'

if (-not (Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -eq [version]$PesterVersion })) {
    throw "Pester $PesterVersion is required. Install it with: Install-Module Pester -RequiredVersion $PesterVersion -Scope CurrentUser -Force -SkipPublisherCheck"
}

Import-Module Pester -RequiredVersion $PesterVersion
$result = Invoke-Pester -Path $Path -Output $Output -PassThru

if ($result.Result -ne 'Passed') {
    exit 1
}
