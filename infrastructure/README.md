# infrastructure/

Deployment and local-runtime infrastructure, portable between self-hosted (Docker Compose) and Azure
(Container Apps + Bicep) without app rewrites (see [ADR 0001](../docs/adr/0001-service-oriented-modular-platform.md)).

- `compose/` — Docker Compose stack for local self-hosted development.
- `dapr/` — Dapr component configs (pub/sub, state, bindings) per environment.
- `bicep/` — Azure Bicep templates (Container Apps, Service Bus, Postgres, Blob Storage, Key Vault).
- `observability/` — OpenTelemetry collector config and local dashboards.

`compose/` and `dapr/` are built (M00.3 — local self-hosted runtime). `bicep/` and `observability/` are
not yet created (M00.3's Azure counterpart and M00.5, respectively).
