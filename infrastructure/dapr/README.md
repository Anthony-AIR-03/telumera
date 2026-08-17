# infrastructure/dapr

Dapr component configuration for the local self-hosted runtime (M00.3), per
`docs/adr/0002-dapr-pubsub-abstraction.md`. Mounted read-only into every Dapr sidecar container in
`infrastructure/compose/docker-compose.yml` at `/components` (via `-resources-path=/components`).

- `components/pubsub-rabbitmq.yaml` — the `pubsub` building block, backed by RabbitMQ locally (swapped
  for Azure Service Bus Topics in the Azure environment — application code targets `pubsub` either way
  and never changes). Configures durable/persistent delivery, bounded prefetch, and a dead-letter queue
  for messages that exhaust retries.
- `components/secretstore-env.yaml` — resolves `pubsub-rabbitmq.yaml`'s `secretKeyRef` (the RabbitMQ
  connection string) from the sidecar container's own environment variables, so the real credential
  never appears in a committed YAML file.
- `components/resiliency.yaml` — the retry/backoff + circuit breaker policy applied to `pubsub`, scoped
  to specific Dapr `app-id`s. **Add a new service's app-id to the `scopes` list here when it starts
  publishing or subscribing**, or it silently falls back to Dapr's unbounded default retry behavior.
- `config/config.yaml` — the Dapr sidecar `Configuration` resource (tracing sample rate, OTel export
  target — currently unset, see `infrastructure/observability/`, built in M00.5).

No state store or bindings component exists yet — none of the read planning/architecture docs specify one
as a required M00.3 building block. Add one only when a concrete need shows up (e.g. Redis-backed Dapr
state store for a specific service), per the same "logical boundary earns its complexity" rule as
everything else in this repo.

See `docs/runbooks/local-environment.md` for the quick start, the secrets model, and a debugging
checklist for "why isn't my event arriving."
