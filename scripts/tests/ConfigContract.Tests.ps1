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
    # A key containing one of these words is secret-shaped: its committed value must be blank (a number or a flag cannot be a secret, so PublicKeyPermitLimit is fine).
    # OPENTELEMETRY__HEADERS is added because the OTLP headers carry a token. KeyRingPath is a directory, not a key.
    $script:SecretWords = 'password|secret|dsn|token|key|(^|__)headers$'

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
            $unexpected = @($blank | Where-Object { $_.Key -notin $script:BlankKeys[$Name] } | ForEach-Object { $_.Key })
            $unexpected | Should -BeNullOrEmpty -Because "$path leaves these blank, but only a blank-valid setting may be: $($unexpected -join ', ')"
            @($blank | Where-Object { $_.Key -match '__\d+$' }) | Should -BeNullOrEmpty -Because "$path has a blank array element; comment the key out instead"
        }
    }

    It 'no committed file holds a non-blank secret-shaped value' {
        $problems = @()
        foreach ($path in $script:Example, $script:Template) {
            foreach ($entry in (Get-EnvEntries -Path $path)) {
                $isNumberOrFlag = $entry.Value -match '^(\d+(\.\d+)?|true|false)$'
                if ($entry.Key -match $script:SecretWords -and $entry.Key -notmatch 'KEYRINGPATH' -and $entry.Value -ne '' -and -not $isNumberOrFlag) { $problems += "$path $($entry.Key)" }
            }
        }
        foreach ($leaf in (Get-AppsettingsLeaves -HostName $Name)) {
            $isNumberOrFlag = $leaf.Value -match '^(\d+(\.\d+)?|true|false)$'
            if ($leaf.Key -match $script:SecretWords -and $leaf.Key -notmatch 'KEYRINGPATH' -and $leaf.Value -and -not $isNumberOrFlag) { $problems += "appsettings.json $($leaf.Key)" }
        }
        $problems | Should -BeNullOrEmpty
    }

    It 'a connection string in a committed env file carries no password but replace-me' {
        foreach ($path in $script:Example, $script:Template) {
            foreach ($entry in (Get-EnvEntries -Path $path | Where-Object { $_.Key -eq 'CONNECTIONSTRINGS__TECHSTRAP' })) {
                if ($entry.Value -ne '') { $entry.Value | Should -Match 'Password=replace-me(;|$)' -Because "$path must not hold a real password" }
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
        foreach ($host_ in 'Api', 'Admin', 'Portal') {
            $path = Join-Path $script:RepoRoot 'src' "TechStrap.$host_" 'appsettings.Development.json'
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
