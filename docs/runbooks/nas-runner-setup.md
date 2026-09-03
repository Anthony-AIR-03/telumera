# NAS auto-deploy — runner setup

> Status: M01.8. One-time setup for the self-hosted GitHub Actions runner that auto-deploys Telumera
> to a NAS, plus how the pipeline works day to day. The deploy itself is
> `.github/workflows/deploy-nas.yml`; what it deploys is
> `infrastructure/compose/docker-compose.yml` + `docker-compose.nas.yml`.
>
> This runbook is deliberately host-agnostic (this repo may become public — see
> `docs/architecture/nas-deployment-profile.md`). The concrete host, SSH details, deploy user, and
> absolute paths for the operator's own NAS live in their private notes, not here. Placeholders below:
> `$NAS_DEPLOY_DIR` (the repo Variable), `$RUNNER_USER` (the account the runner runs as).

## How it works

```
push to main
  └─▶ Container Build (.github/workflows/container-build.yml, GitHub-hosted)
        builds all 6 images, pushes ghcr.io/<owner>/telumera-<svc>:{sha-<short>, latest}
        └─▶ Deploy to NAS (.github/workflows/deploy-nas.yml, self-hosted runner on the NAS)
              checkout the same commit
              rsync the tree ▶ $NAS_DEPLOY_DIR      (excludes .env.nas + geoip/ — host-only state)
              docker login ghcr.io   (GITHUB_TOKEN, packages: read)
              docker compose … pull      ← pulls the sha-<short> images just built
              docker compose … up -d --remove-orphans
              wait for /health/ready on the 5 .NET services
```

Nothing is compiled on the NAS. `analytics` runs its EF Core migrations on startup, so every deploy
effectively applies pending migrations — keep them expand/contract (`rollback-and-migrations.md`).

Manual deploy / rollback: **Actions → Deploy to NAS → Run workflow**, with an `image_tag`
(`latest`, or an older `sha-…` to roll back).

## One-time setup

### 1. Give the runner user Docker access

The deploy runs `docker` directly (no sudo). The runner account must be able to reach the Docker
socket:

```bash
getent group docker                 # confirm the group exists
sudo usermod -aG docker "$RUNNER_USER"
# group change needs a fresh login — restart the runner service after step 2
```

Verify: `sudo -u "$RUNNER_USER" docker ps` succeeds with no permission error.

### 2. Register the Telumera runner

GitHub personal-account runners are per-repo, so an existing runner for another repo cannot be
reused — Telumera needs its own registration. It can share the NAS and the runner user; give it its
own directory.

1. GitHub → this repo → **Settings → Actions → Runners → New self-hosted runner** → Linux / x64.
   Follow the download snippet it shows, then configure with a `telumera` label:

   ```bash
   ./config.sh --url https://github.com/<owner>/<repo> --token <TOKEN> \
     --name <runner-name> --labels telumera --unattended
   ```

   `--labels telumera` is what `runs-on: [self-hosted, telumera]` matches.
2. Install it as a service so it survives reboots:

   ```bash
   sudo ./svc.sh install "$RUNNER_USER"
   sudo ./svc.sh start
   sudo ./svc.sh status
   ```

Confirm it shows **Idle** under Settings → Actions → Runners.

### 3. Repo Variables

GitHub → **Settings → Secrets and variables → Actions → Variables**:

| Variable | Value |
|---|---|
| `NAS_DEPLOY_DIR` | absolute path of the deploy directory on the NAS (e.g. `/srv/telumera`) |
| `VITE_AZURE_AD_TENANT_ID` | the Entra tenant ID |
| `VITE_AZURE_AD_DASHBOARD_CLIENT_ID` | the **Telumera Dashboard** SPA app-registration client ID |
| `VITE_AZURE_AD_API_SCOPE` | `api://<Telumera API client id>/access_as_user` |

The three `VITE_AZURE_*` values are baked into the `dashboard-web` image at CI build time (Vite
inlines them). None are secret — a SPA ships all of them to every browser — but the GUIDs are kept
out of tracked files. After setting them, re-run **Container Build** once so a `dashboard-web` image
exists with them baked in. The `api.` / `hubs.` / `collect.telumera.nl` URLs are literals in
`container-build.yml`.

