BeforeDiscovery {
    $script:Cases = @(
        @{ Readme = 'src/TechStrap.Client/README.md'; Source = 'samples/TechStrap.Client.Samples.Console/Program.cs'; Region = 'client-register' }
        @{ Readme = 'src/TechStrap.Client/README.md'; Source = 'samples/TechStrap.Client.Samples.Console/Program.cs'; Region = 'client-submit' }
        @{ Readme = 'src/TechStrap.Client/README.md'; Source = 'samples/TechStrap.Client.Samples.Console/Program.cs'; Region = 'client-errors' }
        @{ Readme = 'src/TechStrap.Client.Maui/README.md'; Source = 'tests/TechStrap.Client.Maui.Tests/Snippets/MauiReadmeSnippets.cs'; Region = 'maui-register' }
        @{ Readme = 'src/TechStrap.Client.Maui/README.md'; Source = 'tests/TechStrap.Client.Maui.Tests/Snippets/MauiReadmeSnippets.cs'; Region = 'maui-submit' }
    )
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    # Returns a hashtable: region name (from "#region readme:<name>") -> body, dedented by the common leading whitespace.
    function Get-ReadmeRegions {
        param([Parameter(Mandatory)][string]$Path)
        $lines = (Get-Content -LiteralPath $Path -Raw) -replace "`r`n", "`n" -split "`n"
        $regions = @{}
        $name = $null
        $body = $null
        foreach ($line in $lines) {
            if ($line -match '^\s*#region readme:(?<n>\S+)\s*$') {
                if ($null -ne $name) { throw "Region '$name' is not closed before region '$($Matches.n)' in $Path." }
                if ($regions.ContainsKey($Matches.n)) { throw "Duplicate region '$($Matches.n)' in $Path." }
                $name = $Matches.n
                $body = [System.Collections.Generic.List[string]]::new()
            }
            elseif ($line -match '^\s*#endregion\b') {
                if ($null -eq $name) { continue }
                $indents = @($body | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_.Length - $_.TrimStart().Length })
                $min = if ($indents.Count -gt 0) { ($indents | Measure-Object -Minimum).Minimum } else { 0 }
                $regions[$name] = (($body | ForEach-Object { if ($_.Length -ge $min) { $_.Substring($min) } else { $_.TrimStart() } }) -join "`n")
                $name = $null
            }
            elseif ($null -ne $name) {
                $body.Add($line)
            }
        }
        if ($null -ne $name) { throw "Region '$name' has no #endregion in $Path." }
        return $regions
    }

    function Test-ReadmeContains {
        param([string]$ReadmeText, [string]$Body)
        return $ReadmeText.Replace("`r`n", "`n").Contains("``````csharp`n$Body`n``````")
    }
}

Describe 'Get-ReadmeRegions' {
    It 'returns every region by name' {
        $file = Join-Path $TestDrive 'two.cs'
        Set-Content -LiteralPath $file -Value "var a = 1;`n#region readme:one`nA();`n#endregion`n#region readme:two`nB();`nC();`n#endregion`n"
        $r = Get-ReadmeRegions -Path $file
        $r.Count | Should -Be 2
        $r['one'] | Should -Be 'A();'
        $r['two'] | Should -Be "B();`nC();"
    }

    It 'dedents by the common indentation and keeps nested indentation' {
        $file = Join-Path $TestDrive 'nested.cs'
        $text = "class X {`r`n    void M()`r`n    {`r`n        #region readme:n`r`n        if (a)`r`n        {`r`n            B();`r`n        }`r`n`r`n        #endregion`r`n    }`r`n}`r`n"
        [System.IO.File]::WriteAllText($file, $text)
        $r = Get-ReadmeRegions -Path $file
        $r['n'] | Should -Be "if (a)`n{`n    B();`n}`n"
    }

    It 'throws when a region has no #endregion' {
        $file = Join-Path $TestDrive 'open.cs'
        Set-Content -LiteralPath $file -Value "#region readme:open`nA();`n"
        { Get-ReadmeRegions -Path $file } | Should -Throw '*no #endregion*'
    }

    It 'throws on a duplicate region name' {
        $file = Join-Path $TestDrive 'dup.cs'
        Set-Content -LiteralPath $file -Value "#region readme:x`nA();`n#endregion`n#region readme:x`nB();`n#endregion`n"
        { Get-ReadmeRegions -Path $file } | Should -Throw '*Duplicate region*'
    }
}

Describe 'README code blocks match the compiled snippets' {
    It 'finds <Region> of <Source> in <Readme>' -ForEach $script:Cases {
        $regions = Get-ReadmeRegions -Path (Join-Path $script:RepoRoot $Source)
        $regions.ContainsKey($Region) | Should -BeTrue -Because "$Source needs #region readme:$Region"
        $readme = Get-Content -LiteralPath (Join-Path $script:RepoRoot $Readme) -Raw
        Test-ReadmeContains -ReadmeText $readme -Body $regions[$Region] | Should -BeTrue -Because "$Readme must contain the $Region snippet in a csharp fence, exactly"
    }

    It 'detects a README whose code block drifted from the snippet' {
        $source = Join-Path $TestDrive 'snip.cs'
        Set-Content -LiteralPath $source -Value "#region readme:r`nA();`nB();`n#endregion`n"
        $body = (Get-ReadmeRegions -Path $source)['r']
        Test-ReadmeContains -ReadmeText "text`n``````csharp`nA();`nB();`n```````n" -Body $body | Should -BeTrue
        Test-ReadmeContains -ReadmeText "text`n``````csharp`nA();`nB2();`n```````n" -Body $body | Should -BeFalse
    }
}
