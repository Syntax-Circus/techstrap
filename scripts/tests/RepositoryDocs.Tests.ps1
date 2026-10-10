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

    It 'states the current status: PHASE-12 is next and the SDK is published as 0.1.0' {
        $text = Get-RepoText 'README.md'
        $status = ($text -split '\r?\n' | Where-Object { $_ -like '> **Status:**' + '*' }) -join ' '
        $status | Should -Match 'PHASE-01 to PHASE-11'
        $status | Should -Match 'PHASE-12'
        $status | Should -Match '0\.2\.0'
        $text | Should -Not -Match 'will be published'
        foreach ($doc in 'CLIENT-SDK', 'INTAKE', 'DEPLOYMENT', 'RELEASING', 'TICKET-OPERATIONS', 'DEV-DATA', 'AGENT-AUTHENTICATION') {
            $text | Should -Match ([regex]::Escape($doc + '.md'))
        }
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

    It 'ticks every PHASE-08 task P08-T01 to P08-T20 and the Admin deliverable, and the roadmap row says the phase is merged' {
        $phase = Get-RepoText 'docs/architecture/PHASE-08-knowledge-base.md'
        foreach ($number in 1..20) {
            $id = 'P08-T{0:00}' -f $number
            $phase | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done"
        }
        $phase | Should -Match '(?m)^- \[x\] Admin KB list, editor with live preview and image upload, categories page, article picker in the reply composer\.'
        $phase | Should -Match '(?m)^- \[x\] Reply-article linking validated and surfaced in the timeline\.'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 08 \|.*D-044.*\| PHASE-08 merged \(PR #13\)'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 08 \|.*\| PHASE-08 merged \(PR #13\)'
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

Describe 'D-045 (the public portal)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-045: PHASE-09: the public portal'
        $script:Log | Should -Match '(?s)## D-045:.*?- \*\*Status:\*\* Approved \(owner 2026-10-05.*?- \*\*Date:\*\* 2026-10-05'
        $script:Log | Should -Match '(?m)^\| D-045 \|.*\| 2026-10-05 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-05, PHASE-09 planning\):\*\* D-045'
    }

    It 'records the owner decisions and the technical rulings the three pull requests rely on' {
        foreach ($phrase in 'Three pull requests', 'vanilla JS', '<ts-kb-suggestions>', 'GET api/public/products', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'ProductKey', 'T20 (Playwright end-to-end) is deferred',
                'ApiConnection', 'X-Ticket-Token', 'YAGNI', 'MapSeoRobotsTxt', 'Seo:BaseUrl', 'per-path rules', 'Referrer-Policy: no-referrer', 'byte-identical', '?ref=', 'ProductThemeViewModel', 'Contracts plus Hosting') {
            $script:Log | Should -Match ([regex]::Escape($phrase)) -Because "D-045 must mention $phrase"
        }
    }

    It 'corrects the PHASE-09 spec and the package map' {
        $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
        $spec | Should -Match '(?m)^### Corrections \(D-045, 2026-10-05\)'
        $spec | Should -Match 'MapSeoRobotsTxt'
        $spec | Should -Match 'ProductThemeViewModel'
        (Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md') | Should -Match 'MapSeoRobotsTxt'
    }

    It 'ticks only the tasks and deliverables 09a to 09d fully deliver, and the roadmap and discovery rows say PHASE-09 is complete and all four pull requests merged' {
        $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
        foreach ($number in 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 19, 21, 22, 23) {
            $id = 'P09-T{0:00}' -f $number
            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a, 09b, 09c or 09d"
        }
        # T16 waits for the owner's evidence (axe, Lighthouse, the JavaScript-off walk, screenshots), T18 for the owner's compose run of the smoke, T20 is deferred.
        foreach ($number in 16, 18, 20) {
            $id = 'P09-T{0:00}' -f $number
            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not closed by 09d"
        }
        $spec | Should -Match 'delivered except double-submit \(deferred to 09d, D-045' -Because 'the 09b history of T09 is kept'
        $spec | Should -Match '\*\*Owner evidence pending:\*\*' -Because 'T16 says what the owner still has to record'
        $spec | Should -Match '(?m)^- \[x\] `TechStrap\.Portal` host with `\.env\.example`, forwarded-headers and client-IP forwarding to the API\.'
        $spec | Should -Match '(?m)^- \[x\] Branded layout with per-product theming and NotFound handling\.'
        $spec | Should -Match '(?m)^- \[x\] Contact page with honeypot, attachments, deflection island, submitted page\.'
        $spec | Should -Match '(?m)^- \[x\] Customer ticket view, reply \(incl\. Closed -> follow-up\), lost-link, attachment pass-through\.'
        $spec | Should -Match '(?m)^- \[x\] Typed clients for public product, public ticket, customer ticket, public KB\.'
        $spec | Should -Match '(?m)^- \[x\] KB home/category/search/article pages'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c merged \(PR #16\); 09d merged \(PR #17\); PHASE-09 complete:'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c merged \(PR #16\); 09d merged \(PR #17\); PHASE-09 complete:'
    }

    It 'has a Portal developer guide, linked from the README, that lists every setting and the known gaps' {
        $guide = Get-RepoText 'docs/development/PORTAL-APP.md'
        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '### Forms and uploads', '### The double-send guard', '### Suggestions beside the subject', '### Form helpers (`portal-forms.js`)', '### Accessibility, layout and the base address', '### The help centre', '### SEO and structured data', '### Caching and the sitemap', '### The ticket page and attachments', '### The lost-link page', '## Where things live', '## Tests', '## Known gaps') {
            $guide | Should -Match ('(?m)^' + [regex]::Escape($heading))
        }
        foreach ($key in 'API__BASEURL', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'TECHSTRAP_PORTAL_SHOW_POWERED_BY', 'CANONICALHOST__CANONICALHOST') {
            $guide | Should -Match ([regex]::Escape($key))
        }
        (Get-RepoText 'README.md') | Should -Match '\[Portal app\]\(docs/development/PORTAL-APP\.md\)'
        $links = [regex]::Matches($guide, '\]\((?!http)(?<link>[^)#]+)') | ForEach-Object { $_.Groups['link'].Value }
        foreach ($link in $links) {
            Test-Path -LiteralPath (Join-Path $script:RepoRoot 'docs' 'development' $link) | Should -BeTrue -Because "PORTAL-APP.md links to $link"
        }
    }
}

Describe 'D-045 addendum (PHASE-09b rulings, 2026-10-06)' {
    BeforeAll {
        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Section = [regex]::Match($script:Log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
    }

    It 'is a dated addendum inside D-045, not a new decision number' {
        $script:Section | Should -Match '(?m)^### Addendum \(2026-10-06, PHASE-09b customer flows\)'
    }

    It 'records each ruling the 09b plan rests on' {
        foreach ($phrase in '/p/{key}/suggest', 'IntakeLimits', 'ProductKey', 'reply-conflict', 'IBrowserFile', 'RequestSizeLimit', 'CreateProtector', 'ToTimeLimitedDataProtector', 'DATAPROTECTION__KEYRINGPATH',
                'honeypot', 'noindex', 'FollowUpViewUrl', 'CustomerMessageBody', 'OpenStreamAsync', 'PortalScripts.Tests.ps1', 'Test-ComposeSmoke.ps1') {
            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the addendum must mention $phrase"
        }
    }

    It 'moves the suggest adapter in the spec and the decision text, so no category slug needs reserving' {
        $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
        $spec | Should -Not -Match 'kb/suggest'
        $spec | Should -Match '(?m)^### Corrections \(D-045 addendum, 2026-10-06\)'
        $script:Log | Should -Not -Match 'A category named `suggest` would be unreachable'
    }

    It 'says in the architecture and PHASE-06 tables that the customer ticket carries the product key' {
        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Match 'CustomerTicketDto` \(public messages only; carries the product key\)'
        (Get-RepoText 'docs/architecture/PHASE-06-ticket-operations.md') | Should -Match 'CustomerTicketDto` \(public messages only; carries the product key\)'
    }
}

Describe 'D-045 addendum (PHASE-09c rulings, 2026-10-06)' {
    BeforeAll {
        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Section = [regex]::Match($script:Log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
    }

    It 'is a dated addendum inside D-045, not a new decision number' {
        $script:Section | Should -Match '(?m)^### Addendum \(2026-10-06, PHASE-09c knowledge base pages, SEO and caching\)'
    }

    It 'records each ruling the 09c plan rests on' {
        foreach ($phrase in 'categories/{categorySlug}/articles', 'PublicKbArticleSummaryDto', 'PublicProductSummaryDto', 'PublicProductLimits.MaxListed', 'PublishedIn', 'kb-category-not-found', 'ListCategoryArticlesAsync',
                'KbArticleBody', 'KbArticleCard', 'KbBreadcrumbs', 'JsonLdText', 'MapSeoSitemap', 'IMemoryCache', 'single-flight', '50,000', 'AddOutputCache', 'PortalCachePaths', 'SetOnSuccess', 'X-Correlation-Id',
                'img-src', 'SetVaryByQuery') {
            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the addendum must mention $phrase"
        }
    }

    It 'lists the two new routes in the architecture tables and the public rate limit row' {
        $architecture = Get-RepoText 'docs/architecture/02-ARCHITECTURE.md'
        $architecture | Should -Match ([regex]::Escape('`GET /api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=`'))
        $architecture | Should -Match ([regex]::Escape('`GET /api/public/products` (anonymous, `public` limit; for the Portal sitemap)'))
        $architecture | Should -Match ([regex]::Escape('`GET /api/public/products`, `GET /api/public/products/{key}`, public KB endpoints, sitemap'))
    }
}

Describe 'D-045 addendum (PHASE-09d rulings, 2026-10-06)' {
    BeforeAll {
        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Section = [regex]::Match($script:Log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
    }

    It 'is a dated addendum inside D-045, not a new decision number' {
        $script:Section | Should -Match '(?m)^### Addendum \(2026-10-06, PHASE-09d portal polish\)'
    }

    It 'records each ruling the 09d plan rests on and the spike findings' {
        foreach ($phrase in 'SubmitId', 'FormGuard', 'SubmitGuard', 'own `MemoryCache`', 'RequestAborted', 'portal-forms.js', 'ts-copy-text', 'ts-char-count', '--ts-reading-width', '--p-error', 'PageLinks.ToFragment',
                'data-enhance-nav', 'BodyHeadings', 'StartupFailure', 'ResponsiveStyleTests', 'HeadingHostTests', 'Seen', 'Spike findings', 'does not run a script that arrives with swapped content', 'Deviations from the brief') {
            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the addendum must mention $phrase"
        }
    }

    It 'words the 09b note about a post to an unknown product as the same request, not a re-execution' {
        $script:Section | Should -Match ([regex]::Escape('renders the not-found page in the same request'))
        $script:Section | Should -Not -Match ([regex]::Escape('the framework re-executes the post'))
    }
}

Describe 'D-045 as built in 09b' {
    BeforeAll {
        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Section = [regex]::Match($log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
        $script:Guide = Get-RepoText 'docs/development/PORTAL-APP.md'
    }

    It 'records what the 09b build found, as consequences of the addendum' {
        foreach ($phrase in 'As built in 09b: a post to an unknown product', 'As built in 09b: the request size limit', 'As built in 09b: the honeypot', 'As built in 09b: the ticket page',
                'As built in 09b: the suggest adapter', 'As built in 09b: the smoke', 'Known in 09b: no copy button') {
            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the as-built list must carry: $phrase"
        }
    }

    It 'has a developer guide that describes the 09b flows, the one markup site and the smoke check' {
        foreach ($phrase in 'ts-kb-suggestions', 'ReceivedReference', 'CustomerMessageBody', 'FollowUpLink', 'AttachmentPassThrough', 'RequestTooLargeMiddleware', 'PortalScripts.Tests.ps1', 'Test-ComposeSmoke.ps1', 'reply-conflict', '/p/{key}/suggest') {
            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because "PORTAL-APP.md must mention $phrase"
        }
        $script:Guide | Should -Not -Match 'answer 404 until 09b'
        $script:Guide | Should -Not -Match '`MarkupString` is used nowhere yet'
    }

    It 'tells the Admin guide that the compose smoke now covers the Portal and the real client address' {
        $admin = Get-RepoText 'docs/development/ADMIN-APP.md'
        $paragraph = $admin -split '(?:\r?\n){2}' | Where-Object { $_ -match '\*\*Compose smoke\.\*\*' } | Select-Object -First 1
        $paragraph | Should -Match 'Portal'
        $paragraph | Should -Match 'real client address'
    }
}

Describe 'D-045 as built in 09d' {
    BeforeAll {
        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Section = [regex]::Match($log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
        $script:Guide = Get-RepoText 'docs/development/PORTAL-APP.md'
        $script:Spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
    }

    It 'records what the 09d build found, as consequences of the addendum' {
        foreach ($phrase in 'As built in 09d: the double-send guard', 'As built in 09d: the form helpers', 'As built in 09d: links to the current page', 'As built in 09d: styles', 'As built in 09d: test hardening',
                'Known in 09d: the guard is per instance', 'Known in 09d: the owner', 'Known in 09d: an `h1` in a body', 'Resolved in 09d') {
            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the as-built list must carry: $phrase"
        }
    }

    It 'has a developer guide that describes the guard, the helpers, the base-address rule and the owner checklist' {
        foreach ($phrase in 'SubmitGuard', 'SubmitIds', 'FormGuard', 'portal-forms.js', 'ts-copy-text', 'ts-char-count', 'PageLinks.ToFragment', 'data-enhance-nav', 'BodyHeadings', 'StartupFailure', 'ResponsiveStyleTests',
                'HeadingHostTests', 'DoubleSendHostTests', '## Manual checks (owner, before merging 09d)', 'Lighthouse', 'axe', 'forced-colors', 'Double click') {
            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because "PORTAL-APP.md must mention $phrase"
        }
        $script:Guide | Should -Not -Match 'merging them is PHASE-09d'
        $script:Guide | Should -Not -Match 'there is no script to disable the button'
    }

    It 'lists the six semantic Portal tokens in BRAND.md' {
        $brand = Get-RepoText 'docs/BRAND.md'
        foreach ($token in '--p-error', '--p-error-bg', '--p-success', '--p-success-bg', '--p-warn', '--p-warn-bg') {
            $brand | Should -Match ([regex]::Escape('`' + $token + '`')) -Because "BRAND.md must define $token"
        }
        $brand | Should -Match ([regex]::Escape('--ts-reading-width'))
    }

    It 'ticks T09 and the success criteria the tests prove, and leaves the owner evidence open' {
        $script:Spec | Should -Match '(?m)^- \[x\] A customer can open'
        $script:Spec | Should -Match '(?m)^- \[ \] Rate limits observe the real client IP through the portal\.'
        $script:Spec | Should -Match '(?m)^- \[x\] `dotnet build`/`dotnet test` green \(the automated half'
        $script:Spec | Should -Match '(?m)^- \[ \] Portal container healthy under compose \(the other half: the owner''s compose smoke, P09-T18\)\.'
    }
}

Describe 'D-045 as built in 09c' {
    BeforeAll {
        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Section = [regex]::Match($log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
        $script:Guide = Get-RepoText 'docs/development/PORTAL-APP.md'
        $script:Spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
    }

    It 'records what the 09c build found, as consequences of the addendum' {
        foreach ($phrase in 'As built in 09c: the category list and the product list', 'As built in 09c: the pages never fail on a visitor', 'As built in 09c: the cache', 'As built in 09c: structured data',
                'As built in 09c: the sitemap', 'As built in 09c: the second markup site', 'Known in 09c: the sitemap build', 'Known in 09c: a plain-http image', 'Known in 09c: the category page shows no description',
                'Known in 09c: the package', 'Resolved in 09c: the product pages no longer link ahead', 'As built in 09c: deviations from the plan, driven by the spike', 'As built in 09c: the cache key and its case',
                'As built in 09c: the sitemap cache is stale-while-revalidate', 'As built in 09c: layering and plain text', 'with one, `/` redirects to it', 'Only all-lowercase paths are kept') {
            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the as-built list must carry: $phrase"
        }
    }

    It 'words the KbPlainText match timeout as 1 s, not 100 ms' {
        $script:Section | Should -Match ([regex]::Escape('`KbPlainText` patterns have a 1 s match timeout'))
        $script:Section | Should -Not -Match ([regex]::Escape('100 ms match timeout'))
    }

    It 'records the lower-case-only cache rule, the search-box merge and the article-only JSON-LD in the right places' {
        $script:Guide | Should -Match ([regex]::Escape('Only all-lowercase paths are kept'))
        $script:Guide | Should -Not -Match 'known exception'
        $script:Guide | Should -Match ([regex]::Escape('merged in 09d'))
        $script:Section | Should -Match ([regex]::Escape('there is no exception'))
        $script:Section | Should -Not -Match 'timing-dependent'
        $script:Spec | Should -Match ([regex]::Escape('JSON-LD is on the article page only'))
        (Get-RepoText 'docs/architecture/01-REQUIREMENTS.md') | Should -Match ([regex]::Escape('`GetKbSitemapRequestHandler` (via the Portal provider)'))
    }

    It 'records the sitemap rate-limit limit in the decision log and the developer guide' {
        foreach ($text in $script:Section, $script:Guide) {
            $text | Should -Match ([regex]::Escape('1 + N API calls under one forwarded IP'))
            $text | Should -Match ([regex]::Escape('120 per minute per IP'))
            $text | Should -Match ([regex]::Escape('about 120 or more active products'))
            $text | Should -Match ([regex]::Escape('a bulk sitemap endpoint, or exempting the Portal'))
        }
    }

    It 'has a developer guide that describes the help centre, the SEO head, the cache, the sitemap and the two markup sites' {
        foreach ($phrase in 'KbArticleBody', 'KbHome', 'KbCategory', 'KbSearch', 'KbArticle', 'Pager', 'StateMessage', 'KbCopy', 'JsonLdText', 'PortalCachePaths', 'AddPortalOutputCache', 'SetOnSuccess', 'X-Correlation-Id',
                'PortalSitemapCache', 'PortalSitemapBuilder', 'single-flight', '50,000', 'exactly two files', 'KbPaging', 'KbSlugShape', 'ListAsync') {
            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because "PORTAL-APP.md must mention $phrase"
        }
        $script:Guide | Should -Not -Match 'answers 404 until 09c'
        $script:Guide | Should -Not -Match 'search only; 09c extends it'
        $script:Guide | Should -Not -Match 'is used in one file'
    }

    It 'names the real sitemap handoff in the spec and the architecture, and the old names appear only where the spec says they never existed' {
        $script:Spec | Should -Match '(?m)^### Corrections \(D-045 addendum, 2026-10-06, PHASE-09c\)'
        $block = [regex]::Match($script:Spec, '(?ms)### Corrections \(D-045 addendum, 2026-10-06, PHASE-09c\).*?(?=^## )').Value
        $block | Should -Match 'GetKbSitemapRequestHandler'
        $block | Should -Match 'IPublicKbClient\.GetSitemapAsync'
        $block | Should -Match 'PortalSitemapBuilder'
        $script:Spec | Should -Match 'PortalSitemapBuilder` \(Portal\)'
        $script:Spec | Should -Match '`MapSeoSitemap` endpoint itself is package-owned'
        foreach ($old in 'ISitemapEntryProvider', 'GetSitemapEntriesRequestHandler', 'ApiSitemapEntryProvider') {
            $lines = $script:Spec -split '\r?\n' | Where-Object { $_ -match [regex]::Escape($old) }
            foreach ($line in $lines) {
                $line | Should -Match 'do not exist|never existed' -Because "$old may be named only to say it does not exist"
            }
        }
        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Not -Match 'GetSitemapEntriesRequestHandler'
        (Get-RepoText 'docs/architecture/UX-BRIEF-portal.md') | Should -Not -Match 'GetSitemapEntriesRequestHandler'
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

Describe 'D-046 (live updates)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-046: PHASE-10: live updates'
        $script:Log | Should -Match '(?s)## D-046:.*?- \*\*Status:\*\* Approved \(owner 2026-10-07.*?- \*\*Date:\*\* 2026-10-07'
        $script:Log | Should -Match '(?m)^\| D-046 \|.*\| 2026-10-07 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-07, PHASE-10 planning\):\*\* D-046'
    }

    It 'records the owner decisions and the technical rulings the two pull requests rely on' {
        foreach ($phrase in 'Two pull requests', 'New activity - refresh', 'Agent.Name', 'public token-provider API', 'UpdateTicketPresenceRequest', 'TicketChangeCaptureInterceptor', 'TicketChangePublishingInterceptor',
                'HubException("Ticket not found")', 'An `access_token` query value is not read', 'CloseOnAuthenticationExpiration', 'techstrap_ticket_changes', 'Resync', 'techstrap.live.connected_agents', 'PgBouncer', 'No new setting, no migration') {
            $script:Log | Should -Match ([regex]::Escape($phrase)) -Because "D-046 must mention $phrase"
        }
    }

    It 'corrects the PHASE-10 spec and the handler table' {
        $spec = Get-RepoText 'docs/architecture/PHASE-10-live-updates.md'
        $spec | Should -Match '(?m)^### Corrections \(D-046, 2026-10-07\)'
        foreach ($phrase in 'ICurrentUserService', 'LiveConnectionState', 'EventId', 'access_token', 'UpdateTicketPresenceRequest') {
            $spec | Should -Match ([regex]::Escape($phrase))
        }
        $architecture = Get-RepoText 'docs/architecture/02-ARCHITECTURE.md'
        $architecture | Should -Not -Match 'unauthorized joins abort the connection'
        $architecture | Should -Match 'TicketChangeCaptureInterceptor'
    }

    It 'no longer says a merged phase is pending merge in the roadmap and discovery rows' {
        foreach ($file in 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md') {
            $text = Get-RepoText $file
            $text | Should -Match '(?m)^\| 07 \|.*\| Complete: 07a merged \(PR #9\), 07b merged \(PR #10\), 07c merged \(PR #11\)'
            $text | Should -Not -Match '(?m)^\| 0[789] \|.*pending merge'
        }
    }

    It 'ticks all of PHASE-10 after 10b: P10-T01 to T17, every deliverable and every success criterion' {
        $spec = Get-RepoText 'docs/architecture/PHASE-10-live-updates.md'
        foreach ($number in 1..17) {
            $id = 'P10-T{0:00}' -f $number
            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 10a or 10b"
        }
        foreach ($number in 11..17) {
            $id = 'P10-T{0:00}' -f $number
            # The note must sit inside the task's own block: a lazy match would run on into the next task.
            $spec | Should -Match ('(?s)- \[x\] \*\*' + $id + '\*\*(?:(?!- \[[x ]\] \*\*P10-T).)*?\*\*As built \(10b\):\*\*') -Because "$id carries its as-built note"
        }
        # The as-built notes quote the banner copy of the UI, which has an en dash.
        $spec | Should -Match ('QueueLiveBanner` \("Queue updated {0} refresh"\)' -f [char]0x2013)
        $spec | Should -Match ('ChangedTicketBanner` \("New activity {0} refresh"\)' -f [char]0x2013)
        $deliverables = ($spec -split '(?m)^## Deliverables')[1] -split '(?m)^## Actionable Tasks' | Select-Object -First 1
        $deliverables | Should -Not -Match '- \[ \]'
        $criteria = ($spec -split '(?m)^## Success Criteria')[1] -split '(?m)^## Boundary Validation' | Select-Object -First 1
        $criteria | Should -Not -Match '- \[ \]'
        $spec | Should -Match '(?m)^- \[x\] Admin `ITicketLiveClient`'
        $spec | Should -Match '(?m)^- \[x\] Reverse-proxy/WebSocket notes'
        $spec | Should -Match '\*\*Kill switch \(10b addendum, 2026-10-07\)\.\*\*'
    }

    It 'says in the roadmap and discovery rows that 10a and 10b are merged and PHASE-10 is complete' {
        foreach ($file in 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md') {
            $text = Get-RepoText $file
            $text | Should -Match '(?m)^\| 10 \|.*\| 10a merged \(PR #18\); 10b merged \(PR #19\); PHASE-10 complete: the owner'
            $text | Should -Not -Match '10a complete \(pending merge\)'
        }
    }

    It 'tells the operator that the hub is internal and that LISTEN needs a direct connection' {
        $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
        $runbook | Should -Match '(?m)^## Live updates \(PHASE-10\)'
        foreach ($phrase in 'The hub is internal', 'PgBouncer in transaction mode breaks `LISTEN`', 'techstrap-ticket-change-listener', 'Do not publish `/hubs`', 'No new settings') {
            $runbook | Should -Match ([regex]::Escape($phrase)) -Because "the live-updates note must say $phrase"
        }
    }

    It 'records the two packages the live updates use' {
        $map = Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md'
        $map | Should -Match 'TechStrap\.Api\.Tests` connects a real `HubConnection` in 10a'
        $map | Should -Match 'A direct reference of `TechStrap\.Infrastructure` \(10a, D-046\)'
    }
}

Describe 'D-046 addendum (PHASE-10b, live updates in the Admin)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'records the owner rulings and the spike findings as an addendum of D-046, not as a new decision' {
        $script:Log | Should -Match '(?m)^### Addendum \(2026-10-07, PHASE-10b live updates in the Admin\)'
        $addendum = ($script:Log -split '(?m)^### Addendum \(2026-10-07, PHASE-10b live updates in the Admin\)')[1]
        foreach ($phrase in 'LiveUpdates:Enabled', 'LIVEUPDATES__ENABLED', 'NullTicketLiveClient', 'AdminLiveClientHostTests', 'Own changes are ignored', ('Queue updated {0} refresh' -f [char]0x2013), 'IUserAccessTokenProvider',
                'OnAfterRenderAsync', 'compose is not edited', 'Known limits (10b)', 'There is no new decision number', 'does not sign the agent out') {
            $addendum | Should -Match ([regex]::Escape($phrase)) -Because "the 10b addendum must mention $phrase"
        }
    }

    It 'documents live updates for agents and operators, with the manual check and the known gaps, and no longer says they are coming' {
        $admin = Get-RepoText 'docs/development/ADMIN-APP.md'
        $admin | Should -Match '(?m)^## Live updates \(10b\)'
        $admin | Should -Match '(?m)^### Manual check against a real identity provider \(owner\)'
        $admin | Should -Match '(?m)^## Known gaps in 10b'
        $admin | Should -Match 'before the live connection is first established \(on either page\), or while a ticket.s first load runs, raises no banner'
        $admin | Should -Match '(?s)## Known gaps in 10b.*refuses the connection for good'
        $admin | Should -Match "UPDATE tickets SET solved_at = now\(\) - interval '2 days' WHERE number"
        $admin | Should -Match ('New activity {0} refresh' -f [char]0x2013)
        $admin | Should -Match ('Queue updated {0} refresh' -f [char]0x2013)
        $admin | Should -Match '(?m)^\| `LIVEUPDATES__ENABLED` \| no \| `true` \|'
        $admin | Should -Not -Match 'live updates arrive with PHASE-10'
        $admin | Should -Not -Match 'it is not live \(PHASE-10\)'
        $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
        $runbook | Should -Match 'LIVEUPDATES__ENABLED'
    }

    It 'maps the packages 10b uses: Blazor.Auth 0.2.0 with the token provider, and the SignalR client in the Admin' {
        $map = Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md'
        $map | Should -Match '\| `SyntaxCircus\.Blazor\.Auth` \| 0\.2\.0 \|'
        $map | Should -Match 'dragon-poop 0\.1\.7; TechStrap first on 0\.2\.0'
        $map | Should -Match 'IUserAccessTokenProvider'
        $map | Should -Match 'SignalRTicketLiveClient'
    }
}

Describe 'D-047 (client SDK)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-047: PHASE-11: client SDK'
        $script:Log | Should -Match '(?s)## D-047:.*?- \*\*Status:\*\* Approved \(owner 2026-10-07.*?- \*\*Date:\*\* 2026-10-07'
        $script:Log | Should -Match '(?m)^\| D-047 \|.*HttpRequestResiliencePipeline.*\| 2026-10-07 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-07, PHASE-11 planning\):\*\* D-047'
    }

    It 'records the owner decisions, the technical rulings and the known limits' {
        $section = ($script:Log -split '(?m)^## D-047:')[1]
        foreach ($phrase in 'Three pull requests', 'JSON-only', 'HttpRequestResiliencePipeline', 'TechStrapClientErrorCodes', 'NotReplayable', 'Microsoft.AspNetCore.App', 'eng/Packaging.props', 'P11-T05',
                'Owner decisions (2026-10-07)', 'Technical rulings', '**Known limits**', 'Retry-After', 'one circuit per DI container', 'net10.0') {
            $section | Should -Match ([regex]::Escape($phrase)) -Because "D-047 must mention $phrase"
        }
    }

    It 'corrects the PHASE-11 spec and ticks what 11a delivered, leaving attachments open' {
        $spec = Get-RepoText 'docs/architecture/PHASE-11-client-sdk.md'
        $spec | Should -Match '(?m)^### Corrections \(D-047, 2026-10-07\)'
        $spec | Should -Match 'Where this page and D-047 differ, D-047 wins\.'
        foreach ($id in 'P11-T01', 'P11-T02', 'P11-T03', 'P11-T04', 'P11-T06', 'P11-T10', 'P11-T17') {
            $spec | Should -Match ('(?s)- \[x\] \*\*' + $id + '\*\*(?:(?!- \[[x ]\] \*\*P11-T).)*?\*\*As built \(11a\):\*\*') -Because "$id carries its as-built note"
        }
        $spec | Should -Match '(?m)^- \[ \] \*\*P11-T05\*\*.*\(deferred, D-047\)'
        foreach ($id in 'P11-T16') {
            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id was delivered by 11c"
        }
    }

    It 'says in the roadmap and discovery rows that 10b is merged and 11a is complete pending merge' {
        foreach ($file in 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md') {
            $text = Get-RepoText $file
            $text | Should -Match '(?m)^\| 10 \|.*\| 10a merged \(PR #18\); 10b merged \(PR #19\); PHASE-10 complete: the owner'
            $text | Should -Not -Match '10b complete \(pending merge\)'
            $text | Should -Match '(?m)^\| 11 \|.*11a merged \(PR #20\); 11b merged \(PR #21\); 11c merged \(PR #22\); v0\.1\.0 published 2026-10-08 \(PHASE-11 complete\)'
            $text | Should -Match 'T05 \(attachments\) deferred to 11d, which first needs multipart intake'
        }
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| P11-T05 \|.*\(deferred, D-047\)'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Not -Match 'excluded by the solution filter until 11b'
    }

    It 'maps the SDK dependencies: the pipeline, not a plain HttpClient, and the web-neutral Common' {
        $map = Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md'
        $map | Should -Not -Match 'plain `HttpClient`'
        $map | Should -Match 'HttpRequestResiliencePipeline'
        $map | Should -Match '\| `SyntaxCircus\.Common` \| 0\.2\.0 \|'
        $map | Should -Match '\| `SyntaxCircus\.AspNetCore\.Common` \| 0\.1\.16 \|'
        $architecture = Get-RepoText 'docs/architecture/02-ARCHITECTURE.md'
        $architecture | Should -Match 'TechStrap\.Client\.Tests` was created in PHASE-11a'
        $architecture | Should -Match 'retry only with an Idempotency-Key \(D-047\)'
    }

    It 'documents the SDK for maintainers' {
        $doc = Get-RepoText 'docs/development/CLIENT-SDK.md'
        foreach ($heading in 'Packages', 'Local pack', 'Configuration', 'Retry and idempotency', 'Error codes', 'Key safety', 'Running the sample', 'Tests', 'Known limits') {
            $doc | Should -Match ('(?m)^## ' + [regex]::Escape($heading)) -Because "CLIENT-SDK.md needs a $heading section"
        }
        foreach ($phrase in 'Test-PackageContents.ps1', 'AddTechStrapClient', 'Replayable', 'NotReplayable', 'TechStrapClientErrorCodes', 'Integration', 'one new ticket') {
            $doc | Should -Match ([regex]::Escape($phrase)) -Because "CLIENT-SDK.md must mention $phrase"
        }
    }
}

Describe 'D-048 (MAUI helper)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-048: PHASE-11b: MAUI helper'
        $script:Log | Should -Match '(?s)## D-048:.*?- \*\*Status:\*\* Approved \(owner 2026-10-07.*?- \*\*Date:\*\* 2026-10-07'
        $script:Log | Should -Match '(?m)^\| D-048 \|.*TicketMetadataKeys.*\| 2026-10-07 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-07, PHASE-11b planning\):\*\* D-048'
    }

    It 'records the owner decisions, the technical rulings and the known limits' {
        $section = ($script:Log -split '(?m)^## D-048:')[1]
        foreach ($phrase in 'Single net10.0', 'Microsoft.Maui.Essentials', 'TicketMetadataKeys', 'MauiTicketDraft', 'UseMaui', 'NETSDK1147',
                'Owner decisions (2026-10-07)', 'Technical rulings', '**Known limits**', 'metadata-invalid', 'MAUI >= 10.0.0') {
            $section | Should -Match ([regex]::Escape($phrase)) -Because "D-048 must mention $phrase"
        }
    }

    It 'corrects the PHASE-11 spec and ticks T07 to T09 with as-built notes, leaving attachments open' {
        $spec = Get-RepoText 'docs/architecture/PHASE-11-client-sdk.md'
        $spec | Should -Match '(?m)^### Corrections \(D-048, 2026-10-07\)'
        $spec | Should -Match 'Where this page and D-048 differ, D-048 wins\.'
        foreach ($id in 'P11-T07', 'P11-T08', 'P11-T09') {
            $spec | Should -Match ('(?s)- \[x\] \*\*' + $id + '\*\*(?:(?!- \[[x ]\] \*\*P11-T).)*?\*\*As built \(11b\):\*\*') -Because "$id carries its as-built note"
        }
        $spec | Should -Match '(?m)^- \[ \] \*\*P11-T05\*\*.*\(deferred, D-047\)'
        foreach ($id in 'P11-T16') {
            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id was delivered by 11c"
        }
    }

    It 'maps the Essentials package and the Maui project in the package map' {
        $map = Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md'
        $map | Should -Match '(?m)^\| `Microsoft\.Maui\.Essentials` \| Selected \| 10\.0\.0 \|.*\| P11 \|'
        $map | Should -Match '(?m)^\| `TechStrap\.Client\.Maui` \|.*Microsoft\.Maui\.Essentials.*\(D-048\)'
        $map | Should -Not -Match 'MAUI workload build'
        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Match 'TechStrap\.Client\.Maui\.Tests` was created in PHASE-11b'
    }

    It 'retires owner action 10 in the roadmap' {
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match 'Withdrawn \(D-048\): single net10\.0, no macOS runner'
    }

    It 'documents the MAUI helper for maintainers' {
        $doc = Get-RepoText 'docs/development/CLIENT-SDK.md'
        $doc | Should -Match '(?m)^## TechStrap\.Client\.Maui'
        foreach ($phrase in 'AddTechStrapMaui', 'MauiTicketDraft', 'TicketMetadataKeys', 'metadata-invalid', 'UseMaui') {
            $doc | Should -Match ([regex]::Escape($phrase)) -Because "CLIENT-SDK.md must mention $phrase"
        }
    }
}

Describe 'D-049 (publishing)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-049: PHASE-11c: publishing'
        $script:Log | Should -Match '(?s)## D-049:.*?- \*\*Status:\*\* Approved \(owner 2026-10-08.*?- \*\*Date:\*\* 2026-10-08'
        $script:Log | Should -Match '(?m)^\| D-049 \|.*publish-nuget\.yml.*\| 2026-10-08 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-08, PHASE-11c planning\):\*\* D-049'
    }

    It 'records the owner decisions, the technical rulings, the alternatives and the known limits' {
        $section = ($script:Log -split '(?m)^## D-049:')[1]
        foreach ($phrase in 'publish-nuget.yml', 'Trusted Publishing', 'GenerateDocumentationFile', 'TechStrapJsonContext',
                'samples/TechStrap.Client.Samples.Console', 'RELEASING.md', 'Owner decisions (2026-10-08)', 'Technical rulings',
                'Alternatives Considered', '**Known limits**', 'NUGET_USER') {
            $section | Should -Match ([regex]::Escape($phrase)) -Because "D-049 must mention $phrase"
        }
    }

    It 'corrects the PHASE-11 spec, ticks T11 to T16 with as-built notes' {
        $spec = Get-RepoText 'docs/architecture/PHASE-11-client-sdk.md'
        $spec | Should -Match '(?m)^### Corrections \(D-049, 2026-10-08\)'
        $spec | Should -Match 'Where this page and D-049 differ, D-049 wins\.'
        foreach ($id in 'P11-T11', 'P11-T12', 'P11-T13', 'P11-T14', 'P11-T15') {
            $spec | Should -Match ('(?s)- \[x\] \*\*' + $id + '\*\*(?:(?!- \[[x ]\] \*\*P11-T).)*?\*\*As built \(11c\):\*\*') -Because "$id carries its as-built note"
        }
        $spec | Should -Match '(?s)- \[x\] \*\*P11-T16\*\*(?:(?!- \[[x ]\] \*\*P11-T).)*?\*\*As built \(11c, 2026-10-08\):\*\*'
        $spec | Should -Not -Match 'Pending \(11c\)'
    }

    It 'records the first version as v0.1.0 in a D-049 addendum and in the PHASE-11 corrections' {
        $section = ($script:Log -split '(?m)^## D-049:')[1]
        $section | Should -Match '(?m)^### Addendum \(2026-10-08, first version\)'
        $section | Should -Match 'the first published version is `v0\.1\.0`'
        $spec = Get-RepoText 'docs/architecture/PHASE-11-client-sdk.md'
        $spec | Should -Match '(?s)### Corrections \(D-049, 2026-10-08\).*?First version: `v0\.1\.0`, not `v1\.0\.0-rc\.1` \(D-049 addendum, 2026-10-08\)'
        foreach ($file in 'docs/architecture/PHASE-11-client-sdk.md', 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/development/RELEASING.md', 'README.md') {
            (Get-RepoText $file) | Should -Match 'v0\.1\.0' -Because "$file names the first version"
        }
    }

    It 'says in the roadmap and discovery rows that 11c is merged and v0.1.0 is published' {
        foreach ($file in 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md') {
            (Get-RepoText $file) | Should -Match '(?m)^\| 11 \|.*11c merged \(PR #22\); v0\.1\.0 published 2026-10-08 \(PHASE-11 complete\); T05 \(attachments\) deferred to 11d'
        }
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 9 \|.*Done 2026-10-08'
    }

    It 'maps the SDK packaging in the package map and names the sample in the architecture' {
        $map = Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md'
        $map | Should -Match '(?m)^\| `Microsoft\.Extensions\.Hosting` \|'
        $map | Should -Match 'hosts only; packages take their version from the release tag \(D-049\)'
        $map | Should -Match 'not referenced: SourceLink is bundled in the SDK \(D-049\)'
        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Match ([regex]::Escape('samples/TechStrap.Client.Samples.Console'))
    }

    It 'names the three packages in the root README and links the SDK README and the release guide' {
        $readme = Get-RepoText 'README.md'
        foreach ($id in 'TechStrap.Contracts', 'TechStrap.Client', 'TechStrap.Client.Maui') {
            $readme | Should -Match ([regex]::Escape($id)) -Because "README names $id"
        }
        $readme | Should -Match ([regex]::Escape('(src/TechStrap.Client/README.md)'))
        $readme | Should -Match ([regex]::Escape('(docs/development/RELEASING.md)'))
    }

    It 'has no stale 11c deferral in the SDK guide and explains the dry run and the package version' {
        (Get-RepoText 'docs/development/CLIENT-SDK.md') | Should -Not -Match '(?i)until 11c|arrives in 11c|in 11c\b'
        $doc = Get-RepoText 'docs/development/RELEASING.md'
        $doc | Should -Match 'only be dispatched once it is on `main`'
        $doc | Should -Match 'assembly versions carry the tag too|stamps the assembly versions'
        $doc | Should -Match 'organization secret'
        $doc | Should -Match 'Selected branches and tags'
    }

    It 'scopes the Contracts SemVer promise to the SDK-facing surface in the README, the decision and the SDK guide' {
        foreach ($file in 'src/TechStrap.Contracts/README.md', 'docs/architecture/04-DECISION-LOG.md', 'docs/development/CLIENT-SDK.md') {
            (Get-RepoText $file) | Should -Match 'SDK-facing surface' -Because "$file scopes the stability promise"
        }
    }
}

