BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    function Get-RepoText {
        param([string]$RelativePath)
        return Get-Content -LiteralPath (Join-Path $script:RepoRoot $RelativePath) -Raw
    }
}

Describe 'open source files' {
    It 'LICENSE is the MIT license for Syntax Circus' {
        $text = Get-RepoText 'LICENSE'
        $text | Should -Match '^MIT License'
        $text | Should -Match 'Copyright \(c\) 2026 Syntax Circus'
    }

    It 'SECURITY.md uses GitHub private vulnerability reporting, with supported versions and scope' {
        $text = Get-RepoText 'SECURITY.md'
        $text | Should -Match 'Report a vulnerability'
        $text | Should -Match '## Supported versions'
        $text | Should -Match '## Scope'
        $text | Should -Not -Match '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}'
    }

    It 'CONTRIBUTING.md covers Conventional Commits, test-first and EF-tool-only migrations' {
        $text = Get-RepoText 'CONTRIBUTING.md'
        $text | Should -Match 'Conventional Commits'
        $text | Should -Match 'Test first'
        $text | Should -Match 'dotnet ef migrations add'
        $text | Should -Match 'Never write or edit a migration by hand'
    }
}

Describe 'README.md' {
    It 'shows the logo at the top, centered, 200 pixels wide' {
        $text = Get-RepoText 'README.md'
        $text | Should -Match '(?s)^<p align="center">\s*<img src="assets/brand/logo-512\.png"[^>]*width="200"'
    }

    It 'keeps the original sections and adds the compose quick start' {
        $text = Get-RepoText 'README.md'
        foreach ($heading in '## What it is', '## Tech stack', '## Documentation', '## License', '## Quick start') {
            $text | Should -Match ([regex]::Escape($heading))
        }
        $text | Should -Match 'docker compose up -d --build'
        $text | Should -Match 'docs/architecture/00-DISCOVERY-INDEX\.md'
    }

    It 'links only to files that exist' {
        $text = Get-RepoText 'README.md'
        $links = [regex]::Matches($text, '\]\((?<path>(?!https?:|#)[^)\s]+)\)') | ForEach-Object { $_.Groups['path'].Value }
        foreach ($link in $links) {
            Test-Path -LiteralPath (Join-Path $script:RepoRoot $link) | Should -BeTrue -Because "README links to $link"
        }
    }
}

Describe 'D-043 (scoped configuration and one image-only deploy compose)' {
    It 'is in the decision log with its date, its status and an index row' {
        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $log | Should -Match '(?m)^## D-043: Scoped per-project configuration and one image-only deployment compose'
        $log | Should -Match '(?s)## D-043:.*?- \*\*Status:\*\* Approved \(owner 2026-10-05.*?- \*\*Date:\*\* 2026-10-05'
        $log | Should -Match '(?m)^\| D-043 \|.*\| 2026-10-05 \|'
    }

    It 'replaces the old deployment files in the architecture, the roadmap and PHASE-12' {
        $architecture = Get-RepoText 'docs/architecture/02-ARCHITECTURE.md'
        $architecture | Should -Match 'deploy/docker-compose\.yml'
        $phase12 = Get-RepoText 'docs/architecture/PHASE-12-release-hardening.md'
        $phase12 | Should -Match '\*\*P12-T14\*\*.*deploy/docker-compose\.yml'
        foreach ($text in $architecture, $phase12) {
            $text | Should -Not -Match 'docker-compose\.(uat|production)\.yml'
            $text | Should -Not -Match '(?<!deploy/)\.env\.production\.example'
        }
        $roadmap = Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md'
        $roadmap | Should -Match '~~Warn in `\.env\.production\.example` that the Postgres password must be connection-string safe~~'
        $roadmap | Should -Match '\| 8 \|.*deploy/\.env\.<env>\.local'
    }
}

