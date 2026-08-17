# docs/runbooks

Operational runbooks (deploy, rollback, incident response) per service. Populated as services reach
production readiness, starting with M00.5 (CI/CD) and M09 (Alerts and Uptime) — plus one platform-level
runbook started early, in M00.3:

- `local-environment.md` — running the local self-hosted runtime (`infrastructure/compose/`), the
  secrets model, and a debugging checklist for the Dapr sidecar/RabbitMQ pub/sub setup.
