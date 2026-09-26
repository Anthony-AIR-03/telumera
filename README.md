<div align="center">

# Telumera

**A self-hosted, privacy-first analytics and developer-intelligence platform, built module by module.**

[🌐 Live at telumera.nl](https://telumera.nl) · [💼 Portfolio write-up](https://anthony-air.nl/projects/telumera) · [📖 Case study](https://anthony-air.nl/projects/telumera/case-study/product-analytics)

![Vue.js](https://img.shields.io/badge/Vue.js-4FC08D?style=flat-square&logo=vuedotjs&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?style=flat-square&logo=typescript&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Dapr](https://img.shields.io/badge/Dapr-0D2192?style=flat-square&logo=dapr&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![ClickHouse](https://img.shields.io/badge/ClickHouse-FFCC01?style=flat-square&logo=clickhouse&logoColor=black)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white)
![GitHub Actions](https://img.shields.io/badge/GitHub%20Actions-2088FF?style=flat-square&logo=githubactions&logoColor=white)

<a href="https://telumera.nl"><img src="docs/case-studies/assets/overview-live-visitors.jpg" alt="Telumera dashboard with live visitors" width="720" /></a>

</div>

## About

In the spirit of PostHog, Plausible and Sentry combined. Telumera is live and tracks the traffic of
my portfolio, [anthony-air.nl](https://anthony-air.nl).

## What makes it different

- **Every number is traceable.** Metrics trace back to raw vs. processed event counts, bot and
  duplicate classification, and consent state, instead of a polished number with no visible
  provenance.
- **Strict data ownership.** Each service owns its own database and schema. No service reads another
  service's tables; they only talk through versioned events and typed APIs.
- **Privacy by design.** Public ingestion tokens instead of secrets in the browser, no raw IP
  retention, and a documented [privacy threat model](docs/privacy/privacy-threat-model.md).
- **Services only when they earn it.** A module becomes its own deployable service only once it
  genuinely needs independent scaling, storage or a security boundary.

## Modules

| Module | Status |
|---|---|
| **Product Analytics**: tracking SDK, event collector, ClickHouse pipeline, live dashboard | ✅ Shipped ([case study](docs/case-studies/product-analytics.md)) |
| Performance Monitoring: Web Vitals, percentiles, regression detection | Planned |
| Error Tracking: grouping, source maps, issue lifecycle | Planned |
| Deployment Intelligence: release tracking, before/after comparison | Planned |
| Goals and Funnels | Planned |
| AI Insights: Claude via typed tools, no raw database access | Planned |
| Alerts and Uptime | Planned |

## Architecture

```
Browser SDK ──► Event Collector ──► RabbitMQ (Dapr pub/sub) ──► Analytics ──► ClickHouse
                                                                              │
Dashboard (Vue 3) ──► Gateway / BFF ──► Identity & Workspace · Site Registry · Analytics API
                                              │                  │
                                         PostgreSQL          PostgreSQL
```

| Part | Folder | Role |
|---|---|---|
| Dashboard | [`apps/dashboard-web`](apps/dashboard-web) | Vue 3 + TypeScript dashboard, live visitors over SignalR |
| Gateway | [`gateway`](gateway) | API gateway / backend-for-frontend, Azure AD sign-in |
| Identity & Workspace | [`services/identity-workspace`](services/identity-workspace) | Users, workspaces, membership |
| Site Registry | [`services/site-registry`](services/site-registry) | Tracked sites and their public ingestion tokens |
| Event Collector | [`services/event-collector`](services/event-collector) | Public ingestion endpoint: validation, bot/duplicate classification |
| Analytics | [`services/analytics`](services/analytics) | Event processing into ClickHouse and the metrics API |
| Browser SDK | [`packages/browser-sdk`](packages/browser-sdk) | The tracking script sites embed |
| Shared packages | [`packages`](packages) | Event contracts, transactional outbox, idempotent consumers, service defaults |

The key decisions are recorded as [ADRs](docs/adr): a service-oriented modular platform, Dapr as the
pub/sub abstraction, PostgreSQL plus ClickHouse, the transactional outbox with idempotent consumers,
context-level data isolation, and public browser ingestion tokens. Diagrams and data ownership are
in [`docs/architecture`](docs/architecture).

## Tech stack

**Front-end:** Vue 3, TypeScript, Vite · **Back-end:** C#, ASP.NET Core (.NET), Dapr, SignalR ·
**Data:** PostgreSQL, ClickHouse, RabbitMQ, Redis, MinIO · **Observability:** OpenTelemetry ·
**Infrastructure:** Docker Compose, Bicep (Azure), GitHub Actions, GitHub Container Registry

## How it's built and shipped

- **CI** ([`ci.yml`](.github/workflows/ci.yml)) builds the .NET solution, type-checks, lints and
  builds the dashboard, and tests the browser SDK on every pull request and push.
- **Container Build** ([`container-build.yml`](.github/workflows/container-build.yml)) builds all six
  services as Docker images in parallel and pushes them to GHCR on every push to `main`.
- **Deploy** ([`deploy-nas.yml`](.github/workflows/deploy-nas.yml)) runs on a self-hosted runner,
  pulls the SHA-pinned images and restarts the stack with Docker Compose. Nothing is compiled on the
  host. Setup is described in [`docs/runbooks/nas-runner-setup.md`](docs/runbooks/nas-runner-setup.md).

## Running it locally

Requirements: Docker with Compose v2, the .NET SDK and Node.js.

```bash
cd infrastructure/compose
cp .env.example .env        # local-only values
docker compose up -d
./scripts/health-check.sh   # scripts/health-check.ps1 on Windows PowerShell
```

The full quick-start, secrets model and debugging guide are in
[`docs/runbooks/local-environment.md`](docs/runbooks/local-environment.md).

## More screenshots

| Geography | Data quality |
|---|---|
| ![Country breakdown from GeoIP](docs/case-studies/assets/geography.jpg) | ![Data-quality view with raw vs. processed counts](docs/case-studies/assets/data-quality.jpg) |

## Repository layout

- `apps/`: front-end applications
- `gateway/`: the API gateway / BFF
- `services/`: one folder per bounded-context service
- `packages/`: shared libraries for services and apps
- `templates/`: `dotnet new` templates for scaffolding new services
- `infrastructure/`: Docker Compose, Dapr components, Bicep, observability config
- `docs/`: architecture, ADRs, privacy, contracts, runbooks, case studies
- `tests/`: contract, integration, load and end-to-end tests
- `planning/`: the modular project plan and roadmap

Working conventions are in [CONTRIBUTING.md](CONTRIBUTING.md); the full project context is in
[CLAUDE.md](CLAUDE.md).

---

<div align="center">

Made by **Anthony Inocencio Ramos** · [anthony-air.nl](https://anthony-air.nl) · [LinkedIn](https://www.linkedin.com/in/anthony-inoc%C3%AAncio-ramos-b89003277/)

</div>