Describe 'D-044 (the knowledge base)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-044: PHASE-08: the knowledge base'
        $script:Log | Should -Match '(?s)## D-044:.*?- \*\*Status:\*\* Approved \(owner 2026-10-05.*?- \*\*Date:\*\* 2026-10-05'
        $script:Log | Should -Match '(?m)^\| D-044 \|.*\| 2026-10-05 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-05, PHASE-08 planning\):\*\* D-044'
    }

    It 'records the owner decisions and the technical decisions the API tasks rely on' {
        foreach ($phrase in 'Slug uniqueness is blocked across scopes', 'TECHSTRAP_API_PUBLIC_URL', 'No audit', 'kb-publish-incomplete', 'IKbContentRenderer', 'api/public/kb/{productKey}/search', 'No render cache') {
            $script:Log | Should -Match ([regex]::Escape($phrase))
        }
    }

    It 'lists the as-built KB routes in PHASE-08 and the architecture, and not the old public routes' {
        foreach ($name in 'docs/architecture/PHASE-08-knowledge-base.md', 'docs/architecture/02-ARCHITECTURE.md') {
            $text = Get-RepoText $name
            foreach ($route in '/api/public/kb/{productKey}/search', '/api/public/kb/{productKey}/categories', '/api/public/kb/{productKey}/articles/{categorySlug}/{slug}', '/api/public/kb/{productKey}/sitemap', '/api/kb/articles/{id}/publish') {
                $text | Should -Match ([regex]::Escape($route)) -Because "$name lists $route"
            }
            $text | Should -Not -Match ([regex]::Escape('/api/public/sitemap')) -Because "$name must not list the old sitemap route"
            $text | Should -Not -Match ([regex]::Escape('/api/public/kb/search?product=')) -Because "$name must not list the old search route"
        }
    }

    It 'ticks every PHASE-08 task P08-T01 to P08-T20 and the Admin deliverable, and the roadmap row says the phase is complete, pending merge' {
        $phase = Get-RepoText 'docs/architecture/PHASE-08-knowledge-base.md'
        foreach ($number in 1..20) {
            $id = 'P08-T{0:00}' -f $number
            $phase | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done"
        }
        $phase | Should -Match '(?m)^- \[x\] Admin KB list, editor with live preview and image upload, categories page, article picker in the reply composer\.'
        $phase | Should -Match '(?m)^- \[x\] Reply-article linking validated and surfaced in the timeline\.'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 08 \|.*D-044.*\| PHASE-08 complete \(pending merge\)'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 08 \|.*\| PHASE-08 complete \(pending merge\)'
        (Get-RepoText 'docs/development/ADMIN-APP.md') | Should -Match '(?m)^## Knowledge base \(08\)'
    }

    It 'describes the dev knowledge base seed and the reply link rule' {
        (Get-RepoText 'docs/development/DEV-DATA.md') | Should -Match 'using-dark-mode'
        $operations = Get-RepoText 'docs/development/TICKET-OPERATIONS.md'
        $operations | Should -Match 'kb-article-not-linkable'
        $operations | Should -Match ([regex]::Escape('/p/{product key}/kb/{category slug}/{article slug}'))
    }

    It 'tells the operator about the Api public URL and the /kb-images/ proxy route' {
        $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
        $runbook | Should -Match 'TECHSTRAP_API_PUBLIC_URL'
        $runbook | Should -Match ([regex]::Escape('/kb-images/'))
    }

    It 'documents the category description and version in the schema doc' {
        $schema = Get-RepoText 'docs/architecture/05-SCHEMA.md'
        $schema | Should -Match '(?s)kb_categories \{.*?text description "nullable".*?xid xmin "concurrency token".*?\}'
        $schema | Should -Match '\| `AddKbCategoryVersionAndDescription` \|'
    }
}

Describe 'the deployment runbook' {
    BeforeAll { $script:Runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md' }

    It 'gives the config, pull, up and ps commands for the deploy compose with an inputs file' {
        foreach ($verb in 'config --quiet', 'pull', 'up -d --wait', 'ps') {
            $script:Runbook | Should -Match ([regex]::Escape("sudo docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml $verb"))
        }
    }

    It 'covers the shared Postgres network, the 0600 env directory, the registry login, health checks, rollback by image tag and migrations' {
        foreach ($phrase in 'docker network create techstrap-db', 'install -d -m 0700 /etc/techstrap/uat', 'docker login ghcr.io', '/health/ready', 'set the previous tags', 'DATABASE__MIGRATEONSTARTUP', 'openssl rand -hex 24') {
            $script:Runbook | Should -Match ([regex]::Escape($phrase))
        }
    }

    It 'requires a different project name per environment and explains the first-deploy network check' {
        foreach ($phrase in 'TECHSTRAP_PROJECT', 'techstrap-uat', 'gw_priority', 'docker inspect', 'restore from backup', 'Compose 2.33.1 or later and Docker Engine 28 or later', 'v5.5.1', 'docker version --format', 'docker network connect techstrap-db', '/proc/net/route', '/proc/net/tcp', '011F10AC', 'sudo docker volume ls --filter name=', 'runs as root') {
            $script:Runbook | Should -Match ([regex]::Escape($phrase))
        }
    }

    It 'never tells the operator to remove volumes and is linked from the README' {
        $script:Runbook | Should -Not -Match 'down -v(\s|$)'
        (Get-RepoText 'README.md') | Should -Match ([regex]::Escape('(docs/self-hosting/DEPLOYMENT.md)'))
    }

    It 'links only to files that exist' {
        $links = [regex]::Matches($script:Runbook, '\]\((?<path>(?!https?:|#)[^)\s]+)\)') | ForEach-Object { $_.Groups['path'].Value }
        $links.Count | Should -BeGreaterThan 1
        foreach ($link in $links) {
            Test-Path -LiteralPath (Join-Path $script:RepoRoot 'docs' 'self-hosting' $link) | Should -BeTrue -Because "DEPLOYMENT.md links to $link"
        }
    }
}
