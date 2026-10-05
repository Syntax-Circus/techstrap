BeforeDiscovery {
    $script:HostCases = @(
        @{ Name = 'Api' }
        @{ Name = 'Worker' }
        @{ Name = 'Admin' }
        @{ Name = 'Portal' }
    )
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    # --- The documented exclusion list -------------------------------------------------------------------------------------------------------------------
    # Keys are compared case-insensitively in SECTION__KEY form (appsettings "A:B:C" is A__B__C; an array element is __0, and every index counts as __0, because
    # the settings are the same one setting however many entries an operator gives it). An empty JSON array counts as its __0 key.
    # These appsettings keys are not settings an operator sets, so no .env file lists them:
    $script:ExcludedPrefixes = @(
        'SASSCOMPILER'                     # build-time stylesheet compiler (Admin, Portal)
        'SERILOG__USING'                   # sink assembly list
        'SERILOG__WRITETO'                 # sink definitions
        'SERILOG__MINIMUMLEVEL__OVERRIDE'  # keys hold dots (Microsoft.AspNetCore) that an environment variable name cannot carry
    )
    # Not listed anywhere, on purpose (each is either set in code or must not be listed):
    #   SecurityHeaders:ContentSecurityPolicy and PathOverrides  the Content-Security-Policy is set per host in code (TechStrapCsp), overriding any setting
    #   Auth:Scopes                       an array with a non-empty library default: binding appends to the default, so listing the defaults would duplicate them
    #   Auth:TokenCache, Storage:S3, Storage:Local:PublicBaseUrl  library settings TechStrap does not use
    #   AutoClose:Days                    the section form of TECHSTRAP_AUTOCLOSE_DAYS, which wins
    # Compose owns these per host (deploy/docker-compose.yml environment:), so the deploy env templates must not list them:
    $script:ComposeOwned = @{
        Api    = @('STORAGE__LOCAL__ROOTPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
        Worker = @()
        Admin  = @('API__BASEURL', 'DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
        Portal = @('DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
    }
    # Names compose sets that are not appsettings keys (the host environment and the switch that stops a container loading a .env file):
    $script:ComposeNonSettings = @('ASPNETCORE_ENVIRONMENT', 'DOTENV__ENABLED')
    # Development only, so the deploy env templates must not list them:
    $script:DevelopmentOnly = @{ Api = @('TECHSTRAP_SEED_DEV_DATA'); Worker = @(); Admin = @(); Portal = @() }
    # The only keys whose committed value may be blank in appsettings.json. Every number, flag and enum carries its real default instead, because a blank
    # value fails binding ("Failed to convert configuration value '' to type Int32"); the nullable settings TlsMode and TotalSendTimeout bind a blank as null.
    $script:CommonBlank = @('SENTRY__DSN', 'SENTRY__ENVIRONMENT', 'OPENTELEMETRY__OTLPENDPOINT', 'OPENTELEMETRY__HEADERS', 'OPENTELEMETRY__SERVICENAME', 'OPENTELEMETRY__SERVICEVERSION', 'OPENTELEMETRY__ENVIRONMENT')
    $script:BlankKeys = @{
        Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
        Worker = $script:CommonBlank + @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__USERNAME', 'EMAIL__SMTP__PASSWORD', 'EMAIL__SMTP__DEFAULTFROM', 'EMAIL__SMTP__TLSMODE', 'EMAIL__SMTP__TOTALSENDTIMEOUT', 'EMAILOUTBOX__WORKERID')
        Admin  = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET', 'DATAPROTECTION__KEYRINGPATH')
        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'DATAPROTECTION__KEYRINGPATH')
    }
    # A key containing one of these words is secret-shaped: its committed value must be blank, whatever the value looks like (a numeric password is still a password).
    # OPENTELEMETRY__HEADERS is added because the OTLP headers carry a token. KeyRingPath is a directory, not a key.
    $script:SecretWords = 'password|secret|dsn|token|key|(^|__)headers$'
    # The only secret-shaped names that carry a plain number: the rate limits (PublicKeyPermitLimit, TokenAccessWindowSeconds), which are named after what they limit.
    $script:NumericSecretShapedKeys = '^RATELIMITING__.*(PERMITLIMIT|WINDOWSECONDS)$'
    # An array element that may be blank in a deploy template, because the host validates it itself and rejects a blank value (the Api audience), so it is a required key like
    # AUTHORITY. A blank TrustedProxy element is different: it counts as configured, so those stay commented out.
    $script:BlankTemplateElements = @('AUTHENTICATION__JWTBEARER__AUDIENCES__0')
    # Deploy-template values that intentionally differ from appsettings.json (every other non-blank template value must equal the appsettings value). Each needs a reason.
    $script:ValueDeviations = @{
        Api    = @{}
        Worker = @{
            EMAIL__SMTP__MAXRETRYATTEMPTS = 'production value from the old compose: one retry (the library default is 3)'
            EMAIL__SMTP__TLSMODE          = 'production value: explicit StartTls (appsettings leaves it blank, which falls back to the legacy STARTTLS flag)'
            EMAIL__SMTP__RETRYMODE        = 'production value: retry only transient failures (the library default is Legacy)'
            EMAIL__SMTP__TOTALSENDTIMEOUT = 'production value: a 30 s overall send deadline (appsettings leaves it blank, meaning none)'
        }
        Admin  = @{}
        Portal = @{}
    }

    # True when a committed value must not be there: a secret-shaped key with any value (except the numeric rate limits).
    function Test-SecretValue {
        param([string]$Key, [string]$Value)
        if ($Value -eq '') { return $false }
        if ($Key -match 'KEYRINGPATH') { return $false }
        if ($Key -match $script:SecretWords) {
            $isNumberOrFlag = $Value -match '^(\d+(\.\d+)?|true|false)$'
            if (-not ($isNumberOrFlag -and $Key -match $script:NumericSecretShapedKeys)) { return $true }
        }
        return $false
    }

    # True when every Password or Pwd in a connection string is replace-me, wherever it appears.
    function Test-ConnectionStringPassword {
        param([string]$Value)
        foreach ($match in [regex]::Matches($Value, '(?i)\b(password|pwd)\s*=\s*([^;]*)')) {
            if ($match.Groups[2].Value.Trim() -ne 'replace-me') { return $false }
        }
        return $true
    }

    function Test-ValuesEqual {
        param([string]$Left, [string]$Right)
        $a = 0.0; $b = 0.0
        $style = [System.Globalization.NumberStyles]::Float
        if ([double]::TryParse($Left, $style, [cultureinfo]::InvariantCulture, [ref]$a) -and [double]::TryParse($Right, $style, [cultureinfo]::InvariantCulture, [ref]$b)) { return $a -eq $b }
        return $Left -ieq $Right
    }

    function ConvertTo-KeyName {
        param([string]$Path)
        return ($Path -replace ':', '__').ToUpperInvariant()
    }

    function ConvertTo-IndexlessKey {
        param([string]$Key)
        return ($Key -replace '__\d+(?=__|$)', '__0')
    }

    # Every leaf of a JSON object as @{ Key = 'A__B__C'; Value = '...' }; an array element is __<index>, and an empty array is its __0 key with a null value.
    function Get-JsonLeaves {
        param($Node, [string]$Prefix = '')
        $leaves = @()
        if ($Node -is [System.Management.Automation.PSCustomObject]) {
            foreach ($property in $Node.PSObject.Properties) {
                $name = if ($Prefix) { "$Prefix`__$($property.Name)" } else { $property.Name }
                $leaves += Get-JsonLeaves -Node $property.Value -Prefix $name
            }
        }
        elseif ($Node -is [System.Array]) {
            if ($Node.Count -eq 0) { $leaves += [pscustomobject]@{ Key = "$Prefix`__0".ToUpperInvariant(); Value = $null; Empty = $true } }
            for ($i = 0; $i -lt $Node.Count; $i++) {
                $leaves += Get-JsonLeaves -Node $Node[$i] -Prefix "$Prefix`__$i"
            }
        }
        else {
            $leaves += [pscustomobject]@{ Key = $Prefix.ToUpperInvariant(); Value = if ($null -eq $Node) { '' } else { [string]$Node }; Empty = $false }
        }
        return $leaves
    }

    function Test-Excluded {
        param([string]$Key)
        foreach ($prefix in $script:ExcludedPrefixes) { if ($Key -eq $prefix -or $Key.StartsWith("$prefix`__")) { return $true } }
        return $false
    }

    function Get-AppsettingsLeaves {
        param([string]$HostName, [switch]$IncludeDevelopment)
        $directory = Join-Path $script:RepoRoot 'src' "TechStrap.$HostName"
        $files = @(Join-Path $directory 'appsettings.json')
        if ($IncludeDevelopment -and (Test-Path (Join-Path $directory 'appsettings.Development.json'))) { $files += Join-Path $directory 'appsettings.Development.json' }
        $leaves = foreach ($file in $files) { Get-JsonLeaves -Node (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -NoEnumerate) }
        return @($leaves | Where-Object { -not (Test-Excluded $_.Key) })
    }

    function Get-AppsettingsKeys {
        param([string]$HostName)
        return @(Get-AppsettingsLeaves -HostName $HostName -IncludeDevelopment | ForEach-Object { ConvertTo-IndexlessKey $_.Key } | Sort-Object -Unique)
    }

    # Every KEY=value line of an env file, commented out or not. A commented key is "# KEY=" with one space; a line like "#   Host=db" is prose.
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

    function Get-EnvKeys {
        param([string]$Path)
        return @(Get-EnvEntries -Path $Path | ForEach-Object { ConvertTo-IndexlessKey $_.Key } | Sort-Object -Unique)
    }

    # The environment: keys of every service of a compose file, by service (a small reader for the 2/4/6-space layout these files use).
    function Get-ComposeEnvironmentKeys {
        param([string]$File)
        $result = @{}
        $service = $null
        $inEnvironment = $false
        foreach ($line in (Get-Content -LiteralPath (Join-Path $script:RepoRoot $File))) {
            if ($line -match '^  (?<name>[a-z][a-z0-9-]*):\s*$') { $service = $Matches['name']; $inEnvironment = $false; continue }
            if ($line -match '^    environment:\s*$') { $inEnvironment = $true; $result[$service] = @(); continue }
            if ($inEnvironment -and $line -match '^      (?<key>[A-Za-z_][A-Za-z0-9_]*):') { $result[$service] += $Matches['key'].ToUpperInvariant(); continue }
            if ($inEnvironment -and $line -match '^\s*#') { continue }
            if ($inEnvironment -and $line -match '^\s{0,4}\S') { $inEnvironment = $false }
        }
        return $result
    }

    function Test-GitIgnored {
        param([string]$RelativePath)
        & git -C $script:RepoRoot check-ignore -q -- $RelativePath
        return ($LASTEXITCODE -eq 0)
    }
}

Describe 'the config contract of <Name>' -ForEach $script:HostCases {
    BeforeAll {
        $script:HostName = $Name
        $script:Example = Join-Path $script:RepoRoot 'src' "TechStrap.$Name" '.env.example'
        $script:Template = Join-Path $script:RepoRoot 'deploy' ".env.$($Name.ToLowerInvariant()).example"
        $script:SettingKeys = Get-AppsettingsKeys -HostName $Name
    }

    It 'the scan sees the settings (a vacuous pass would hide a broken reader)' {
        $script:SettingKeys.Count | Should -BeGreaterThan 15
        $script:SettingKeys | Should -Contain 'SENTRY__DSN'
        $script:SettingKeys | Should -Contain 'SERILOG__MINIMUMLEVEL__DEFAULT'
    }

    It 'appsettings.json plus appsettings.Development.json and .env.example list the same keys, both ways' {
        $example = Get-EnvKeys -Path $script:Example
        $missing = @($script:SettingKeys | Where-Object { $_ -notin $example })
        $extra = @($example | Where-Object { $_ -notin $script:SettingKeys })
        $missing | Should -BeNullOrEmpty -Because "src/TechStrap.$Name/.env.example is missing: $($missing -join ', ')"
        $extra | Should -BeNullOrEmpty -Because "src/TechStrap.$Name/.env.example lists keys the host does not read: $($extra -join ', ')"
    }

    It 'the deploy env template lists the same keys minus the ones compose owns and the Development-only ones, both ways' {
        $expected = @($script:SettingKeys | Where-Object { $_ -notin $script:ComposeOwned[$Name] -and $_ -notin $script:DevelopmentOnly[$Name] })
        $template = Get-EnvKeys -Path $script:Template
        $missing = @($expected | Where-Object { $_ -notin $template })
        $extra = @($template | Where-Object { $_ -notin $expected })
        $missing | Should -BeNullOrEmpty -Because "deploy/.env.$($Name.ToLowerInvariant()).example is missing: $($missing -join ', ')"
        $extra | Should -BeNullOrEmpty -Because "deploy/.env.$($Name.ToLowerInvariant()).example lists keys the host does not read or compose owns: $($extra -join ', ')"
    }

    It 'no env file lists a key twice (commented or not)' {
        foreach ($path in $script:Example, $script:Template) {
            $names = @(Get-EnvEntries -Path $path | ForEach-Object { $_.Key })
            $duplicates = @($names | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
            $duplicates | Should -BeNullOrEmpty -Because "$path repeats: $($duplicates -join ', ')"
        }
    }

    It 'appsettings.json is blank only where blank is valid, and nowhere else' {
        $blank = @(Get-AppsettingsLeaves -HostName $Name | Where-Object { -not $_.Empty -and $_.Value -eq '' } | ForEach-Object { $_.Key } | Sort-Object -Unique)
        $allowed = @($script:BlankKeys[$Name] | Sort-Object -Unique)
        $unexpected = @($blank | Where-Object { $_ -notin $allowed })
        $absent = @($allowed | Where-Object { $_ -notin $blank })
        $unexpected | Should -BeNullOrEmpty -Because "a blank number, flag or enum fails binding at start; these are blank in appsettings.json: $($unexpected -join ', ')"
        $absent | Should -BeNullOrEmpty -Because "these are expected blank but carry a value: $($absent -join ', ')"
    }

    It 'no array element is blank in appsettings.json (an empty array stays [], so a blank trusted network cannot defeat the Production check)' {
        $blankElements = @(Get-AppsettingsLeaves -HostName $Name | Where-Object { -not $_.Empty -and $_.Value -eq '' -and $_.Key -match '__\d+$' })
        $blankElements | Should -BeNullOrEmpty
    }

    It 'a blank value in .env.example or the deploy template is on the blank list, and an array element is never left blank' {
        foreach ($path in $script:Example, $script:Template) {
            $blank = @(Get-EnvEntries -Path $path | Where-Object { -not $_.Commented -and $_.Value -eq '' })
            $unexpected = @($blank | Where-Object { $_.Key -notin $script:BlankKeys[$Name] -and -not ($path -eq $script:Template -and $_.Key -in $script:BlankTemplateElements) } | ForEach-Object { $_.Key })
            $unexpected | Should -BeNullOrEmpty -Because "$path leaves these blank, but only a blank-valid setting may be: $($unexpected -join ', ')"
            @($blank | Where-Object { $_.Key -match '__\d+$' -and -not ($path -eq $script:Template -and $_.Key -in $script:BlankTemplateElements) }) | Should -BeNullOrEmpty -Because "$path has a blank array element; comment the key out instead"
        }
    }

    It 'no committed file holds a non-blank secret-shaped value' {
        $problems = @()
        foreach ($path in $script:Example, $script:Template) {
            foreach ($entry in (Get-EnvEntries -Path $path)) {
                if (Test-SecretValue -Key $entry.Key -Value $entry.Value) { $problems += "$path $($entry.Key)" }
            }
        }
        foreach ($leaf in (Get-AppsettingsLeaves -HostName $Name)) {
            if (Test-SecretValue -Key $leaf.Key -Value $leaf.Value) { $problems += "appsettings.json $($leaf.Key)" }
        }
        $problems | Should -BeNullOrEmpty
    }

    It 'every non-blank value in the deploy template equals the appsettings.json value, except the documented deviations (which must really differ)' {
        $leaves = @{}
        foreach ($leaf in (Get-AppsettingsLeaves -HostName $Name)) { $leaves[(ConvertTo-IndexlessKey $leaf.Key)] = $leaf }
        $deviations = $script:ValueDeviations[$Name]
        $wrong = @()
        foreach ($entry in (Get-EnvEntries -Path $script:Template | Where-Object { -not $_.Commented -and $_.Value -ne '' })) {
            $key = ConvertTo-IndexlessKey $entry.Key
            if (-not $leaves.ContainsKey($key)) { continue }
            $same = Test-ValuesEqual -Left $entry.Value -Right $leaves[$key].Value
            if ($deviations.ContainsKey($key)) { if ($same) { $wrong += "$key is listed as a deviation but equals appsettings.json ($($entry.Value))" } }
            elseif (-not $same) { $wrong += "$key is '$($entry.Value)' in the template but '$($leaves[$key].Value)' in appsettings.json" }
        }
        $wrong | Should -BeNullOrEmpty -Because 'a template value is the code default unless it is on the documented deviation list'
    }

    It 'every documented deviation has a reason' {
        foreach ($key in $script:ValueDeviations[$Name].Keys) { $script:ValueDeviations[$Name][$key] | Should -Not -BeNullOrEmpty -Because $key }
    }

    It 'a connection string in a committed env file carries no password but replace-me' {
        foreach ($path in $script:Example, $script:Template) {
            foreach ($entry in (Get-EnvEntries -Path $path | Where-Object { $_.Key -eq 'CONNECTIONSTRINGS__TECHSTRAP' })) {
                if ($entry.Value -ne '') { Test-ConnectionStringPassword -Value $entry.Value | Should -BeTrue -Because "$path must hold no Password or Pwd other than replace-me, wherever it appears in the string" }
            }
        }
    }

    It 'the deploy env template has the key-only header and the sync rule' {
        $text = Get-Content -LiteralPath $script:Template -Raw
        $text | Should -Match "(?m)^# Key-only template for the $Name container: copy to /etc/techstrap/<uat\|production>/\.env\.$($Name.ToLowerInvariant())\r?$"
        $text | Should -Match "(?m)^# Keep this file in sync with src/TechStrap\.$Name/appsettings\.json and src/TechStrap\.$Name/\.env\.example"
        $text | Should -Match '(?m)^# -- .+ \[[A-Za-z, ]+\]'
    }

    It 'the .env.example header says to copy it to .env.local, loaded in Development only' {
        $text = Get-Content -LiteralPath $script:Example -Raw
        $text | Should -Match 'Copy to \.env\.local in this directory \(gitignored\)\. SyntaxCircus\.DotEnv loads \.env then \.env\.local in Development only\.'
        $text | Should -Match 'Use SECTION__KEY \(ALL_CAPS\) naming'
    }
}

Describe 'the config contract of the committed files that belong to no single host' {
    It 'the Admin Development placeholders are clearly fake' {
        $development = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'src' 'TechStrap.Admin' 'appsettings.Development.json') -Raw | ConvertFrom-Json
        ([uri]$development.Auth.Authority).Host | Should -Match '\.invalid$'
        $development.Auth.ClientSecret | Should -Be 'not-configured'
    }

    It 'no Development file holds a secret-shaped value other than a clear placeholder' {
        foreach ($path in (Get-ChildItem -Path (Join-Path $script:RepoRoot 'src') -Filter 'appsettings.Development.json' -Recurse -Depth 2 | ForEach-Object { $_.FullName })) {
            foreach ($leaf in (Get-JsonLeaves -Node (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -NoEnumerate))) {
                if ($leaf.Key -match $script:SecretWords -and $leaf.Value) { $leaf.Value | Should -Match '^(not-configured|.*\.invalid.*)$' -Because "$path $($leaf.Key)" }
            }
        }
    }

    It 'the deploy env templates warn that the Postgres password must be safe inside a connection string' {
        foreach ($app in 'api', 'worker') {
            $text = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$app.example") -Raw
            $text | Should -Match 'use only letters, digits and - _ \. ~'
            $text | Should -Match 'openssl rand -hex 24'
        }
    }
}

Describe 'the config contract of the local compose' {
    It 'every local compose environment: key is a known setting of its host' {
        $environment = Get-ComposeEnvironmentKeys -File 'docker-compose.yml'
        foreach ($name in 'Api', 'Worker', 'Admin', 'Portal') {
            $service = $name.ToLowerInvariant()
            $environment[$service] | Should -Not -BeNullOrEmpty
            $known = Get-AppsettingsKeys -HostName $name
            $unknown = @($environment[$service] | ForEach-Object { ConvertTo-IndexlessKey $_ } | Where-Object { $_ -notin $known -and $_ -notin $script:ComposeNonSettings })
            $unknown | Should -BeNullOrEmpty -Because "docker-compose.yml $service sets keys $name does not read: $($unknown -join ', ')"
        }
    }

    It 'the local compose no longer overrides the Admin sign-in or the group keys (the clash between .env.local and compose is gone)' {
        $admin = (Get-ComposeEnvironmentKeys -File 'docker-compose.yml')['admin']
        @($admin | Where-Object { $_ -like 'AUTH__*' -or $_ -like 'TECHSTRAP_*GROUP*' -or $_ -like 'TECHSTRAP_GROUP_CLAIM_TYPE' }) | Should -BeNullOrEmpty
        @($admin | Sort-Object) | Should -Be @('API__BASEURL', 'ASPNETCORE_ENVIRONMENT', 'DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
    }

    It 'the root .env.example documents the local compose inputs and nothing else' {
        $keys = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot '.env.example') | ForEach-Object { $_.Key } | Sort-Object)
        $keys | Should -Be @('REVERSE_PROXY_CIDR', 'TECHSTRAP_MAILPIT_PORT', 'TECHSTRAP_SEED_DEV_DATA', 'TECHSTRAP_SUBNET')
        $compose = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'docker-compose.yml') -Raw
        foreach ($key in $keys) { $compose | Should -Match ('\$\{' + $key + ':-') }
    }
}

