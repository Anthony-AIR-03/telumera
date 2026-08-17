# Telumera

Self-hosted, modular analytics and developer-intelligence platform — a privacy-first alternative to
PostHog/Plausible/Sentry combined, built module-by-module.

- Full project context, principles and working conventions: [CLAUDE.md](./CLAUDE.md)
- Branch, commit, PR and release conventions: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Roadmap and module plan: [planning/Telumera_Modular_Project_Plan.md](./planning/Telumera_Modular_Project_Plan.md)
- Accepted architecture decisions: [docs/adr](./docs/adr)
- Architecture diagrams and data ownership: [docs/architecture](./docs/architecture)
- Privacy threat model: [docs/privacy/privacy-threat-model.md](./docs/privacy/privacy-threat-model.md)

## Status

Early scaffolding — M00 (Platform Foundation) in progress. M00.1 (architecture decisions), M00.2
(repository and service templates), and M00.3 (local self-hosted runtime — see
`infrastructure/compose/README.md`) are complete. No application services are deployable yet;
M00.3's Docker Compose stack is infrastructure only (`gateway/` and `services/*` are still empty
scaffolds, next up in M00.4).

## Repository layout

See plan §11 for the full specification. Each top-level folder has its own README describing what
belongs there and which milestone populates it:

- `apps/` — frontend applications (Vue 3 dashboard)
- `gateway/` — the API Gateway / BFF
- `services/` — one folder per bounded-context service
- `agents/` — the optional C++ edge agent (M11)
- `packages/` — shared libraries used across services and apps
- `templates/` — installable `dotnet new` templates for scaffolding new services
- `infrastructure/` — Docker Compose, Dapr components, Bicep, observability config
- `docs/` — architecture, ADRs, privacy, contracts, runbooks
- `tests/` — contract, integration, load and e2e tests
