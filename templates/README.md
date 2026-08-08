# templates/

Installable `dotnet new` templates for scaffolding new services. Not part of plan §11's original tree —
added because the reference project a template ships from must sit at the same folder depth as where
it will actually be instantiated (`services/<name>/`, `gateway/<name>/`), so its `ProjectReference` to
`packages/dotnet-service-defaults` resolves correctly in the generated output. Nesting the templates
under `packages/` instead (one level deeper) breaks that path once scaffolded elsewhere.

- `api-service/` — ASP.NET Core API template (`telumera-api`): health checks, ProblemDetails, request
  validation, structured logging, OpenTelemetry.
- `worker-service/` — .NET worker template (`telumera-worker`): graceful shutdown, retry (Polly), an
  idempotency hook, OpenTelemetry tracing.

Both reference `packages/dotnet-service-defaults` for shared cross-cutting setup.

## Using a template

```sh
# One-time, per machine:
dotnet new install ./templates/api-service
dotnet new install ./templates/worker-service

# Scaffold a new service (run from services/ or gateway/ so the generated
# ProjectReference to packages/dotnet-service-defaults resolves):
cd services
dotnet new telumera-api -n Telumera.Services.MyService.Api
dotnet new telumera-worker -n Telumera.Services.MyService.Worker
```
