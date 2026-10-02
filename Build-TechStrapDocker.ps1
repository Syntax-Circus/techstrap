<#
.SYNOPSIS
  Builds (and optionally pushes) the four TechStrap container images.
.DESCRIPTION
  Images are named techstrap-{api,admin,portal,worker}. The version comes from -ImageTag, or from
  GitVersion (dotnet msbuild -target:GetVersion, then dotnet-gitversion, then gitversion) and must be
  valid SemVer.

  -Push with a -Registry runs one multi-platform `docker buildx build --push` per image.
  Otherwise each platform is built separately with --load: tags get an -amd64 / -arm64 suffix, and the
  amd64 build also gets the canonical tags.

  -DryRun prints the docker commands and runs nothing (no Docker needed).
.EXAMPLE
  ./Build-TechStrapDocker.ps1 -Platforms linux/amd64
.EXAMPLE
  ./Build-TechStrapDocker.ps1 -Push -Registry ghcr.io/syntax-circus -ImageTag 0.1.0
#>
[CmdletBinding()]
param(
    [ValidateSet('api', 'admin', 'portal', 'worker')]
    [string[]]$Targets = @('api', 'admin', 'portal', 'worker'),

    [string]$ImageTag = '',

    [string]$SemVerTag = '',

    [string]$Registry = '',

    [switch]$Push,

    [bool]$PushLatest = $true,

    [switch]$NoCache,

    [ValidateSet('linux/amd64', 'linux/arm64')]
    [string[]]$Platforms = @('linux/amd64', 'linux/arm64'),

    [string]$VersionProjectPath = 'src/TechStrap.Api/TechStrap.Api.csproj',

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:ImageNamePrefix = 'techstrap-'
$script:SemVerPattern = '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

function Test-SemVer {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Version)
    return $Version -match $script:SemVerPattern
}

function Get-PlatformSuffix {
    param([Parameter(Mandatory)][string]$Platform)

    switch ($Platform) {
        'linux/amd64' { return 'amd64' }
        'linux/arm64' { return 'arm64' }
        default { throw "Unsupported platform '$Platform'." }
    }
}

function Get-ImageReference {
    param(
        [Parameter(Mandatory)][string]$ImageName,
        [Parameter(Mandatory)][string]$Tag,
        [string]$Registry = ''
    )

    $prefix = ''
    if (-not [string]::IsNullOrWhiteSpace($Registry)) {
        $prefix = $Registry.Trim().TrimEnd('/') + '/'
    }

    return "$prefix${ImageName}:$Tag"
}

function Get-ImageTags {
    param(
        [Parameter(Mandatory)][string]$ImageTag,
        [string]$SemVerTag = '',
        [bool]$PushLatest = $true
    )

    $tags = @($ImageTag, $SemVerTag)
    if ($PushLatest) {
        $tags += 'latest'
    }

    return @(
        $tags |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() } |
            Select-Object -Unique
    )
}

function Invoke-GitVersionCli {
    param(
        [string]$Command,
        [string[]]$Arguments
    )

    if ($null -eq (Get-Command -Name $Command -ErrorAction SilentlyContinue)) {
        return $null
    }

    $output = & $Command @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    try {
        $parsed = $output | ConvertFrom-Json
    }
    catch {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace($parsed.SemVer)) {
        return $null
    }

    return [pscustomobject]@{
        SemVer = [string]$parsed.SemVer
        InformationalVersion = [string]$parsed.InformationalVersion
    }
}

