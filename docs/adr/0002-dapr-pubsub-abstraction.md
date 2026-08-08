# ADR 0002: Dapr for Pub/Sub and Service Invocation

- **Status:** Accepted
- **Date:** 2026-08-06
- **Related:** `docs/architecture/c4-container.md`, `docs/adr/0001-service-oriented-modular-platform.md`

## Context

Telumera must run in two very different environments without a rewrite: self-hosted (on the owner's NAS,
via Docker Compose) and Azure (via Container Apps, for a development/demo environment). The two
environments have different natural choices for messaging and secrets:

| Capability | Self-hosted | Azure |
|---|---|---|
| Containers | Docker Compose | Azure Container Apps |
| Pub/Sub | RabbitMQ | Azure Service Bus **Topics** (not Queues — several events have multiple independent subscribers, e.g. `analytics.processed.v1` is consumed by Conversion, Alerting, and AI Insights; Queues are point-to-point and can't fan out that way) |
| Secrets | Docker secrets/environment files | Azure Key Vault + managed identity |

If services called RabbitMQ's or Azure Service Bus's SDKs directly, every service would need
environment-specific code paths (or two build targets) to run in both places, and the module boundary
rules in `docs/adr/0001-service-oriented-modular-platform.md` (versioned contracts, no direct coupling)
would be harder to enforce consistently.

## Decision

Use **Dapr** as the abstraction layer for cross-service pub/sub and service invocation, wherever it adds
real value (i.e. anywhere a service talks to another service or publishes an event — not for pure
in-process logic).

- Services publish and subscribe through the Dapr pub/sub building block, backed by RabbitMQ locally and
  Azure Service Bus Topics in Azure, via Dapr component configuration — never by importing a
  broker-specific SDK into application code.
- Synchronous cross-service calls that aren't naturally event-shaped use Dapr service invocation rather
  than hardcoded service URLs.
- Every published event uses the CloudEvents-style envelope:

  ```json
  {
    "id": "globally-unique-event-id",
    "type": "analytics.page-view.received.v1",
    "source": "telumera.collector",
    "subject": "sites/site-id",
    "time": "2026-08-06T14:00:00Z",
    "tenantId": "workspace-id",
    "siteId": "site-id",
    "correlationId": "request-or-session-correlation-id",
    "dataVersion": 1,
    "data": {}
  }
  ```

- Compatibility rules: additive fields are preferred; consumers must ignore unknown fields; breaking
  payload changes require a new event type version (`.v2`, etc.); a service may support multiple input
  versions during a migration; events describe **completed facts**, never remote commands (commands use
  explicit APIs or dedicated command topics); every consumer must be idempotent (see the consumer
  idempotency pattern in M00.5).
- **CloudEvents envelope ownership (explicit rule):** Dapr's pub/sub building block wraps every published
  message in its own transport-level CloudEvent by default. To avoid accidentally nesting one CloudEvent
  inside another, **Dapr owns the transport envelope; Telumera explicitly supplies/overrides the `id`,
  `type`, `source`, and `subject` fields on publish** (Dapr's pub/sub API accepts these as overrides
  rather than only auto-generating them), and Telumera-specific fields — `tenantId`, `siteId`,
  `correlationId`, `dataVersion` — live inside the CloudEvent's `data` payload, not duplicated at the
  transport level. Application code never hand-constructs a second, nested CloudEvents-shaped JSON object
  as the message body.
- **Delivery semantics (explicit rule):** delivery is **at-least-once**. Consequences:
  - duplicate delivery is expected and normal, not an error condition — every consumer must be idempotent
    (already required above);
  - retries are bounded and configured per subscription (retry count/backoff set in Dapr component
    config, not left at silent defaults);
  - after retries are exhausted, a message goes to a **dead-letter destination** (a dead-letter topic on
    Azure Service Bus; a dead-letter queue/exchange on RabbitMQ) rather than being silently dropped;
  - dead-lettered events must be inspectable and replayable — this is an operational requirement for
    M00.3's local runtime and M00.5's CI/review process, not just a production concern.
- The initial event catalogue (who publishes, who subscribes) is defined in
  `planning/Telumera_Modular_Project_Plan.md` §8.3 and mirrored in
  `docs/architecture/c4-container.md`. Event granularity (per-item vs. per-batch vs. per-window) must be
  decided per event type before a publisher ships it — see the Analytics granularity note in
  `docs/architecture/bounded-contexts-and-data-ownership.md`.
- Producer-side reliability (making sure a published event isn't lost if the publishing service crashes
  between its own state write and the publish call) is covered separately in
  `docs/adr/0004-transactional-outbox-and-idempotent-consumers.md`.

## Consequences

- **Positive:** application code should not need a rewrite when switching brokers between self-hosted and
  Azure — only Dapr component configuration changes.
- **Positive:** the versioned envelope + idempotency requirement gives every module the same contract to
  code against, regardless of which broker is underneath.
- **Negative:** adds an extra moving part (the Dapr sidecar) to local development and deployment, which
  M00.3 (local self-hosted runtime) must set up and document clearly enough that "why isn't my event
  arriving" doesn't become a recurring debugging cost.
- **Negative:** Dapr's own abstractions (pub/sub, service invocation) don't cover every messaging pattern
  perfectly — if a future module needs something Dapr doesn't model well, that's a case-by-case exception
  to evaluate, not a reason to abandon the abstraction wholesale.
