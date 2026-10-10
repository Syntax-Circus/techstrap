BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    function Get-RepoText { param([string]$RelativePath) Get-Content -LiteralPath (Join-Path $script:RepoRoot $RelativePath) -Raw }

    # GitHub-style slugs of a file's headings: lower case, drop everything but letters, digits, space and hyphen, spaces to hyphens.
    # Lines inside ``` fences (shell comments such as "# comment") are not headings and are skipped.
    function Get-HeadingSlugs {
        param([string]$Path)
        $inFence = $false
        foreach ($line in (Get-Content -LiteralPath $Path)) {
            if ($line -match '^\s*```') { $inFence = -not $inFence; continue }
            if ($inFence) { continue }
            if ($line -match '^#{1,6}\s+(?<t>.+?)\s*$') { (($Matches['t'].ToLowerInvariant() -replace '[^a-z0-9 \-]', '') -replace ' ', '-') }
        }
    }

    # Copies of the ConfigContract.Tests.ps1 helpers (same regexes; those live inside its BeforeAll and cannot be dot-sourced).
    function ConvertTo-IndexlessKey { param([string]$Key) return ($Key -replace '__\d+(?=__|$)', '__0') }
    function Get-EnvEntries {
        param([string]$Path)
        $entries = @()
        foreach ($line in (Get-Content -LiteralPath $Path)) {
            if ($line -match '^(?<hash>#\s?)?(?<key>[A-Za-z][A-Za-z0-9_]*)=(?<value>.*)$') {
                $entries += [pscustomobject]@{ Key = $Matches['key'].ToUpperInvariant(); Value = $Matches['value'].Trim(); Commented = [bool]$Matches['hash'] }
            }
        }
        return $entries
    }
    function Get-EnvKeys { param([string]$Path) return @(Get-EnvEntries -Path $Path | ForEach-Object { ConvertTo-IndexlessKey $_.Key } | Sort-Object -Unique) }

    # Rows of the table under a "### <heading>" in SELF-HOSTING.md: Key (indexless, upper case) and Required (yes or no).
    function Get-TableRows {
        param([string]$Document, [string]$Heading)
        $section = [regex]::Match($Document, '(?ms)^### ' + [regex]::Escape($Heading) + '\s*$(.*?)(?=^### |^## |\z)').Groups[1].Value
        $rows = foreach ($line in ($section -split "`n")) {
            if ($line -match '^\|\s*`(?<key>[A-Za-z][A-Za-z0-9_]*)`\s*\|\s*(?<req>yes|no)\s*\|') {
                [pscustomobject]@{ Key = (ConvertTo-IndexlessKey $Matches['key'].ToUpperInvariant()); Required = $Matches['req'] }
            }
        }
        return @($rows)
    }

    $script:Guide = Get-RepoText 'docs/self-hosting/SELF-HOSTING.md'
    $script:Apps = @(
        @{ Heading = '.env.api'; Example = 'deploy/.env.api.example' },
        @{ Heading = '.env.worker'; Example = 'deploy/.env.worker.example' },
        @{ Heading = '.env.admin'; Example = 'deploy/.env.admin.example' },
        @{ Heading = '.env.portal'; Example = 'deploy/.env.portal.example' }
    )
    # The operator-required set mirrors ProductionBlankTemplateTests.cs (tests/TechStrap.Api.Tests, the required-key lists near the top of the file). Keep in step with that C# test.
    $script:Required = @{
        '.env.api'    = @('CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'AUTHENTICATION__JWTBEARER__AUDIENCES__0', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_API_PUBLIC_URL')
        '.env.worker' = @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__DEFAULTFROM')
        '.env.admin'  = @('AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET')
        '.env.portal' = @('TECHSTRAP_PORTAL_PUBLIC_URL')
    }
    $script:ComposeHeading = 'Compose inputs (deploy/.env.uat.example and deploy/.env.production.example)'
}