function Resolve-GitVersion {
    param(
        [Parameter(Mandatory)][string]$ProjectFile,
        [Parameter(Mandatory)][string]$ConfigFile
    )

    if (Test-Path -LiteralPath $ProjectFile) {
        $output = & dotnet msbuild $ProjectFile `
            -nologo `
            -verbosity:quiet `
            -target:GetVersion `
            -getProperty:GitVersion_SemVer `
            -getProperty:GitVersion_InformationalVersion 2>&1 | Out-String

        if ($LASTEXITCODE -eq 0) {
            try {
                $properties = ($output | ConvertFrom-Json).Properties
                if (-not [string]::IsNullOrWhiteSpace($properties.GitVersion_SemVer)) {
                    return [pscustomobject]@{
                        SemVer = [string]$properties.GitVersion_SemVer
                        InformationalVersion = [string]$properties.GitVersion_InformationalVersion
                    }
                }
            }
            catch {
                # Fall through to the CLI tools.
            }
        }
    }

    $cliArguments = @('/output', 'json', '/config', $ConfigFile)
    foreach ($command in 'dotnet-gitversion', 'gitversion') {
        $resolved = Invoke-GitVersionCli -Command $command -Arguments $cliArguments
        if ($null -ne $resolved) {
            return $resolved
        }
    }

    throw 'Unable to resolve a version with GitVersion. Install GitVersion or pass -ImageTag explicitly.'
}

function Get-BuildPlan {
    <#
      Pure function: turns the parameters into the ordered list of docker commands. Each result has
      Target, Mode ('push' or 'local'), Platform, Images and Arguments (the arguments after `docker`).
    #>
    param(
        [Parameter(Mandatory)][string[]]$Targets,
        [Parameter(Mandatory)][string[]]$Tags,
        [Parameter(Mandatory)][string]$BuildVersion,
        [Parameter(Mandatory)][string]$InformationalVersion,
        [string]$Registry = '',
        [bool]$Push = $false,
        [bool]$NoCache = $false,
        [string[]]$Platforms = @('linux/amd64', 'linux/arm64')
    )

    $shouldPush = $Push -and -not [string]::IsNullOrWhiteSpace($Registry)
    $buildArguments = @(
        '--build-arg', "BUILD_VERSION=$BuildVersion",
        '--build-arg', "BUILD_INFORMATIONAL_VERSION=$InformationalVersion",
        '--build-arg', 'DISABLE_GITVERSION_TASK=true'
    )

    $plan = New-Object System.Collections.Generic.List[object]

    foreach ($target in ($Targets | Select-Object -Unique)) {
        $imageName = "$($script:ImageNamePrefix)$target"
        $dockerfile = "Dockerfile.$target"
        $canonical = @(
            $Tags |
                ForEach-Object { Get-ImageReference -ImageName $imageName -Tag $_ -Registry $Registry }
        )

        if ($shouldPush) {
            $arguments = @('buildx', 'build', '--platform', ($Platforms -join ','))
            foreach ($image in $canonical) {
                $arguments += @('-t', $image)
            }
            $arguments += $buildArguments
            $arguments += @('-f', $dockerfile)
            if ($NoCache) {
                $arguments += '--no-cache'
            }
            $arguments += @('--push', '.')

            $plan.Add([pscustomobject]@{
                    Target = $target
                    Mode = 'push'
                    Platform = ($Platforms -join ',')
                    Images = $canonical
                    Arguments = $arguments
                })
            continue
        }

        foreach ($platform in $Platforms) {
            $suffix = Get-PlatformSuffix -Platform $platform
            $images = @(
                $Tags |
                    ForEach-Object { Get-ImageReference -ImageName $imageName -Tag "$_-$suffix" -Registry $Registry }
            )
            if ($platform -eq 'linux/amd64') {
                $images += $canonical
            }

            $arguments = @('buildx', 'build', '--platform', $platform)
            foreach ($image in $images) {
                $arguments += @('-t', $image)
            }
            $arguments += $buildArguments
            $arguments += @('-f', $dockerfile)
            if ($NoCache) {
                $arguments += '--no-cache'
            }
            $arguments += @('--load', '.')

            $plan.Add([pscustomobject]@{
                    Target = $target
                    Mode = 'local'
                    Platform = $platform
                    Images = $images
                    Arguments = $arguments
                })
        }
    }

    return $plan.ToArray()
}

function Invoke-Main {
    if (-not [string]::IsNullOrWhiteSpace($ImageTag) -and -not (Test-SemVer -Version $ImageTag)) {
        throw "ImageTag '$ImageTag' is not valid SemVer. Use a value such as '0.1.0' or '0.1.0-rc.1'."
    }

    $buildVersion = $ImageTag
    $informationalVersion = $ImageTag
    $semVerTag = $SemVerTag

    if ([string]::IsNullOrWhiteSpace($ImageTag)) {
        $resolved = Resolve-GitVersion `
            -ProjectFile (Join-Path $PSScriptRoot $VersionProjectPath) `
            -ConfigFile (Join-Path $PSScriptRoot 'GitVersion.yml')

        $buildVersion = $resolved.SemVer
        $informationalVersion = $resolved.SemVer
        if (-not [string]::IsNullOrWhiteSpace($resolved.InformationalVersion)) {
            $informationalVersion = $resolved.InformationalVersion
        }
        $ImageTag = $resolved.SemVer
        if ([string]::IsNullOrWhiteSpace($semVerTag)) {
            $semVerTag = $resolved.SemVer
        }
    }

    if (-not (Test-SemVer -Version $buildVersion)) {
        throw "Build version '$buildVersion' is not valid SemVer."
    }

    if ($Push -and [string]::IsNullOrWhiteSpace($Registry)) {
        Write-Warning 'No -Registry given: -Push is ignored and the images are built locally per platform.'
    }

    $tags = Get-ImageTags -ImageTag $ImageTag -SemVerTag $semVerTag -PushLatest $PushLatest
    $plan = Get-BuildPlan `
        -Targets $Targets `
        -Tags $tags `
        -BuildVersion $buildVersion `
        -InformationalVersion $informationalVersion `
        -Registry $Registry `
        -Push ([bool]$Push) `
        -NoCache ([bool]$NoCache) `
        -Platforms $Platforms

    Write-Host "Version: $buildVersion  Tags: $($tags -join ', ')  Platforms: $($Platforms -join ', ')"

    if (-not $DryRun) {
        & docker info *> $null
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker is not running.'
        }
        & docker buildx version *> $null
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker buildx is required.'
        }
    }

    Push-Location $PSScriptRoot
    try {
        foreach ($step in $plan) {
            $line = "docker $($step.Arguments -join ' ')"
            if ($DryRun) {
                Write-Host $line
                continue
            }

            Write-Host "`n=== $($step.Target) [$($step.Mode), $($step.Platform)] ===`n$line`n"
            & docker @($step.Arguments)
            if ($LASTEXITCODE -ne 0) {
                throw "docker build failed for $($step.Target) ($($step.Platform)) with exit code $LASTEXITCODE."
            }
        }
    }
    finally {
        Pop-Location
    }
}

# Dot-sourcing (Pester) loads the functions only.
if ($MyInvocation.InvocationName -eq '.') {
    return
}

Invoke-Main
