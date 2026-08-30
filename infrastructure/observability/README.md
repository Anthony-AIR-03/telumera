# infrastructure/observability

OpenTelemetry Collector configuration for the local self-hosted runtime (M00.5).

- `otel-collector-config.yaml` — receives OTLP traces (gRPC `:4317`, HTTP `:4318`) from every .NET
  service (`packages/dotnet-service-defaults`'s `OTEL_EXPORTER_OTLP_ENDPOINT`) and from every Dapr
  sidecar (`infrastructure/dapr/config/config.yaml`'s `tracing.otel` block), and exports them to the
  collector's own container logs (`docker compose logs otel-collector`) via the `debug` exporter.

This proves distributed tracing propagates end-to-end — an HTTP request, the Dapr service-invocation hop
it makes, and (once something subscribes) a pub/sub hop all sharing one trace id — without a browsable
trace UI. There is no Grafana/Jaeger/Tempo backend wired up yet; add one later by extending
`otel-collector-config.yaml` with a real exporter, which needs no change anywhere upstream since services
and Dapr both already just point at this collector, not at a specific backend.