Describe 'D-050 (product hosts)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-050: PHASE-11e: product hosts'
        $script:Log | Should -Match '(?s)## D-050:.*?- \*\*Status:\*\* Approved \(owner 2026-10-08.*?- \*\*Date:\*\* 2026-10-08'
        $script:Log | Should -Match '(?m)^\| D-050 \|.*PortalHost.*\| 2026-10-08 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-08, PHASE-11e planning\):\*\* D-050'
    }

    It 'amends D-002 under its status line' {
        $script:Log | Should -Match '(?s)## D-002:.*?- \*\*Amended by D-050 \(2026-10-08\):\*\* products may have their own portal host'
    }

    It 'records the decisions, the alternatives and the known limits' {
        $section = ($script:Log -split '(?m)^## D-050:')[1]
        foreach ($phrase in 'PortalHost', 'HostNameShape', 'ProductHostMiddleware', 'PortalLinks', 'SetVaryByHost', 'product-host-taken',
                'stale-while-revalidate', 'Owner decisions (2026-10-08)', 'Technical rulings', 'Alternatives Considered', '**Known limits**') {
            $section | Should -Match ([regex]::Escape($phrase)) -Because "D-050 must mention $phrase"
        }
    }

    It 'does not overstate what survives a host change and states the setup order' {
        $section = ($script:Log -split '(?m)^## D-050:')[1]
        $section | Should -Not -Match 'never breaks a ticket link'
        $section | Should -Not -Match 'disagree for up to 60'
        $section | Should -Match 'product-host-reserved'
        $deploy = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
        $deploy | Should -Not -Match 'so they keep working on the default host'
        $deploy | Should -Match 'preserves `/t/` links only'
        $deploy | Should -Match '(?s)Only then set the "Portal host" field'
        $deploy | Should -Match '<TECHSTRAP_PORTAL_PORT>'
        (Get-RepoText 'docs/development/PORTAL-APP.md') | Should -Not -Match 'ticket link keeps working after a host change'
    }

    It 'has the 11e row in the roadmap and the discovery index' {
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 11e \|.*D-050.*\| 11e merged \(PR #25\)'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 11e \|.*\| 11e merged \(PR #25\)'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| P11e-T07 \|'
    }

    It 'amends the D-050 known limits (clear-vs-keep host, cached 301s, per-host robots.txt) and names the 0.2.0 break' {
        $section = ($script:Log -split '(?m)^## D-050:')[1]
        foreach ($phrase in 'Amended 2026-10-08', 'max-age=3600', 'ProductHostSeoUrlBuilder', 'null leaves') {
            $section | Should -Match ([regex]::Escape($phrase)) -Because "D-050 must mention $phrase"
        }
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match 'tag v0\.2\.0 after this PR merges'
        (Get-RepoText 'src/TechStrap.Contracts/README.md') | Should -Match '(?m)^## Version notes'
        (Get-RepoText 'docs/development/RELEASING.md') | Should -Match 'Version notes'
    }

    It 'says in the Portal guide that the Host header is a lookup key only' {
        $guide = Get-RepoText 'docs/development/PORTAL-APP.md'
        $guide | Should -Not -Match 'never the Host header'
        $guide | Should -Match 'lookup key'
        $guide | Should -Match '(?m)^### Product hosts'
    }

    It 'documents product hosts in the runbook and the UX brief' {
        (Get-RepoText 'docs/self-hosting/DEPLOYMENT.md') | Should -Match '(?m)^## Product hosts'
        (Get-RepoText 'docs/architecture/UX-BRIEF-portal.md') | Should -Match 'own host'
        (Get-RepoText 'docs/architecture/05-SCHEMA.md') | Should -Match 'ix_products_portal_host'
    }

    It 'corrects the PHASE-11e spec and ticks every task' {
        $spec = Get-RepoText 'docs/architecture/PHASE-11e-product-hosts.md'
        $spec | Should -Match '(?m)^### Corrections \(D-050, 2026-10-08\)'
        $spec | Should -Match 'Where this page and D-050 differ, D-050 wins\.'
        foreach ($n in 1..7) {
            $spec | Should -Match ('- \[x\] \*\*P11e-T0' + $n + '\*\*') -Because "P11e-T0$n is ticked"
        }
        $spec | Should -Not -Match '- \[ \] \*\*P11e-T0'
    }
}

