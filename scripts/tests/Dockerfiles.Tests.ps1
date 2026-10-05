BeforeDiscovery {
    $script:Hosts = @('api', 'admin', 'portal', 'worker')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    function Get-DockerfileText {
        param([string]$Name)
        return Get-Content -LiteralPath (Join-Path $script:RepoRoot "Dockerfile.$Name") -Raw
    }

    # Every TechStrap project the host needs to build: itself plus its ProjectReference closure (Admin: Contracts and Hosting).
    function Get-ProjectReferenceClosure {
        param([string]$Project)
        $seen = [System.Collections.Generic.HashSet[string]]::new()
        $queue = [System.Collections.Generic.Queue[string]]::new()
        $queue.Enqueue($Project)
        while ($queue.Count -gt 0) {
            $name = $queue.Dequeue()
            if (-not $seen.Add($name)) { continue }
            $text = Get-Content -LiteralPath (Join-Path $script:RepoRoot "src/$name/$name.csproj") -Raw
            foreach ($match in [regex]::Matches($text, '<ProjectReference Include="\.\./(?<name>[^/]+)/')) {
                $queue.Enqueue($match.Groups['name'].Value)
            }
        }
        return @($seen | Sort-Object)
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

Describe 'self-hosted font assertion' {
    # Fonts are restored by libman (jsdelivr) during publish and never committed, so an image built without network
    # access must fail the build instead of shipping without fonts. Admin: Sans 3 + Mono 3 + Serif 1 files, 3 licences.
    It 'Dockerfile.<Name> fails the build unless <Fonts> WOFF2 files and <Licences> OFL licences are published' -ForEach @(
        @{ Name = 'admin'; Fonts = 7; Licences = 3 }
        @{ Name = 'portal'; Fonts = 6; Licences = 2 }
    ) {
        $text = Get-DockerfileText -Name $Name
        $fontCheck = 'test "$(find /app/publish/wwwroot/fonts -name ''*.woff2'' | wc -l)" -eq ' + $Fonts
        $licenceCheck = 'test "$(find /app/publish/wwwroot/fonts -name LICENSE | wc -l)" -eq ' + $Licences
        $text.Contains($fontCheck) | Should -BeTrue -Because "Dockerfile.$Name must contain: $fontCheck"
        $text.Contains($licenceCheck) | Should -BeTrue -Because "Dockerfile.$Name must contain: $licenceCheck"
    }

    It 'Dockerfile.api and Dockerfile.worker have no font assertion because they serve no static assets' -ForEach 'api', 'worker' {
        (Get-DockerfileText -Name $_) | Should -Not -Match 'wwwroot/fonts'
    }
}

Describe '.dockerignore' {
    It 'keeps secrets, git history and compiled CSS out of the build context' {
        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.dockerignore')
        $lines | Should -Contain '.git/'
        $lines | Should -Contain '**/.env'
        $lines | Should -Contain '**/wwwroot/css/app.css'
        $lines | Should -Contain '**/wwwroot/fonts/'
        $lines | Should -Contain '**/Styles/Vendor/'
        $lines | Should -Contain '**/bin/'
        $lines | Should -Contain '**/obj/'
    }
}

Describe '.dockerignore and the shared brand SCSS' {
    It 'excludes assets/ but re-includes assets/brand/scss/, which Admin and Portal import at build' {
        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.dockerignore')
        $lines | Should -Contain 'assets/'
        $lines | Should -Contain '!assets/brand/scss/'
        [array]::IndexOf($lines, '!assets/brand/scss/') | Should -BeGreaterThan ([array]::IndexOf($lines, 'assets/'))
    }
}

Describe 'font endpoints manifest assertion' {
    # MapStaticAssets serves only what the published endpoints manifest lists. Fonts restored by libman after static web
    # asset discovery sit under wwwroot/fonts in the image yet answer 404, so the build must prove the manifest maps them.
    It 'Dockerfile.<Name> fails the build unless the published endpoints manifest maps a .woff2 file' -ForEach @(
        @{ Name = 'admin'; Project = 'TechStrap.Admin' }
        @{ Name = 'portal'; Project = 'TechStrap.Portal' }
    ) {
        $text = Get-DockerfileText -Name $Name
        $text | Should -Match "RUN grep -q '\\.woff2' /app/publish/$([regex]::Escape($Project))\.staticwebassets\.endpoints\.json \\r?\n\s+\|\| "
    }
}

Describe 'clean publish copy list' {
    It '<Project> copies itself and every project it references, and no other TechStrap project' -ForEach @(
        @{ Project = 'TechStrap.Admin'; Expected = @('TechStrap.Admin', 'TechStrap.Contracts', 'TechStrap.Hosting') }
        @{ Project = 'TechStrap.Portal'; Expected = @('TechStrap.Contracts', 'TechStrap.Hosting', 'TechStrap.Portal') }
    ) {
        Get-ProjectReferenceClosure -Project $Project | Should -Be $Expected
    }
}

Describe 'clean publish serves the self-hosted fonts' -Tag 'Network' {
    # Reproduces the image build in a throwaway copy of the tree (no bin, obj or restored fonts), so libman restores the
    # fonts during publish itself and nothing under the real src/ is touched. Tagged Network because libman downloads
    # from jsdelivr, like the Docker build; exclude with -ExcludeTagFilter Network when offline.
    It '<Project> publishes an endpoints manifest that maps the restored .woff2 files' -ForEach @(
        @{ Project = 'TechStrap.Admin' }
        @{ Project = 'TechStrap.Portal' }
    ) {
        $copy = Join-Path ([IO.Path]::GetTempPath()) "techstrap-publish-$([guid]::NewGuid().ToString('N'))"
        try {
            $null = New-Item -ItemType Directory -Path $copy -Force
            foreach ($file in 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'GitVersion.yml', 'NuGet.config', 'nuget.config') {
                $source = Join-Path $script:RepoRoot $file
                if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $copy $file) -Force }
            }
            $projectDirs = Get-ProjectReferenceClosure -Project $Project | ForEach-Object { "src/$_" }
            foreach ($dir in @('.config', 'eng', 'assets/brand/scss') + $projectDirs) {
                $source = Join-Path $script:RepoRoot $dir
                if (-not (Test-Path -LiteralPath $source)) { continue }
                $target = Join-Path $copy $dir
                $null = New-Item -ItemType Directory -Path (Split-Path $target) -Force
                Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
            }
            foreach ($name in 'wwwroot/fonts', 'bin', 'obj') {
                foreach ($dir in $projectDirs) {
                    Remove-Item -LiteralPath (Join-Path $copy $dir $name) -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
            $output = Join-Path $copy 'out'
            $log = & dotnet publish (Join-Path $copy 'src' $Project "$Project.csproj") -c Release -o $output -p:DisableGitVersionTask=true 2>&1
            $LASTEXITCODE | Should -Be 0 -Because ($log -join [Environment]::NewLine)
            $manifest = Get-Content -LiteralPath (Join-Path $output "$Project.staticwebassets.endpoints.json") -Raw
            $manifest | Should -Match '\.woff2' -Because 'MapStaticAssets serves only what this manifest lists'
        }
        finally {
            Remove-Item -LiteralPath $copy -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
