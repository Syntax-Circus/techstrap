BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Send-TestTicket.ps1'
}

Describe 'Send-TestTicket' {
    BeforeAll { $script = $script:ScriptPath }

    It 'posts JSON to the intake endpoint with the key and an idempotency key' {
        Mock Invoke-RestMethod { [pscustomobject]@{ ticketNumber = 'ORB-1'; viewUrl = 'http://localhost:8082/t/x'; warnings = @() } }
        & $script -BaseUrl 'http://localhost:8080' -ApiKey 'tsk_example' -Email 'ann@example.com' -Subject 'Hi' -Body 'Hello' | Out-Null
        Should -Invoke Invoke-RestMethod -Times 1 -ParameterFilter {
            $Uri -eq 'http://localhost:8080/api/intake/tickets' -and $Method -eq 'Post' -and
            $Headers['X-Api-Key'] -eq 'tsk_example' -and $Headers['Idempotency-Key'] -and $ContentType -eq 'application/json'
        }
    }
    It 'prints the request without sending it under -DryRun' {
        Mock Invoke-RestMethod { throw 'must not be called' }
        $output = & $script -BaseUrl 'http://localhost:8080' -ApiKey 'tsk_example' -DryRun | Out-String
        Should -Invoke Invoke-RestMethod -Times 0
        $output | Should -Match ([regex]::Escape('POST http://localhost:8080/api/intake/tickets'))
    }
    It 'never prints the API key' {
        Mock Invoke-RestMethod { [pscustomobject]@{ ticketNumber = 'ORB-1'; viewUrl = $null; warnings = @() } }
        $normal = & $script -ApiKey 'tsk_canaryKeyValue' | Out-String
        $dry = & $script -ApiKey 'tsk_canaryKeyValue' -DryRun | Out-String
        $normal | Should -Not -Match 'tsk_canaryKeyValue'
        $dry | Should -Not -Match 'tsk_canaryKeyValue'
    }
}