Describe 'D-051 (release hardening)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-051: PHASE-12'
        $script:Log | Should -Match '(?s)## D-051:.*?- \*\*Status:\*\* Approved \(owner 2026-10-08.*?- \*\*Date:\*\* 2026-10-08'
        $script:Log | Should -Match '(?m)^\| D-051 \|.*v0\.3\.0.*\| 2026-10-08 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-08, PHASE-12 planning\):\*\* D-051'
    }

    It 'records the decisions and the technical rulings' {
        $section = ($script:Log -split '(?m)^## D-051:')[1]
        foreach ($phrase in 'v0.3.0', '12a', '12b', '12c', 'SECURITY-REVIEW.md', 'MessageBubble', 'Trivy', 'CycloneDX', 'Alternatives Considered') {
            $section | Should -Match ([regex]::Escape($phrase)) -Because "D-051 must mention $phrase"
        }
    }

    It 'corrects the PHASE-12 spec' {
        $spec = Get-RepoText 'docs/architecture/PHASE-12-release-hardening.md'
        $spec | Should -Match '(?m)^### Corrections \(D-051, 2026-10-08\)'
        $spec | Should -Match 'Where this page and D-051 differ, D-051 wins\.'
        $spec | Should -Match '\*\*P12-T14\*\*.*latest published tag.*\(D-051\)'
    }

    It 'has the 12 row in the roadmap and the discovery index' {
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 12 \|.*D-051.*12a'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 12 \|.*12a'
    }
}

