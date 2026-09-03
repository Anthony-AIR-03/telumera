# docs/runbooks

Operational runbooks (deploy, rollback, incident response) per service. Populated as services reach
production readiness, starting with M00.5 (CI/CD) and M09 (Alerts and Uptime) — plus one platform-level
runbook started early, in M00.3:

- `local-environment.md` — running the local self-hosted runtime (`infrastructure/compose/`), the
  secrets model, and a debugging checklist for the Dapr sidecar/RabbitMQ pub/sub setup.
- `rollback-and-migrations.md` — what "rollback" means per environment (self-hosted, Azure), and the
  expand/contract rule for database migrations that keeps a rollback from also being broken.
- `analytics-module.md` — operating the Product Analytics pipeline (M01.8): health checks, event
  replay, GeoIP database refresh, the live panel and data-quality dashboard, deploying to the NAS on
  `telumera.nl`, and backup/restore.
- `nas-runner-setup.md` — one-time setup for the self-hosted runner that auto-deploys the stack to
  the NAS on every push to `main` (`.github/workflows/deploy-nas.yml`), and how that pipeline works.