Describe 'the config contract of the deploy compose' {
    It 'the deploy compose environment: lists exactly what compose owns for each host, and every key is a known setting' {
        $environment = Get-ComposeEnvironmentKeys -File 'deploy/docker-compose.yml'
        foreach ($name in 'Api', 'Worker', 'Admin', 'Portal') {
            $service = $name.ToLowerInvariant()
            $keys = @($environment[$service] | ForEach-Object { ConvertTo-IndexlessKey $_ } | Sort-Object -Unique)
            $expected = @($script:ComposeOwned[$name] + $script:ComposeNonSettings | Sort-Object -Unique)
            $keys | Should -Be $expected -Because "deploy/docker-compose.yml $service environment:"
            $known = Get-AppsettingsKeys -HostName $name
            @($keys | Where-Object { $_ -notin $known -and $_ -notin $script:ComposeNonSettings }) | Should -BeNullOrEmpty
        }
    }

    It 'the UAT and production compose input templates set the same variable names' {
        $uat = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' '.env.uat.example') | Where-Object { -not $_.Commented } | ForEach-Object { $_.Key } | Sort-Object)
        $production = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' '.env.production.example') | Where-Object { -not $_.Commented } | ForEach-Object { $_.Key } | Sort-Object)
        $uat.Count | Should -BeGreaterThan 10
        $uat | Should -Be $production
    }

    It 'the compose input templates set every variable the deploy compose requires, and never latest' {
        $compose = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' 'docker-compose.yml') -Raw
        $required = [regex]::Matches($compose, '\$\{(?<name>[A-Z][A-Z0-9_]*):\?') | ForEach-Object { $_.Groups['name'].Value } | Sort-Object -Unique
        $required.Count | Should -BeGreaterThan 8
        foreach ($file in '.env.uat.example', '.env.production.example') {
            $entries = Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' $file) | Where-Object { -not $_.Commented }
            foreach ($name in $required) { $entries.Key | Should -Contain $name -Because "$file must set $name" }
            foreach ($image in ($entries | Where-Object { $_.Key -like 'TECHSTRAP_*_IMAGE' })) {
                $image.Value | Should -Match '^ghcr\.io/syntax-circus/techstrap-(api|worker|admin|portal):\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$' -Because "$file pins an explicit release tag"
            }
        }
    }

    It 'the UAT and production compose input templates name different projects' {
        $uat = Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' '.env.uat.example') | Where-Object { -not $_.Commented -and $_.Key -eq 'TECHSTRAP_PROJECT' }
        $production = Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' '.env.production.example') | Where-Object { -not $_.Commented -and $_.Key -eq 'TECHSTRAP_PROJECT' }
        $uat.Value | Should -Not -BeNullOrEmpty
        $production.Value | Should -Not -BeNullOrEmpty
        $uat.Value | Should -Not -Be $production.Value
    }

    It 'each compose input template pins all four images to one tag (<file>)' -ForEach @(
        @{ file = '.env.uat.example' }
        @{ file = '.env.production.example' }
    ) {
        $images = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' $file) | Where-Object { -not $_.Commented -and $_.Key -like 'TECHSTRAP_*_IMAGE' })
        $images.Count | Should -Be 4
        @($images | ForEach-Object { ($_.Value -split ':')[-1] } | Sort-Object -Unique).Count | Should -Be 1
    }

    It 'every deploy env example warns that raw format keeps inline comments and quotes' -ForEach @(
        @{ file = '.env.uat.example' }, @{ file = '.env.production.example' }, @{ file = '.env.api.example' }
        @{ file = '.env.worker.example' }, @{ file = '.env.admin.example' }, @{ file = '.env.portal.example' }
    ) {
        (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' $file) -Raw) | Should -Match 'No inline comments: with raw format, `# \.\.\.` after a value becomes part of the value\. No surrounding quotes\.'
    }

    It 'git ignores a filled env file and tracks every example' -ForEach @(
        @{ Path = 'deploy/.env.uat.local'; Ignored = $true }
        @{ Path = 'deploy/.env.production.local'; Ignored = $true }
        @{ Path = 'deploy/.env.api'; Ignored = $true }
        @{ Path = '.env'; Ignored = $true }
        @{ Path = 'src/TechStrap.Api/.env.local'; Ignored = $true }
        @{ Path = '.env.example'; Ignored = $false }
        @{ Path = 'deploy/.env.uat.example'; Ignored = $false }
        @{ Path = 'deploy/.env.production.example'; Ignored = $false }
        @{ Path = 'deploy/.env.api.example'; Ignored = $false }
        @{ Path = 'deploy/.env.worker.example'; Ignored = $false }
        @{ Path = 'deploy/.env.admin.example'; Ignored = $false }
        @{ Path = 'deploy/.env.portal.example'; Ignored = $false }
        @{ Path = 'src/TechStrap.Portal/.env.example'; Ignored = $false }
    ) {
        Test-GitIgnored $Path | Should -Be $Ignored
    }

    It 'the old root production compose files and env example are gone' {
        foreach ($file in 'docker-compose.production.yml', 'docker-compose.uat.yml', '.env.production.example') {
            Test-Path (Join-Path $script:RepoRoot $file) | Should -BeFalse
        }
    }
}
