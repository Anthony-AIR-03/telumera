# ADR 0007: Object Storage — SeaweedFS Over MinIO

- **Status:** Accepted
- **Date:** 2026-10-01
- **Related:** `infrastructure/compose/docker-compose.yml`, `infrastructure/compose/README.md`,
  `infrastructure/compose/seaweedfs/s3.json.template`

## Context

`docker-compose.yml` provisioned a `minio` service (`minio/minio:RELEASE.2024-10-13T13-34-11Z`) as
reserved S3-compatible object storage — "one bucket per bounded context once a service needs one." No
service in this repo consumes it yet (verified: no `.cs`, `appsettings*`, or other application config
anywhere references MinIO — only compose/docs/scripts mention it at all).

On 2026-10-01 the NAS's `Deploy to NAS` workflow failed at `docker compose pull` with `pull access denied
for minio/minio`. This is not a transient registry hiccup:

- MinIO stopped publishing free Community Edition images in October 2025, archived the repo in early
  2026, and **deleted `minio/minio` from Docker Hub entirely around 2026-09-11**.
- The community stopgap of repointing to `quay.io/minio/minio` also stopped working around
  2026-09-24, when Quay gated anonymous pulls of the same images.
- The last-published Community Edition release (including the exact tag this repo had pinned) carries
  an unpatched CVSS 8.8 authentication-bypass CVE — MinIO CE gets no further security patches, so even a
  cached/mirrored copy of the old image is a real security liability, not just an availability one.

In short: there is no registry-swap fix. The dependency itself is terminal for free/open use.

## Decision

Replace the `minio` service with **SeaweedFS** (`chrislusf/seaweedfs:4.48`), run in its single-binary
unified mode (`weed server -s3`, master + volume + filer + S3 gateway in one process) as a direct,
same-footprint substitute for the single-container MinIO setup this replaced.

Why SeaweedFS specifically, over the alternatives surveyed:

- **Apache-2.0**, not AGPL — avoids depending on another project a single commercial vendor could
  relicense/paywall the same way MinIO's maintainer did. (MinIO's own paid successor, "AIStor," was
  considered and rejected for this reason — still AGPL, telemetry-on-by-default, and explicitly
  not positioned as a long-term free option by its own vendor.)
- Actively maintained since 2012, 30k+ GitHub stars, full S3 API surface, used in production by
  projects like Kubeflow Pipelines — not a pre-1.0 or hobby-maintained alternative.
- Officially published to Docker Hub under the maintainer's own canonical `chrislusf/seaweedfs`
  namespace (confirmed against the project's own README/examples, not a third-party mirror).
- Verified locally before writing this change (not assumed from documentation alone, after the MinIO
  pull failure already came from an unverified assumption): the image pulls and runs today, the S3
  gateway starts correctly on its documented port, and its credential model was tested end-to-end —
  see "Credentials" below.

Alternatives considered and rejected: `quay.io/minio/aistor/minio` (AGPL, vendor telemetry, explicitly
not a long-term answer per MinIO's own messaging), Garage (AGPL, lacks object locking/lifecycle
policies — fine for lean deployments but a step down in feature coverage), RustFS (pre-1.0 maturity,
pilot-only).

Because nothing currently consumes this service, this swap requires **zero migration** — no data to
move, no consuming service's S3-client config to change. This was the cheapest possible moment to make
this change; waiting until a real service took a dependency on MinIO-specific behavior would have made
the same swap far more expensive later.

### What stayed the same on purpose

- **Host port**: kept at `9000` (mapped to SeaweedFS's S3 port 8333 internally) — no change needed
  anywhere else that assumes object storage lives on 9000 (e.g. ClickHouse's native-protocol port was
  already remapped to 9004 specifically to avoid this collision).
- **`MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` env var names**: kept as-is in `.env.example` /
  `.env.nas.example`, despite now configuring SeaweedFS. The real production value lives in `.env.nas`
  on the NAS, which is hand-maintained *outside* this repo (git-ignored, `rsync --delete`-excluded per
  the NAS deploy workflow) — renaming the var would require a manual edit there with no corresponding
  automated step to catch a missed rename. Not worth the operational risk for a cosmetic naming
  mismatch.

### What changed (and why)

- **Service/volume keys**: `minio` → `seaweedfs`, `minio-data` → `seaweedfs-data`. Unlike the env var
  names above, these live entirely inside this repo and the NAS's deploy dir (safe to rename, nothing
  external points at them by name), and leaving a service named `minio` running different software
  would itself be a future source of confusion.
- **Credentials**: MinIO took `MINIO_ROOT_USER`/`PASSWORD` directly as environment variables. SeaweedFS
  has no equivalent env-var flag — it reads identities from a mounted JSON config
  (`-s3.config=<path>`). Verified the exact schema against `weed shell`'s `s3.configure` command
  (`identities[].credentials[].{accessKey,secretKey}` + `identities[].actions`), authored
  `seaweedfs/s3.json.template` with placeholder tokens, and confirmed locally that: with no identity
  configured at all, the S3 API is fully open (anonymous list/create-bucket both succeed) — not an
  acceptable default — and that mounting a configured identity file correctly denies anonymous access
  (`403 AccessDenied`) from a cold start, no manual post-deploy step required. The compose service's
  startup command stamps the two existing env var values into the mounted template via `sed` (the
  image is Alpine-based with no `envsubst`) before exec'ing the real server.
- **Health check**: MinIO's compose healthcheck used its bundled `mc ready local` CLI, which doesn't
  exist in the SeaweedFS image. Replaced with a `curl` against SeaweedFS's master
  `/cluster/status` endpoint (verified reachable and unauthenticated) instead of the S3 port itself —
  the S3 port now requires auth, so an unauthenticated probe against it would always read as
  "unhealthy" via a 403, which is backwards. The two local-dev `health-check.sh`/`.ps1` scripts'
  object-storage checks were updated the same way: they now accept *any* HTTP response (including the
  expected 403) as "reachable," since `-f`/default-exception-on-non-2xx semantics would otherwise
  misreport a correctly-secured endpoint as down.

## Consequences

- No application code changes required (confirmed nothing consumes this service yet).
- Whichever service eventually becomes SeaweedFS's first real consumer should use the existing
  `telumera-admin` identity/bucket-per-bounded-context pattern already documented in
  `infrastructure/compose/README.md`, and should read `seaweedfs/s3.json.template` before assuming
  MinIO-specific SDK quirks apply — SeaweedFS's S3 API is broad but not byte-for-byte identical in
  every edge case.
- If SeaweedFS ever needs multi-node/erasure-coding/compliance features it doesn't have in unified
  single-binary mode, that's a reason to revisit this ADR, not to route around it with a MinIO fallback
  — MinIO CE is not coming back as a viable free dependency.