Describe 'SELF-HOSTING.md environment reference (PHASE-12b)' {
    It 'lists every key of every deploy template' {
        foreach ($app in $script:Apps) {
            $documented = (Get-TableRows -Document $script:Guide -Heading $app.Heading).Key
            foreach ($key in (Get-EnvKeys -Path (Join-Path $script:RepoRoot $app.Example))) {
                $documented | Should -Contain $key -Because "$($app.Example) has $key but the $($app.Heading) table does not"
            }
        }
    }

    It 'lists no key that is not in a template' {
        foreach ($app in $script:Apps) {
            $templateKeys = Get-EnvKeys -Path (Join-Path $script:RepoRoot $app.Example)
            foreach ($key in (Get-TableRows -Document $script:Guide -Heading $app.Heading).Key) {
                $templateKeys | Should -Contain $key -Because "the $($app.Heading) table lists $key but $($app.Example) does not"
            }
        }
    }

    It 'lists each key once per table' {
        foreach ($app in $script:Apps) {
            $keys = (Get-TableRows -Document $script:Guide -Heading $app.Heading).Key
            @($keys | Group-Object | Where-Object Count -gt 1) | Should -BeNullOrEmpty -Because $app.Heading
        }
    }

    It 'marks exactly the operator-required keys as required' {
        foreach ($app in $script:Apps) {
            $rows = Get-TableRows -Document $script:Guide -Heading $app.Heading
            $required = @($rows | Where-Object Required -eq 'yes' | ForEach-Object Key | Sort-Object)
            $required | Should -Be @($script:Required[$app.Heading] | Sort-Object) -Because "required set of $($app.Heading)"
        }
    }

    It 'documents the compose inputs of both environment templates' {
        $uat = Get-EnvKeys -Path (Join-Path $script:RepoRoot 'deploy/.env.uat.example')
        $production = Get-EnvKeys -Path (Join-Path $script:RepoRoot 'deploy/.env.production.example')
        $uat | Should -Be $production -Because 'the two compose-input templates carry the same keys'
        $documented = (Get-TableRows -Document $script:Guide -Heading $script:ComposeHeading).Key
        foreach ($key in $uat) { $documented | Should -Contain $key }
        foreach ($key in $documented) { $uat | Should -Contain $key }
    }

    It 'marks every key that deploy/docker-compose.yml requires with ${NAME:? as required' {
        $compose = Get-RepoText 'deploy/docker-compose.yml'
        $rows = Get-TableRows -Document $script:Guide -Heading $script:ComposeHeading
        $names = @([regex]::Matches($compose, '\$\{(?<name>[A-Z][A-Z0-9_]*):\?') | ForEach-Object { $_.Groups['name'].Value } | Sort-Object -Unique)
        $names.Count | Should -BeGreaterThan 5
        foreach ($name in $names) {
            @($rows | Where-Object Key -eq $name | ForEach-Object Required) | Should -Be @('yes') -Because "compose requires $name"
        }
    }
}

