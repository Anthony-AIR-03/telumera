# infrastructure/

Deployment and local-runtime infrastructure, portable between self-hosted (Docker Compose) and Azure
(Container Apps + Bicep) without app rewrites (see [ADR 0001](../docs/adr/0001-service-oriented-modular-platform.md)).

- `compose/` — Docker Compose stack for local self-hosted development.
- `dapr/` — Dapr component configs (pub/sub, state, bindings) per environment.
- `bicep/` — Azure Bicep templates (Container Apps, Service Bus, Postgres, Blob Storage, Key Vault).
- `observability/` — OpenTelemetry collector config and local dashboards.

All populated starting in M00.3 (Local self-hosted runtime) and M00.5 (Messaging, observability, CI/CD).
Not yet created.
