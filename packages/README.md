# packages/

Shared libraries consumed by multiple services and/or apps. Each is versioned and referenced
explicitly — services never share code by reaching into another service's folder.

- `browser-sdk/` — the TypeScript tracking SDK loaded on customer sites (M01).
- `event-contracts/` — versioned event envelope and payload contracts shared across services.
- `outbox/` — shared transactional outbox pattern (entity, EF Core mapping, Dapr-publishing
  `BackgroundService`) per `docs/adr/0004-transactional-outbox-and-idempotent-consumers.md`; a service
  provides its own `DbContext` and topic/source, the package does the polling and CloudEvent publishing.
- `idempotency/` — shared idempotent-consumer pattern (processed-event marker + EF Core mapping + a
  `TryBeginProcessingEventAsync` guard) per the same ADR; validated by `idempotency.Tests/` since no
  real event subscriber exists in the codebase yet.
- `dotnet-service-defaults/` — shared ASP.NET Core/worker cross-cutting concerns (health checks,
  OpenTelemetry, ProblemDetails, logging).
- `test-fixtures/` — shared test utilities and fixtures (fake data builders, test host helpers).