Describe 'SELF-HOSTING.md content (PHASE-12b)' {
    It 'has the section headings' {
        foreach ($heading in 'Overview and requirements', 'Compose layout', 'OIDC requirements', 'Environment reference', 'Reverse proxy', 'TLS', 'SMTP', 'Volumes', 'First administrator', 'Upgrade and rollback', 'Health checks', 'PgBouncer and LISTEN', 'Backups', 'Other identity providers') {
            $script:Guide | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because $heading
        }
        foreach ($heading in 'Default-site Caddy block', 'Request body size', 'Forwarded headers and the pinned subnet', 'Knowledge-base images') {
            $script:Guide | Should -Match ('(?m)^### ' + [regex]::Escape($heading) + '\s*$') -Because $heading
        }
    }

    It 'states the OIDC contract' {
        foreach ($phrase in 'offline_access', '/signin-oidc', '/signout-callback-oidc', 'PKCE', 'confidential', 'TECHSTRAP_AGENT_GROUP', 'TECHSTRAP_ADMIN_GROUP') {
            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
        # The claim names must be stated in the bullets of the OIDC requirements section, not just appear anywhere.
        $oidc = [regex]::Match($script:Guide, '(?ms)^## OIDC requirements\s*$(.*?)(?=^## |\z)').Groups[1].Value
        $oidc | Should -Match '(?m)^- \*\*Claims\.\*\*.*`sub`.*`email`.*`groups`'
        $oidc | Should -Match '(?m)^- \*\*Api token validation\.\*\*.*audience'
        $oidc | Should -Match '(?m)^- \*\*Email\.\*\*.*`agent-email-required`'
    }

    It 'gives a default-site Caddy block with the body limit, the kb-images and product-logos routes and the forwarded headers' {
        foreach ($phrase in 'request_body', 'max_size 26MiB', 'reverse_proxy 127.0.0.1:8080', 'reverse_proxy 127.0.0.1:8081', 'reverse_proxy 127.0.0.1:8082', '/kb-images/', '/product-logos/', 'REVERSE_PROXY_CIDR', 'TECHSTRAP_SUBNET', 'X-Forwarded-For') {
            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
    }

    It 'links the runbook, the Authentik example and the deployment guide, and every link resolves' {
        foreach ($link in '../runbooks/backup-restore.md', 'AUTHENTIK.md', 'DEPLOYMENT.md') { $script:Guide | Should -Match ([regex]::Escape("]($link")) -Because $link }
        foreach ($m in [regex]::Matches($script:Guide, '\]\((?<path>(?!https?:)[^)\s#]*)(?:#(?<frag>[^)\s]+))?\)')) {
            $path = $m.Groups['path'].Value
            $frag = $m.Groups['frag'].Value
            $target = if ($path) { Join-Path $script:RepoRoot 'docs' 'self-hosting' $path } else { Join-Path $script:RepoRoot 'docs' 'self-hosting' 'SELF-HOSTING.md' }
            Test-Path -LiteralPath $target | Should -BeTrue -Because $path
            if ($frag) {
                $slugs = @(Get-HeadingSlugs -Path $target)
                $slugs | Should -Contain $frag -Because "anchor #$frag in $path"
            }
        }
    }

    It 'does not tell the reader to install 1.0.0 or an rc and is ASCII' {
        $script:Guide | Should -Not -Match '1\.0\.0'
        $script:Guide | Should -Not -Match '(?i)-rc\.?\d'
        $script:Guide | Should -Not -Match 'down\s+-v(\s|$)'
        ([regex]::IsMatch($script:Guide, '[^\x00-\x7F]')) | Should -BeFalse
    }
}

Describe 'deploy templates and README after 12b' {
    It 'the two compose-input templates pin 0.2.0 images, no rc and no 1.0.0' {
        foreach ($file in 'deploy/.env.uat.example', 'deploy/.env.production.example') {
            $text = Get-RepoText $file
            $text | Should -Not -Match '-rc\.?\d' -Because $file
            $text | Should -Not -Match '1\.0\.0' -Because $file
            foreach ($app in 'api', 'worker', 'admin', 'portal') { $text | Should -Match ("(?m)^TECHSTRAP_$($app.ToUpper())_IMAGE=ghcr\.io/syntax-circus/techstrap-${app}:0\.2\.0\s*$") -Because "$file $app" }
        }
    }

    It 'DEPLOYMENT.md names the portal required key and links the new guides' {
        $text = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
        $text | Should -Not -Match '\| none yet \|'
        $text | Should -Match '`\.env\.portal` \| `TECHSTRAP_PORTAL_PUBLIC_URL`'
        $text | Should -Match ([regex]::Escape('](SELF-HOSTING.md)'))
        $text | Should -Match ([regex]::Escape('](../runbooks/backup-restore.md)'))
    }

    It 'README says v0.3.0 next and links the self-hosting guide' {
        $text = Get-RepoText 'README.md'
        $text | Should -Not -Match 'then `?v1\.0\.0`?'
        $text | Should -Match ([regex]::Escape('then v0.3.0 (1.0.0 is a later API-lock decision)'))
        $text | Should -Match ([regex]::Escape('(docs/self-hosting/SELF-HOSTING.md)'))
    }
}

Describe 'Authentik guide (PHASE-12b)' {
    BeforeAll {
        $script:Authentik = Get-RepoText 'docs/self-hosting/AUTHENTIK.md'
        $script:AgentAuth = Get-RepoText 'docs/self-hosting/AGENT-AUTHENTICATION.md'
        $script:AdminApp = Get-RepoText 'docs/development/ADMIN-APP.md'
    }

    It 'has the sections' {
        foreach ($heading in 'Overview', 'Prerequisites', 'Create the groups', 'Create the provider', 'Create the application', 'The groups claim', 'Where each value goes', 'Verification', 'Troubleshooting', 'Reference') {
            $script:Authentik | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because $heading
        }
    }

    It 'names the groups, the redirect paths, the scope and PKCE' {
        foreach ($phrase in 'techstrap-agents', 'techstrap-admins', '/signin-oidc', '/signout-callback-oidc', 'offline_access', 'PKCE', 'Confidential', '/application/o/techstrap/', 'TECHSTRAP_AGENT_GROUP', 'TECHSTRAP_ADMIN_GROUP', 'AUTH__CLIENTID', 'AUTHENTICATION__JWTBEARER__AUDIENCES__0', 'syntax-circus-authentik') {
            $script:Authentik | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
    }

    It 'says the default profile scope mapping carries groups and gives the explicit-mapping fallback' {
        $script:Authentik | Should -Match '(?is)default.{0,80}profile.{0,200}groups'
        $script:Authentik | Should -Match '(?i)fallback'
    }

    It 'the groups fallback uses the requested profile scope name, not a groups scope the Admin never asks for' {
        $section = ($script:Authentik -split '(?m)^## The groups claim\s*$')[1]
        $section = ($section -split '(?m)^## ')[0]
        $fallback = ($section -split '(?m)^Fallback')[1]
        $fallback | Should -Match 'scope name `profile`'
        $fallback | Should -Match 'AUTH__SCOPES__0=groups'
        $section | Should -Not -Match '(?i)scope name `groups`'
    }

    It 'AUTHENTIK.md and AGENT-AUTHENTICATION.md links resolve and anchors match a heading' {
        foreach ($name in 'AUTHENTIK.md', 'AGENT-AUTHENTICATION.md') {
            $source = Get-RepoText "docs/self-hosting/$name"
            foreach ($m in [regex]::Matches($source, '\]\((?<path>(?!https?:)[^)\s#]*)(?:#(?<frag>[^)\s]+))?\)')) {
                $path = $m.Groups['path'].Value
                $frag = $m.Groups['frag'].Value
                $target = if ($path) { Join-Path $script:RepoRoot 'docs' 'self-hosting' $path } else { Join-Path $script:RepoRoot 'docs' 'self-hosting' $name }
                Test-Path -LiteralPath $target | Should -BeTrue -Because "$name links to $path"
                if ($frag) {
                    $slugs = @(Get-HeadingSlugs -Path $target)
                    $slugs | Should -Contain $frag -Because "anchor #$frag in $path (from $name)"
                }
            }
        }
    }

    It 'verifies the three outcomes: a member signs in, a non-member is refused, the token carries groups and email' {
        $section = ($script:Authentik -split '(?m)^## Verification\s*$')[1]
        $section | Should -Match '(?i)member'
        $section | Should -Match '(?i)not a member|non-member|refused'
        $section | Should -Match 'groups'
        $section | Should -Match 'email'
    }

    It 'AUTHENTIK.md contains no client secret value' {
        $script:Authentik | Should -Not -Match '(?i)client_secret\s*[=:]\s*\S'
        $script:Authentik | Should -Not -Match 'AUTH__CLIENTSECRET=(?!<|\s*$)'
        $script:Authentik | Should -Not -Match '[A-Za-z0-9+/_-]{40,}'
        $script:Authentik | Should -Not -Match '(?i)(secret|password)\s*[=:]\s*[A-Za-z0-9]{10,}'
    }

    It 'does not link the private provisioning repository' {
        $script:Authentik | Should -Not -Match 'dev\.azure\.com'
        $script:Authentik | Should -Not -Match '\]\(https?://[^)]*syntax-circus-authentik'
    }

    It 'AGENT-AUTHENTICATION.md points at the guide and states the claim requirements' {
        $script:AgentAuth | Should -Match ([regex]::Escape('](AUTHENTIK.md)'))
        $script:AgentAuth | Should -Match ([regex]::Escape('](SELF-HOSTING.md)'))
        foreach ($phrase in 'sub', 'email', 'groups', 'TECHSTRAP_GROUP_CLAIM_TYPE') { $script:AgentAuth | Should -Match ([regex]::Escape($phrase)) }
        $script:AgentAuth | Should -Not -Match '(?i)add a scope mapping'
    }

    It 'ADMIN-APP.md points to the guide and does not claim the provider flow is verified yet' {
        $script:AdminApp | Should -Match ([regex]::Escape('](../self-hosting/AUTHENTIK.md)'))
        $script:AdminApp | Should -Match '(?i)verified against a live Authentik in 12c'
    }

    It 'no self-hosting doc uses the techstrap-admin issuer slug' {
        $files = @(Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'docs/self-hosting') -Filter *.md) + (Get-Item (Join-Path $script:RepoRoot 'docs/development/ADMIN-APP.md'))
        foreach ($file in $files) {
            $text = [System.IO.File]::ReadAllText($file.FullName)
            $text | Should -Not -Match 'techstrap-admin/' -Because "$($file.Name) must use the techstrap slug"
            $text | Should -Not -Match 'application/o/techstrap-admin' -Because $file.Name
        }
    }

    It 'is ASCII only' { ([regex]::IsMatch($script:Authentik, '[^\x00-\x7F]')) | Should -BeFalse }
}

Describe 'self-hosting wording that depends on the 12c live Authentik check' {
    It 'calls Authentik a worked example verified in 12c, not a tested one' {
        $guide = Get-RepoText 'docs/self-hosting/SELF-HOSTING.md'
        $guide | Should -Match ([regex]::Escape('worked example (verified against a live Authentik in 12c)'))
        $guide | Should -Not -Match 'Authentik is the tested example'
    }

    It 'tells the reader to add the post-logout URI as a strict Redirect URIs entry' {
        $authentik = Get-RepoText 'docs/self-hosting/AUTHENTIK.md'
        $authentik | Should -Match ([regex]::Escape('add both as strict entries in Redirect URIs (older versions show a separate post-logout field)'))
    }
}

Describe 'heading slug scan' {
    It 'skips lines inside code fences' {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("slugs-{0}.md" -f [guid]::NewGuid().ToString('N'))
        try {
            $fence = ([string][char]96) * 3
            [System.IO.File]::WriteAllText($path, ("# Real heading", "${fence}bash", "# fenced comment", $fence, "## Second heading", "") -join "`n")
            $slugs = @(Get-HeadingSlugs -Path $path)
            $slugs | Should -Contain 'real-heading'
            $slugs | Should -Contain 'second-heading'
            $slugs | Should -Not -Contain 'fenced-comment'
        } finally {
            Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
        }
    }
}
