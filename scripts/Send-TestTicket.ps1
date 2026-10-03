<#
.SYNOPSIS
Submits one test ticket to a running TechStrap API through the API-key intake endpoint.
.DESCRIPTION
Defaults target the local compose stack and the development Orbitly trusted key (seeded when TECHSTRAP_SEED_DEV_DATA=true;
not a secret, see docs/development/DEV-DATA.md). The confirmation email lands in Mailpit at http://localhost:8025.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:8080',
    [string] $ApiKey = 'tsk_devOrbitlyServerKeyNotASecret00000000000000',
    [string] $Email = 'test.customer@example.com',
    [string] $Name = 'Test Customer',
    [string] $Subject = 'Test ticket from Send-TestTicket.ps1',
    [string] $Body = 'This is a test ticket.',
    [string] $IdempotencyKey = [guid]::NewGuid().ToString(),
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$uri = '{0}/api/intake/tickets' -f $BaseUrl.TrimEnd('/')
$payload = @{ email = $Email; name = $Name; subject = $Subject; body = $Body } | ConvertTo-Json -Compress
$headers = @{ 'X-Api-Key' = $ApiKey; 'Idempotency-Key' = $IdempotencyKey }

if ($DryRun) {
    Write-Output "POST $uri"
    Write-Output "Idempotency-Key: $IdempotencyKey"
    Write-Output $payload
    return
}

$response = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -ContentType 'application/json' -Body $payload
Write-Output ("Created {0}" -f $response.ticketNumber)
if ($response.viewUrl) { Write-Output ("Customer link: {0}" -f $response.viewUrl) }
foreach ($warning in @($response.warnings)) { if ($warning) { Write-Output ("Warning: {0}" -f $warning) } }