### 4. Prepare the deploy directory

```bash
sudo mkdir -p "$NAS_DEPLOY_DIR/infrastructure/compose/geoip"
sudo chown -R "$RUNNER_USER" "$NAS_DEPLOY_DIR"   # the deploy runs as $RUNNER_USER and chmods the tree

cd "$NAS_DEPLOY_DIR/infrastructure/compose"
# put .env.nas here from the template, filled with PROD values:
#   fresh prod passwords, CORS_ALLOWED_ORIGINS=https://telumera.nl, MAXMIND_LICENSE_KEY,
#   AZURE_AD_TENANT_ID / AZURE_AD_API_CLIENT_ID / AZURE_AD_TEST_CLIENT_ID
# template: infrastructure/compose/.env.nas.example
```

`.env.nas` and `geoip/` live only here — the workflow's `rsync --delete` excludes both, so redeploys
never touch them.

**Ownership matters:** `docker compose --env-file .env.nas` runs as `$RUNNER_USER`, so that file
must be readable by it. If you created `.env.nas` with `sudo`, fix it up after:

```bash
sudo chown -R "$RUNNER_USER" "$NAS_DEPLOY_DIR"
sudo chmod 600 "$NAS_DEPLOY_DIR/infrastructure/compose/.env.nas"
```

### 5. The `npm` network

`gateway` / `event-collector` / `analytics` / `dashboard-web` join the external `npm` Docker network
so Nginx Proxy Manager can reach them by container name. It must already exist (shared with NPM):
`docker network inspect npm >/dev/null && echo ok` — if not, `docker network create npm`.

## First deploy

1. Finish steps 1–5.
2. **Actions → Deploy to NAS → Run workflow** on `main`, `image_tag = latest`.
3. "Wait for services to report ready" going green means the 5 .NET services are up.
4. On the NAS, once: `cd "$NAS_DEPLOY_DIR/infrastructure/compose" && ./scripts/refresh-geoip.sh`
   (then monthly — the deploy does **not** do this), then
   `docker compose -f docker-compose.yml -f docker-compose.nas.yml --env-file .env.nas restart analytics analytics-dapr`.
5. Add the four Nginx Proxy Manager proxy hosts — `analytics-module.md` → "Deploying to the NAS".
6. Do the vertical-slice check in `analytics-module.md`.

From here every push to `main` redeploys automatically.

## Troubleshooting

- **Run doesn't start** — runner Offline, or wrong repo/label. Check `svc.sh status` and that the
  runner is registered to this repo with the `telumera` label.
- **`NAS_DEPLOY_DIR is not set`** — repo Variable missing (step 3).
- **`permission denied … /var/run/docker.sock`** — step 1 not done, or the runner service wasn't
  restarted after `usermod`.
- **`pull` fails with `denied` / `manifest unknown`** — `docker login ghcr.io` needs the workflow's
  `packages: read` (already set) and the packages must be linked to this repo. `manifest unknown`
  for a `sha-…` tag: the workflow auto-retries with `latest`; if that also fails, Container Build for
  that commit didn't finish.
- **A .NET service never becomes ready** — the step dumps its last 80 log lines. Usual causes: an
  `.env.nas` value wrong (DB password, Entra IDs), ClickHouse/Postgres still initialising on first
  boot (re-run the deploy), or a Dapr sidecar orphaned — `analytics-module.md` → "Common failures".
- **`!reset` rejected by `docker compose`** — NAS Compose older than 2.24; update the Docker package.
- **`postgres … Permission denied` on `/docker-entrypoint-initdb.d/` (or a Dapr sidecar can't read
  `/components`)** — the NAS's umask produced modes the container (running as another UID) can't
  read. The deploy's "Sync" step chmods the tree world-readable after rsync; if it still happens,
  the deploy dir has files owned by `root` (from a `sudo mkdir`) that `$RUNNER_USER` couldn't chmod
  — `sudo chown -R "$RUNNER_USER" "$NAS_DEPLOY_DIR"` and re-run.
- **Deploy overwrote something on the NAS** — `rsync --delete` mirrors the repo into
  `$NAS_DEPLOY_DIR`. Only `.env.nas` and `geoip/` are excluded; keep no other hand-edited state there.
