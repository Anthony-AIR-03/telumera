# Gets a real Entra ID access token for manual testing of identity-workspace/site-registry's
# protected endpoints, via the OAuth2 device code flow. Uses the separate "Telumera CLI Test
# Client" app registration (public client, device-code flow enabled) — see
# docs/runbooks/local-environment.md's "Auth" section for why this exists instead of a real
# dashboard sign-in flow.
#
# Usage: .\get-dev-token.ps1   (prints instructions to the host, the access token alone to
# stdout once sign-in completes, so `$token = .\get-dev-token.ps1` works)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$envFile = if (Test-Path ".env") { ".env" } else { ".env.example" }
$envVars = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
        $envVars[$matches[1]] = $matches[2]
    }
}

$tenantId = $envVars["AZURE_AD_TENANT_ID"]
$apiClientId = $envVars["AZURE_AD_API_CLIENT_ID"]
$testClientId = $envVars["AZURE_AD_TEST_CLIENT_ID"]

if (-not $tenantId) { Write-Host "AZURE_AD_TENANT_ID not set in .env"; exit 1 }
if (-not $apiClientId) { Write-Host "AZURE_AD_API_CLIENT_ID not set in .env"; exit 1 }
if (-not $testClientId) { Write-Host "AZURE_AD_TEST_CLIENT_ID not set in .env - register the Telumera CLI Test Client app first (docs/runbooks/local-environment.md)"; exit 1 }

function Get-ErrorBody($ErrorRecord) {
    $response = $ErrorRecord.Exception.Response
    if (-not $response) { return $null }
    $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
    $body = $reader.ReadToEnd()
    try { return $body | ConvertFrom-Json } catch { return $null }
}

$scope = "api://$apiClientId/access_as_user"

$deviceResponse = Invoke-RestMethod -Method Post `
    -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/devicecode" `
    -ContentType "application/x-www-form-urlencoded" `
    -Body @{ client_id = $testClientId; scope = $scope }

Write-Host $deviceResponse.message
Write-Host ""

$interval = if ($deviceResponse.interval) { $deviceResponse.interval } else { 5 }
$deadline = (Get-Date).AddSeconds($(if ($deviceResponse.expires_in) { $deviceResponse.expires_in } else { 900 }))

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds $interval

    try {
        $tokenResponse = Invoke-RestMethod -Method Post `
            -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" `
            -ContentType "application/x-www-form-urlencoded" `
            -Body @{
                grant_type  = "urn:ietf:params:oauth:grant-type:device_code"
                client_id   = $testClientId
                device_code = $deviceResponse.device_code
            }

        Write-Host "Signed in."
        Write-Output $tokenResponse.access_token
        exit 0
    } catch {
        $errorBody = Get-ErrorBody $_
        $errorCode = if ($errorBody) { $errorBody.error } else { $null }

        switch ($errorCode) {
            "authorization_pending" { continue }
            "slow_down" { $interval += 5; continue }
            default {
                Write-Host "Sign-in failed: $errorCode"
                if ($errorBody) { Write-Host ($errorBody | ConvertTo-Json) }
                exit 1
            }
        }
    }
}

Write-Host "Timed out waiting for sign-in."
exit 1
