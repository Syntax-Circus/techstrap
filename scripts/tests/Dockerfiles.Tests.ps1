BeforeDiscovery {
    $script:Hosts = @('api', 'admin', 'portal', 'worker')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    function Get-DockerfileText {
        param([string]$Name)
        return Get-Content -LiteralPath (Join-Path $script:RepoRoot "Dockerfile.$Name") -Raw
    }
}

Describe 'Dockerfile.<_>' -ForEach $script:Hosts {
    BeforeAll {
        $script:Text = Get-DockerfileText -Name $_
        $script:Project = 'TechStrap.' + (Get-Culture).TextInfo.ToTitleCase($_)
    }

    It 'builds from the repository root with a BuildKit NuGet cache mount and copies the whole tree before restore' {
        $script:Text | Should -Match '# syntax=docker/dockerfile:1\.7'
        $script:Text | Should -Match '(?s)COPY \. \..*dotnet restore'
        $script:Text | Should -Match '--mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages'
    }

    It 'accepts the version build arguments and forwards them to publish' {
        foreach ($argument in 'BUILD_VERSION', 'BUILD_INFORMATIONAL_VERSION', 'DISABLE_GITVERSION_TASK') {
            $script:Text | Should -Match "ARG $argument="
        }
        $script:Text | Should -Match '/p:DisableGitVersionTask=\$\{DISABLE_GITVERSION_TASK\}'
    }

    It 'publishes the right project and starts it' {
        $script:Text | Should -Match "dotnet publish src/$($script:Project)/$($script:Project)\.csproj"
        $script:Text | Should -Match "ENTRYPOINT \[""dotnet"", ""$($script:Project)\.dll""\]"
    }

    It 'runs on the aspnet:10.0 runtime as non-root uid 10001 on port 80 with curl for health checks' {
        $script:Text | Should -Match 'FROM mcr\.microsoft\.com/dotnet/aspnet:10\.0'
        $script:Text | Should -Match 'ENV ASPNETCORE_URLS=http://\+:80'
        $script:Text | Should -Match 'USER 10001:10001'
        $script:Text | Should -Match 'apt-get install -y --no-install-recommends curl'
    }

    It 'pre-creates and chowns the storage, logs and dataprotection-keys mount points' {
        $script:Text | Should -Match 'mkdir -p /app/storage /app/logs /app/dataprotection-keys'
        $script:Text | Should -Match 'chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys'
    }
}

Describe 'compiled CSS assertion' {
    It 'Dockerfile.<_> fails the build when wwwroot/css/app.css is missing' -ForEach 'admin', 'portal' {
        (Get-DockerfileText -Name $_) | Should -Match 'RUN test -f /app/publish/wwwroot/css/app\.css'
    }

    It 'Dockerfile.<_> has no CSS assertion because it serves no static assets' -ForEach 'api', 'worker' {
        (Get-DockerfileText -Name $_) | Should -Not -Match 'app\.css'
    }
}

Describe '.dockerignore' {
    It 'keeps secrets, git history and compiled CSS out of the build context' {
        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.dockerignore')
        $lines | Should -Contain '.git/'
        $lines | Should -Contain '**/.env'
        $lines | Should -Contain '**/wwwroot/css/app.css'
        $lines | Should -Contain '**/bin/'
        $lines | Should -Contain '**/obj/'
    }
}