Describe 'Security review (PHASE-12a)' {
    BeforeAll { $script:Review = Get-RepoText 'docs/security/SECURITY-REVIEW.md' }

    It 'has the method, the severity scale, the finding template and a Release 0.3.0 section' {
        foreach ($heading in 'Purpose and method', 'Severity scale', 'Finding template', 'Release 0.3.0', 'Findings') {
            $script:Review | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because "the review needs a $heading section"
        }
        foreach ($level in 'Critical', 'High', 'Medium', 'Low', 'Info') { $script:Review | Should -Match ('(?m)^- \*\*' + $level + '\*\*') }
    }

    It 'has the six path-group checklists' {
        foreach ($group in '1\. Customer access tokens', '2\. API keys', '3\. Uploads', '4\. Sanitiser and rendering', '5\. Authorization and headers', '6\. Privacy and operations') {
            $script:Review | Should -Match ('(?m)^### ' + $group + '\s*$')
        }
    }

    It 'gives every SR finding a path group, a severity, a status and evidence' {
        $findings = [regex]::Matches($script:Review, '(?ms)^### (SR-\d\d): .*?(?=^### |^## |\z)')
        $findings.Count | Should -BeGreaterOrEqual 16
        foreach ($finding in $findings) {
            $finding.Value | Should -Match '(?m)^- Path group: .+$' -Because $finding.Groups[1].Value
            $finding.Value | Should -Match '(?m)^- Severity: (Critical|High|Medium|Low|Info)\s*$' -Because $finding.Groups[1].Value
            $finding.Value | Should -Match '(?m)^- Status: (Open|Fixed|Accepted)\s*$' -Because $finding.Groups[1].Value
            $finding.Value | Should -Match '(?m)^- Evidence: .+$' -Because $finding.Groups[1].Value
        }
    }

    It 'has no TODO anywhere' { $script:Review | Should -Not -Match 'TODO' }

    It 'is ASCII only' { ([regex]::IsMatch($script:Review, '[^\x00-\x7F]')) | Should -BeFalse }
}

