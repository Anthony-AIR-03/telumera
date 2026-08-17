# Seeds a demo workspace, a demo site, and a handful of representative
# page-view events, so the local runtime has something to look at without
# waiting for real services to exist.
#
# IMPORTANT: the tables this script creates (bootstrap_workspaces,
# bootstrap_sites, bootstrap_page_views) are NOT the real Identity &
# Workspace / Site Registry / Analytics schemas. Those belong to each
# service's own migrations, once M00.4+ actually builds them. This script
# exists purely to exercise the infrastructure built in M00.3 (Postgres,
# ClickHouse, and the connectivity between them) — it will be superseded,
# and these bootstrap_* tables dropped, once the real services land.
#
# Safe to re-run: uses fixed demo IDs and upserts/re-inserts rather than
# accumulating duplicates.
#
# Usage: .\seed-demo-data.ps1

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$envFile = if (Test-Path ".env") { ".env" } else { ".env.example" }
$envVars = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
        $envVars[$matches[1]] = $matches[2]
    }
}

$DemoWorkspaceId = "00000000-0000-0000-0000-000000000001"
$DemoSiteId = "00000000-0000-0000-0000-000000000001"

Write-Host "== Postgres: demo workspace (telumera_access) =="
$workspaceSql = @"
CREATE TABLE IF NOT EXISTS bootstrap_workspaces (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO bootstrap_workspaces (id, name)
VALUES ('$DemoWorkspaceId', 'Demo Workspace')
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;
"@
$workspaceSql | docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U svc_access -d telumera_access

Write-Host "== Postgres: demo site (telumera_sites) =="
$siteSql = @"
CREATE TABLE IF NOT EXISTS bootstrap_sites (
    id UUID PRIMARY KEY,
    workspace_id UUID NOT NULL,
    name TEXT NOT NULL,
    domain TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO bootstrap_sites (id, workspace_id, name, domain)
VALUES ('$DemoSiteId', '$DemoWorkspaceId', 'anthony-air.nl (demo)', 'anthony-air.nl')
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, domain = EXCLUDED.domain;
"@
$siteSql | docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U svc_sites -d telumera_sites

Write-Host "== ClickHouse: representative page-view events (telumera_analytics) =="
# The last event_id below is inserted twice on purpose: this table is
# append-only (like a real raw-events table would be), so a naive COUNT(*)
# over-counts it. Query with COUNT(DISTINCT event_id) to see the correct
# count - this is the smallest possible stand-in for step 12 of the vertical
# slice in docs/architecture/vision-and-scope.md §7 ("repeat the same event
# ID and prove it is not counted twice"). The real dedup logic belongs to
# the Analytics Service once it exists.
$clickhouseSql = @"
CREATE TABLE IF NOT EXISTS bootstrap_page_views (
    event_id UUID,
    workspace_id UUID,
    site_id UUID,
    url String,
    occurred_at DateTime
) ENGINE = MergeTree
ORDER BY (occurred_at, event_id);

ALTER TABLE bootstrap_page_views DELETE WHERE workspace_id = '$DemoWorkspaceId';

INSERT INTO bootstrap_page_views (event_id, workspace_id, site_id, url, occurred_at) VALUES
    ('10000000-0000-0000-0000-000000000001', '$DemoWorkspaceId', '$DemoSiteId', '/', now() - INTERVAL 3 MINUTE),
    ('10000000-0000-0000-0000-000000000002', '$DemoWorkspaceId', '$DemoSiteId', '/blog', now() - INTERVAL 2 MINUTE),
    ('10000000-0000-0000-0000-000000000003', '$DemoWorkspaceId', '$DemoSiteId', '/pricing', now() - INTERVAL 1 MINUTE),
    ('10000000-0000-0000-0000-000000000004', '$DemoWorkspaceId', '$DemoSiteId', '/', now()),
    ('10000000-0000-0000-0000-000000000004', '$DemoWorkspaceId', '$DemoSiteId', '/', now());
"@
$clickhouseSql | docker compose exec -T clickhouse clickhouse client --user svc_analytics --password $envVars["CLICKHOUSE_APP_PASSWORD"] --database telumera_analytics --multiquery

Write-Host ""
Write-Host "Seeded: 1 workspace, 1 site, 5 page-view rows (4 distinct event_ids) in bootstrap_page_views."
Write-Host 'Try: docker compose exec clickhouse clickhouse client --user svc_analytics --password <APP_DB_PASSWORD> --database telumera_analytics -q "SELECT count(*), count(DISTINCT event_id) FROM bootstrap_page_views"'
