# GeoIP database (`*.mmdb`)

This directory is bind-mounted read-only into the `analytics` service at `/geoip` (see
`docker-compose.yml`). It holds the MaxMind-format country database used by
`services/analytics/GeoLookup.cs` (`MmdbGeoLookup`).

**The `.mmdb` file is not committed** (`.gitignore`: `*.mmdb`) — MaxMind's GeoLite2 licence forbids
redistribution, and the file is several MB. If no file is present, `analytics` logs a warning at
startup and runs with geography enrichment disabled (`events.country` stays empty) — the pipeline
still works.

## Getting a database

Run `../scripts/refresh-geoip.sh` from `infrastructure/compose/`. It supports two providers:

| Provider | File name | Account needed | Notes |
|---|---|---|---|
| **MaxMind GeoLite2 Country** (default) | `GeoLite2-Country.mmdb` | Free MaxMind account → `MAXMIND_LICENSE_KEY` in `.env` | Industry standard, updated twice weekly |
| **DB-IP Lite Country** | `dbip-country-lite.mmdb` (symlinked/copied to `GeoLite2-Country.mmdb`) | None | CC BY 4.0, updated monthly, slightly less accurate |

Refresh monthly. The `analytics` container reopens the file on restart; no code change is needed to
pick up a newer database.

## Definitions

Geography is **country-only** by default (`docs/analytics/definitions-and-privacy-model.md` §7).
Region/subdivision granularity would need GeoLite2-**City** plus a per-site opt-in and is not built.