Describe 'PHASE-12a close-out' {
    BeforeAll {
        $script:Review = Get-RepoText 'docs/security/SECURITY-REVIEW.md'
        $script:Spec = Get-RepoText 'docs/architecture/PHASE-12-release-hardening.md'
        $script:Plan = Get-RepoText 'docs/superpowers/plans/2026-10-08-phase-12a-hardening.md'
    }

    It 'leaves no finding open and no checklist row waiting for a later task' {
        $script:Review | Should -Not -Match '(?m)^- Status: Open\s*$'
        $script:Review | Should -Not -Match 'Task \d adds'
    }

    It 'has an evidence cell in every checklist row' {
        $rows = [regex]::Matches($script:Review, '(?m)^\| (?!Check \||Header or behaviour|---)[^\r\n]*\|\s*$')
        $rows.Count | Should -BeGreaterThan 40
        foreach ($row in $rows) {
            $cells = $row.Value.Trim().Trim('|') -split '(?<!\\)\|' | ForEach-Object { $_.Trim() }
            $cells.Count | Should -BeGreaterOrEqual 2 -Because $row.Value
            foreach ($cell in $cells) { $cell | Should -Not -BeNullOrEmpty -Because $row.Value }
        }
    }

    It 'lists SR-01 to SR-18' {
        foreach ($n in 1..18) { $script:Review | Should -Match ('(?m)^### SR-{0:D2}: ' -f $n) }
    }

    It 'reports the architecture conformance gate with 0 discrepancies and signs off the release' {
        $script:Review | Should -Match '(?m)^## Architecture conformance\s*$'
        $section = ($script:Review -split '(?m)^## Architecture conformance\s*$')[1]
        $section | Should -Match '0 discrepancies'
        $section | Should -Match 'EntryPointCatalogTests'
        $script:Review | Should -Match 'Release 0\.3\.0 sign-off'
        $script:Review | Should -Match '(?s)Release 0\.3\.0 sign-off.*?0 open High or Critical.*?2026-10-\d\d'
    }

    It 'ticks P12-T01 to P12-T10 and P12-T19 with an as-built note (12a)' {
        foreach ($n in (1..10) + 19) {
            $id = 'P12-T{0:D2}' -f $n
            $script:Spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done in 12a"
            $block = [regex]::Match($script:Spec, '(?ms)^- \[x\] \*\*' + $id + '\*\*.*?(?=^- \[|^## |\z)').Value
            $block | Should -Match '\*\*As built \(12a\):\*\*' -Because "$id needs an as-built note"
        }
        $script:Spec | Should -Match '(?m)^- \[x\] `docs/security/SECURITY-REVIEW\.md`'
    }

    It 'marks phase 12 as 12a merged in the roadmap and the discovery index' {
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 12 \|.*D-051.*12a merged \(PR #28\)'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 12 \|.*12a merged \(PR #28\)'
    }

    It 'has the 12a plan fully ticked with an As built section' {
        $script:Plan | Should -Not -Match '(?m)^\s*- \[ \]'
        $script:Plan | Should -Match '(?m)^## As built\s*$'
    }
}

Describe 'Backup and restore runbook (PHASE-12b)' {
    BeforeAll { $script:Runbook = Get-RepoText 'docs/runbooks/backup-restore.md' }

    It 'has the required sections' {
        foreach ($heading in 'Scope', 'RPO and RTO', 'Prerequisites and secrets', 'Taking a backup', 'Encryption and off-box copy', 'Retention', 'Restoring', 'Verification checklist', 'Consistency caveat', 'Disaster scenarios', 'Keys volumes', 'Client and server version', '12b rehearsal record') {
            $script:Runbook | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because "the runbook needs a $heading section"
        }
    }

    It 'states the targets, the migration check and the resurrection caveat' {
        foreach ($phrase in 'RPO', 'RTO', '24 h', '4 h', '__EFMigrationsHistory', 'resurrect', 'admin_events', 'deploy/backup.sh', 'deploy/restore.sh', 'OnCalendar', 'crontab') {
            $script:Runbook | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
    }

    It 'never tells the operator to remove volumes and is ASCII' {
        $script:Runbook | Should -Not -Match 'down\s+-v(\s|$)'
        $script:Runbook | Should -Not -Match '--volumes'
        ([regex]::IsMatch($script:Runbook, '[^\x00-\x7F]')) | Should -BeFalse
    }

    It 'has no TODO and no passphrase value' {
        $script:Runbook | Should -Not -Match 'TODO'
        $script:Runbook | Should -Not -Match '(?i)passphrase\s*[=:]\s*[A-Za-z0-9]{8,}'
    }

    It 'keeps the backups folder out of git' {
        (Get-RepoText '.gitignore') | Should -Match '(?m)^backups/\s*$'
    }

    It 'links only to files that exist' {
        $links = [regex]::Matches($script:Runbook, '\]\((?<path>(?!https?:|#)[^)\s#]+)') | ForEach-Object { $_.Groups['path'].Value }
        foreach ($link in $links) {
            Test-Path -LiteralPath (Join-Path $script:RepoRoot 'docs' 'runbooks' $link) | Should -BeTrue -Because "backup-restore.md links to $link"
        }
    }
}

Describe 'PHASE-12b close-out' {
    BeforeAll {
        $script:Spec = Get-RepoText 'docs/architecture/PHASE-12-release-hardening.md'
        $script:Plan = Get-RepoText 'docs/superpowers/plans/2026-10-09-phase-12b-docs-scripts.md'
        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
    }

    It 'ticks P12-T11, T13, T16 and T17 with an as-built (12b) note' {
        foreach ($id in 'P12-T11', 'P12-T13', 'P12-T16', 'P12-T17') {
            $script:Spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done in 12b"
            $block = [regex]::Match($script:Spec, '(?ms)^- \[x\] \*\*' + $id + '\*\*.*?(?=^- \[|^## |\z)').Value
            $block | Should -Match '\*\*As built \(12b\):\*\*' -Because "$id needs an as-built note"
        }
    }

    It 'leaves the 12c tasks open' {
        foreach ($id in 'P12-T12', 'P12-T14', 'P12-T15', 'P12-T18', 'P12-T20', 'P12-T21') {
            $script:Spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is 12c"
        }
    }

    It 'records the 12b corrections in the spec' {
        $corrections = ($script:Spec -split '(?m)^### Corrections \(D-051, 2026-10-08\)\s*$')[1]
        foreach ($phrase in 'SELF-HOSTING.md', 'AUTHENTIK.md', 'docs/runbooks/backup-restore.md', 'append-only', 'not in CI', 'bash', 'openssl', 'private') {
            $corrections | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
    }

    It 'adds the D-051 addendum 12b rulings' {
        $script:Log | Should -Match '12b rulings \(2026-10-09\)'
        $addendum = ($script:Log -split '12b rulings \(2026-10-09\)')[1]
        foreach ($phrase in 'tests/load', 'X-Forwarded-For', 'postgres:17', 'openssl', 'rehearsal', 'drill', 'SELF-HOSTING.md', 'syntax-circus-authentik', '26MiB') {
            $addendum | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
    }

    It 'marks phase 12 as 12b merged, with the v0.2.1 fixes, in the roadmap and the discovery index' {
        foreach ($text in (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md'), (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md')) {
            $text | Should -Match '(?m)^\| 12 \|.*12a merged \(PR #28\); 12b merged \(PR #29\); v0\.2\.1 \(PR #30'
            $text | Should -Not -Match '12b complete \(pending merge\)'
        }
    }

    It 'no workflow runs the load or backup scripts (on-demand only)' {
        $workflows = @(Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot '.github' 'workflows') -File -Filter *.yml)
        $workflows.Count | Should -BeGreaterThan 0
        foreach ($file in $workflows) {
            # The whole file text, so the bodies of `run: |` blocks are covered too.
            (Get-Content -LiteralPath $file.FullName -Raw) | Should -Not -Match '\bk6\b|backup\.sh|restore\.sh|Invoke-LoadTest' -Because "$($file.Name) must not run the on-demand load or backup scripts"
        }
    }

    It 'has the 12b plan fully ticked with an As built section' {
        $script:Plan | Should -Not -Match '(?m)^\s*- \[ \]'
        $script:Plan | Should -Match '(?m)^## As built\s*$'
    }
}

Describe 'PHASE-11f landing page and product logos (D-052)' {
    BeforeAll {
        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Spec = Get-RepoText 'docs/architecture/PHASE-11f-landing-and-logos.md'
    }

    It 'records D-052 with the owner decisions, the rulings and the D-045 amendment' {
        $script:Log | Should -Match '(?m)^## D-052: PHASE-11f: Portal landing page and product logos'
        $script:Log | Should -Match '(?m)^- \*\*Amended by D-052 \(2026-10-09\):\*\* `TECHSTRAP_PORTAL_LANDING=Products`'
        $entry = ($script:Log -split '(?m)^## D-052: ')[1]
        foreach ($phrase in 'TECHSTRAP_PORTAL_LANDING', 'ListedOnLanding', 'Tagline', 'UploadedLogo', 'product-logos/', 'IProductLogoUrls', 'CappedImageIntake', 'PNG, JPEG or WebP', '1 MiB', 'Contracts 0.3.0', 'Alternatives Considered', 'Approved on:** 2026-10-09') {
            $entry | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
    }

    It 'has the 11f spec with ten tasks and the owner answers' {
        foreach ($n in 1..10) {
            $script:Spec | Should -Match ('- \[[ x]\] \*\*P11f-T' + $n.ToString('00') + '\*\*') -Because "P11f-T$($n.ToString('00')) exists"
        }
        foreach ($phrase in 'TECHSTRAP_PORTAL_LANDING', 'ListedOnLanding', 'landing card only', 'PNG, JPEG or WebP', 'Contracts 0.3.0', '/product-logos/', 'Amends D-045|amends D-045') {
            $script:Spec | Should -Match $phrase -Because $phrase
        }
    }

    It 'has the 11f row in the roadmap and the discovery index and the task table' {
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 11f \|.*D-052.*\| D-052 recorded; 11f'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 11f \|.*\| D-052 recorded; 11f'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| P11f-T10 \|'
    }

    It 'ticks every 11f task and marks 11f complete pending merge' {
        foreach ($n in 1..10) { $script:Spec | Should -Match ('- \[x\] \*\*P11f-T' + $n.ToString('00') + '\*\*') -Because "P11f-T$($n.ToString('00')) is ticked" }
        $script:Spec | Should -Not -Match '- \[ \] \*\*P11f-T'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 11f \|.*\| D-052 recorded; 11f complete \(pending merge\): T01 to T10'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 11f \|.*\| D-052 recorded; 11f complete \(pending merge\): T01 to T10'
    }

    It 'documents the landing setting, the logo proxy path, the runbook prefix and the security tests' {
        $selfHost = Get-RepoText 'docs/self-hosting/SELF-HOSTING.md'
        $selfHost | Should -Match 'TECHSTRAP_PORTAL_LANDING'
        $selfHost | Should -Match '/product-logos/'
        $selfHost | Should -Match 'plain http'
        (Get-RepoText 'docs/runbooks/backup-restore.md') | Should -Match 'product-logos/'
        $review = Get-RepoText 'docs/security/SECURITY-REVIEW.md'
        foreach ($phrase in 'ProductLogoStoreTests', 'ProductLogoServingTests', 'ProductLogoDiskFullTests', 'D-052') { $review | Should -Match $phrase -Because $phrase }
        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Match 'TECHSTRAP_PORTAL_LANDING=Products'
    }
}

Describe 'PHASE-11g theme packs and skins (D-053)' {
    BeforeAll {
        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $script:Spec = Get-RepoText 'docs/architecture/PHASE-11g-theme-packs.md'
    }

    It 'records D-053 with the owner decisions, rulings and alternatives' {
        $script:Log | Should -Match '(?m)^## D-053: PHASE-11g/11h: Portal theme packs and per-product skins'
        $entry = ($script:Log -split '(?m)^## D-053: ')[1]
        foreach ($phrase in 'structured tokens', 'SkinResolver', 'skin-contrast-invalid', 'Classic, Slate, Paper, Contrast, Midnight', 'GET/PUT api/settings/site', 'Contracts 0.4.0', 'Alternatives Considered', 'Approved on:** 2026-10-10') {
            $entry | Should -Match ([regex]::Escape($phrase)) -Because $phrase
        }
    }

    It 'amends D-031, D-045, BRAND.md and the UX brief with Amended by D-053 lines' {
        ([regex]::Matches($script:Log, 'Amended by D-053')).Count | Should -BeGreaterOrEqual 3
        (Get-RepoText 'docs/BRAND.md') | Should -Match 'Amended by D-053'
        (Get-RepoText 'docs/architecture/UX-BRIEF-portal.md') | Should -Match 'Amended by D-053'
    }

    It 'has the 11g spec with eight tasks and its roadmap and discovery rows' {
        foreach ($n in 1..8) {
            $script:Spec | Should -Match ('- \[[ x]\] \*\*P11g-T' + $n.ToString('00') + '\*\*') -Because "P11g-T$($n.ToString('00')) exists"
        }
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 11g \|.*D-053.*\| D-053 recorded; 11g'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 11g \|.*\| D-053 recorded; 11g'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| P11g-T08 \|'
    }
}
