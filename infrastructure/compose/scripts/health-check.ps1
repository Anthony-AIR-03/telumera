# One-command verification that the local self-hosted runtime is actually
# working — not just "containers are running", but that each dependency is
# reachable and, for Dapr, that a publish actually reaches RabbitMQ.
#
# Usage: .\health-check.ps1   (run from anywhere — it cd's to the compose dir)

$ErrorActionPreference = "Continue"
Set-Location (Join-Path $PSScriptRoot "..")

$script:Failed = $false

function Pass($msg) { Write-Host "  OK   $msg" }
function Fail($msg) { Write-Host "  FAIL $msg"; $script:Failed = $true }
function Section($msg) { Write-Host ""; Write-Host "== $msg ==" }

# Load .env (or fall back to .env.example) for credentials used below.
$envFile = if (Test-Path ".env") { ".env" } else { ".env.example" }
$envVars = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
        $envVars[$matches[1]] = $matches[2]
    }
}

Section "Containers"
$expectedServices = @("postgres", "clickhouse", "rabbitmq", "redis", "seaweedfs", "otel-collector", "dapr-placement", "dapr-smoke-test-sidecar")
foreach ($svc in $expectedServices) {
    $cid = (docker compose ps -q $svc 2>$null)
    if (-not $cid) {
        Fail "$svc`: not running (try: docker compose up -d)"
        continue
    }
    $health = docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $cid
    if ($health -eq "healthy" -or $health -eq "running") {
        Pass "$svc`: $health"
    } else {
        Fail "$svc`: $health"
    }
}

Section "PostgreSQL - per-context databases"
$pgCheck = docker compose exec -T postgres psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='telumera_access'" 2>$null
if ($pgCheck -match "1") {
    Pass "telumera_access database exists"
} else {
    Fail "telumera_access database missing - did db-init run? (docker compose down -v && up to force it)"
}

Section "ClickHouse - per-context databases"
$chCheck = docker compose exec -T clickhouse clickhouse client --user admin --password $envVars["CLICKHOUSE_ADMIN_PASSWORD"] -q "EXISTS DATABASE telumera_analytics" 2>$null
if ($chCheck -match "1") {
    Pass "telumera_analytics database exists"
} else {
    Fail "telumera_analytics database missing - did db-init run?"
}

Section "RabbitMQ - management API"
try {
    $pair = "$($envVars['RABBITMQ_DEFAULT_USER']):$($envVars['RABBITMQ_DEFAULT_PASS'])"
    $basicAuth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
    Invoke-WebRequest -Uri "http://localhost:15672/api/overview" -Headers @{Authorization = "Basic $basicAuth" } -UseBasicParsing -TimeoutSec 5 | Out-Null
    Pass "management API reachable at http://localhost:15672"
} catch {
    Fail "management API not reachable"
}

Section "Redis"
$redisCheck = docker compose exec -T redis redis-cli -a $envVars["REDIS_PASSWORD"] ping 2>$null
if ($redisCheck -match "PONG") {
    Pass "PING -> PONG"
} else {
    Fail "PING failed"
}

Section "SeaweedFS (object storage)"
try {
    Invoke-WebRequest -Uri "http://localhost:9000/" -UseBasicParsing -TimeoutSec 5 | Out-Null
    Pass "S3 endpoint reachable at http://localhost:9000"
} catch {
    # The S3 endpoint now requires auth (docs/adr/0007), so an unauthenticated probe 403s — that's
    # still a real HTTP response (.Exception.Response is populated), proof the service is up. Only a
    # true connection failure (refused/timeout — no Response at all) counts as unreachable.
    if ($_.Exception.Response) {
        Pass "S3 endpoint reachable at http://localhost:9000 ($([int]$_.Exception.Response.StatusCode) - auth required, as expected)"
    } else {
        Fail "S3 endpoint not reachable"
    }
}

Section "Dapr sidecar (smoke-test) + pub/sub round trip"
try {
    Invoke-WebRequest -Uri "http://localhost:3500/v1.0/healthz" -UseBasicParsing -TimeoutSec 5 | Out-Null
    Pass "sidecar healthz OK"
} catch {
    Fail "sidecar healthz not reachable - is dapr-smoke-test-sidecar up?"
}

try {
    $metadata = Invoke-WebRequest -Uri "http://localhost:3500/v1.0/metadata" -UseBasicParsing -TimeoutSec 5
    if ($metadata.Content -match '"name":"pubsub"') {
        Pass "pubsub component loaded"
    } else {
        Fail "pubsub component not loaded - check RABBITMQ_CONNECTION_STRING resolution (docs/runbooks/local-environment.md)"
    }
} catch {
    Fail "could not read sidecar metadata"
}

try {
    $body = '{"id":"health-check","tenantId":"health-check","siteId":"health-check","dataVersion":1}'
    Invoke-WebRequest -Uri "http://localhost:3500/v1.0/publish/pubsub/telumera.health-check.v1" -Method Post -Body $body -ContentType "application/json" -UseBasicParsing -TimeoutSec 5 | Out-Null
    Pass "publish through pubsub succeeded (see RabbitMQ management UI for the resulting exchange)"
} catch {
    Fail "publish through pubsub failed"
}

Write-Host ""
if (-not $script:Failed) {
    Write-Host "All checks passed."
    exit 0
} else {
    Write-Host "One or more checks failed - see docs/runbooks/local-environment.md for debugging steps."
    exit 1
}
