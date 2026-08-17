# tests/integration

Integration tests exercising multiple real services together against the local Docker Compose stack
(Postgres, ClickHouse, RabbitMQ, Dapr sidecars).

- `Telumera.Tests.Integration/` — covers the M00.4 vertical slice: identity-workspace + site-registry,
  hit over HTTP at their compose-published ports (`5101`/`5102`), asserting the transactional outbox
  (`docs/adr/0004-transactional-outbox-and-idempotent-consumers.md`) actually publishes
  `site.created.v1`. Requires `docker compose up -d` from `infrastructure/compose/` first; run with
  `APP_DB_PASSWORD` set to your local `.env` value (falls back to `.env.example`'s placeholder
  otherwise) so the test can poll `outbox_events` directly.
