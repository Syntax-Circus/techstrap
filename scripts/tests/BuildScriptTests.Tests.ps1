BeforeAll {
    $script:ScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' 'Build-TechStrapDocker.ps1')).Path

    # Dot-sourcing loads the functions only; the script returns before Invoke-Main.
    . $script:ScriptPath

    function Invoke-BuildScript {
        param([string[]]$Arguments)
        $output = & pwsh -NoProfile -File $script:ScriptPath @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }

    function Get-DockerLines {
        param([string]$Output)
        return @($Output -split "`r?`n" | Where-Object { $_ -match '^docker buildx build' })
    }
}

Describe 'Build-TechStrapDocker.ps1 -DryRun' {
    It 'prints four buildx commands carrying the version build args' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-ImageTag', '1.2.3', '-Platforms', 'linux/amd64')
        $result.ExitCode | Should -Be 0

        $lines = @(Get-DockerLines -Output $result.Output)
        $lines.Count | Should -Be 4
        foreach ($line in $lines) {
            $line | Should -Match '--build-arg BUILD_VERSION=1\.2\.3'
            $line | Should -Match '--build-arg BUILD_INFORMATIONAL_VERSION=1\.2\.3'
            $line | Should -Match '--build-arg DISABLE_GITVERSION_TASK=true'
        }
        foreach ($name in 'api', 'admin', 'portal', 'worker') {
            @($lines | Where-Object { $_ -match "-f Dockerfile\.$name " }).Count | Should -Be 1
            @($lines | Where-Object { $_ -match "-t techstrap-${name}:1\.2\.3 " }).Count | Should -Be 1
        }
    }

    It 'rejects an ImageTag that is not SemVer' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-ImageTag', 'not-semver')
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'not valid SemVer'
    }

    It 'rejects an unknown target' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-ImageTag', '1.2.3', '-Targets', 'bogus')
        $result.ExitCode | Should -Not -Be 0
    }

    It 'resolves the version from GitVersion when no ImageTag is given' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-Platforms', 'linux/amd64', '-Targets', 'api')
        $result.ExitCode | Should -Be 0
        @(Get-DockerLines -Output $result.Output)[0] | Should -Match 'BUILD_VERSION=\d+\.\d+\.\d+'
    }
}

Describe 'Test-SemVer' {
    It 'accepts <_>' -ForEach '0.1.0', '1.2.3-rc.1', '1.0.0-uat.dirty.20260823', '2.0.0+build.5' {
        Test-SemVer -Version $_ | Should -BeTrue
    }

    It 'rejects <_>' -ForEach 'not-semver', '1.2', 'v1.2.3', '1.2.3.4', '' {
        Test-SemVer -Version $_ | Should -BeFalse
    }
}

Describe 'Get-ImageTags' {
    It 'adds latest by default' {
        Get-ImageTags -ImageTag '1.2.3' | Should -Be @('1.2.3', 'latest')
    }

    It 'drops latest when PushLatest is false' {
        Get-ImageTags -ImageTag '1.2.3' -PushLatest $false | Should -Be @('1.2.3')
    }

    It 'adds a distinct SemVer tag and removes duplicates' {
        Get-ImageTags -ImageTag '1.2.3-uat.1' -SemVerTag '1.2.3' | Should -Be @('1.2.3-uat.1', '1.2.3', 'latest')
        Get-ImageTags -ImageTag '1.2.3' -SemVerTag '1.2.3' | Should -Be @('1.2.3', 'latest')
    }
}

Describe 'Get-BuildPlan' {
    BeforeAll {
        $script:Common = @{
            Targets = @('api', 'worker')
            Tags = @('1.2.3', 'latest')
            BuildVersion = '1.2.3'
            InformationalVersion = '1.2.3+5.Branch.main'
        }
    }

    It 'builds one multi-platform push per image when pushing to a registry' {
        $plan = Get-BuildPlan @script:Common -Registry 'ghcr.io/syntax-circus/' -Push $true

        $plan.Count | Should -Be 2
        foreach ($step in $plan) {
            $step.Mode | Should -Be 'push'
            $step.Arguments | Should -Contain '--push'
            $step.Arguments | Should -Not -Contain '--load'
            $step.Arguments[($step.Arguments.IndexOf('--platform') + 1)] | Should -Be 'linux/amd64,linux/arm64'
        }
        $plan[0].Images | Should -Be @('ghcr.io/syntax-circus/techstrap-api:1.2.3', 'ghcr.io/syntax-circus/techstrap-api:latest')
    }

    It 'builds each platform separately with --load and suffixes the tags when not pushing' {
        $plan = Get-BuildPlan @script:Common -Push $false

        $plan.Count | Should -Be 4
        $amd64 = $plan | Where-Object { $_.Target -eq 'api' -and $_.Platform -eq 'linux/amd64' }
        $arm64 = $plan | Where-Object { $_.Target -eq 'api' -and $_.Platform -eq 'linux/arm64' }

        $amd64.Arguments | Should -Contain '--load'
        $amd64.Images | Should -Be @('techstrap-api:1.2.3-amd64', 'techstrap-api:latest-amd64', 'techstrap-api:1.2.3', 'techstrap-api:latest')
        $arm64.Images | Should -Be @('techstrap-api:1.2.3-arm64', 'techstrap-api:latest-arm64')
    }

    It 'falls back to local builds when -Push is set without a registry' {
        $plan = Get-BuildPlan @script:Common -Push $true -Registry ''

        $plan | ForEach-Object { $_.Mode | Should -Be 'local' }
    }

    It 'passes the version build args and disables the GitVersion task in every command' {
        $plan = Get-BuildPlan @script:Common

        foreach ($step in $plan) {
            $step.Arguments | Should -Contain 'BUILD_VERSION=1.2.3'
            $step.Arguments | Should -Contain 'BUILD_INFORMATIONAL_VERSION=1.2.3+5.Branch.main'
            $step.Arguments | Should -Contain 'DISABLE_GITVERSION_TASK=true'
        }
    }

    It 'adds --no-cache when requested' {
        (Get-BuildPlan @script:Common -NoCache $true)[0].Arguments | Should -Contain '--no-cache'
    }
}
