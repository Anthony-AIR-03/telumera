# InsightFlow

## Privacy-first Developer Intelligence Platform

**Project type:** Self-hosted, modular analytics and observability platform  
**Primary frontend:** Vue 3 and TypeScript  
**Primary backend:** ASP.NET Core and C#  
**Optional native component:** C++ edge agent  
**Initial customer:** `anthony-air.nl`  
**Long-term model:** Multiple websites and independently enabled product modules

---

# 1. Product Vision

> InsightFlow gives developers one privacy-aware platform to understand who uses their websites, how the experience performs, what breaks, what changed and what should be improved next.

The platform starts with **Product Analytics**. Other capabilities are introduced as independent modules later:

- performance monitoring;
- error tracking;
- deployment intelligence;
- conversions and funnels;
- SEO and content quality;
- heatmaps and session insights;
- AI-generated insights;
- uptime and alerts;
- multi-site and team management;
- an optional C++ host agent.

This keeps the project useful immediately while allowing each new learning topic to become a separate release.

---

# 2. Important Accuracy Principle

“True analytics” does not mean perfect knowledge of every visitor.

JavaScript can be blocked, requests can fail, privacy choices can disable tracking, bots can imitate humans and multiple devices cannot safely be assumed to be one person. Therefore the platform must expose:

- documented metric definitions;
- raw accepted-event counts;
- processed-event counts;
- duplicate and bot classifications;
- sampling state;
- data-quality warnings;
- privacy and consent configuration.

The goal is **auditable analytics**, not false precision.

---

# 3. Architecture Strategy

## 3.1 The recommended model

Use a **service-oriented modular platform in one monorepo**.

Each product module must:

1. own its domain rules;
2. own its database or database schema;
3. expose an API or publish events;
4. never read another module's tables directly;
5. be independently buildable and deployable;
6. tolerate another module being disabled;
7. use versioned contracts;
8. include its own tests and observability.

## 3.2 Logical service versus deployment unit

Do not create a new production container for every small class.

A boundary becomes a separate service when it needs at least one of these:

- independent scaling;
- independent release timing;
- different storage technology;
- different security boundary;
- failure isolation;
- a separate learning objective;
- clear ownership of high-volume processing.

This means the architecture stays loosely coupled without becoming operationally unmanageable.

## 3.3 Data ownership rule

**No shared business database.**

PostgreSQL may host several physical databases on one server, and ClickHouse may host several logical schemas on one server, but only the owning service may access its tables.

Cross-module information moves through:

- synchronous read APIs;
- Dapr service invocation;
- versioned pub/sub events;
- materialized summaries explicitly published for other modules.

## 3.4 Self-hosted and Azure portability

Use Dapr for service invocation and pub/sub where it adds real value.

Suggested runtime mapping:

| Capability | Self-hosted | Azure |
|---|---|---|
| Containers | Docker Compose | Azure Container Apps |
| Pub/Sub | RabbitMQ | Azure Service Bus |
| Relational data | PostgreSQL | Azure Database for PostgreSQL or containerized PostgreSQL for learning |
| Analytics data | ClickHouse | Self-managed ClickHouse container initially |
| Object storage | MinIO | Azure Blob Storage |
| Secrets | Docker secrets/environment files | Azure Key Vault and managed identity |
| Observability | OpenTelemetry + Grafana stack | Application Insights and Log Analytics |
| Identity | Microsoft Entra ID | Microsoft Entra ID |

The application code should not need a rewrite when switching brokers.


```text
Tracked websites / agents
          │
          ▼
┌──────────────────────┐
│ Event Collector       │  Fast validation; no analytics queries
└──────────┬───────────┘
           │ versioned CloudEvents
           ▼
┌──────────────────────┐
│ Dapr Pub/Sub          │  RabbitMQ locally; Azure Service Bus in Azure
└───┬────────┬─────────┘
    │        │
    ▼        ▼
Analytics   Performance   Error   Session   Future processors
Service     Service       Service Service
    │          │            │       │
    └──────┬───┴──────┬─────┴───────┘
           │ read APIs / summary events
           ▼
┌──────────────────────┐
│ API Gateway / BFF     │
└──────────┬───────────┘
           ▼
┌──────────────────────┐
│ Vue 3 Dashboard       │
└──────────────────────┘

Shared control plane:
Identity & Workspace Service ─ Site Registry Service ─ Deployment Service

Independent consumers:
Conversion Service ─ AI Insights Service ─ Alerting Service ─ Notification Service
```


---

# 4. Core Platform Services

| Service | Responsibility | Data ownership |
|---|---|---|
| Web Dashboard | Vue interface and module navigation | No business data |
| API Gateway / BFF | Authenticated public read API and response composition | No module data |
| Identity & Workspace | Users, memberships and roles | Identity PostgreSQL database |
| Site Registry | Sites, domains, keys, settings and enabled modules | Site PostgreSQL database |
| Event Collector | Fast browser and agent event acceptance | Temporary buffers only |
| Analytics Service | Page views, sessions, visitors, acquisition and events | Analytics ClickHouse schema |
| Performance Service | Web Vitals and timing distributions | Performance ClickHouse schema |
| Error Service | Error occurrences and issue lifecycle | Error PostgreSQL + ClickHouse |
| Deployment Service | Releases and deployments | Deployment PostgreSQL database |
| Conversion Service | Goals, funnels and attribution | Conversion configuration + rollups |
| SEO Scanner | Crawls and technical content checks | SEO metadata + object reports |
| Session Insights | Click and scroll aggregates; optional replay | ClickHouse + object storage |
| AI Insights | Claude tools, generated insights and audit | AI PostgreSQL database |
| Alerting | Rules, alert state and evaluations | Alert PostgreSQL database |
| Notification | Email, Teams and webhook delivery | Delivery log |
| C++ Edge Agent | Optional host and endpoint telemetry | Local encrypted buffer |

---

# 5. Product Roadmap

The roadmap is **dependency-driven, not strictly date-driven**.

After M00, you may build M02, M03, M04, M06 or M09 before another module. M05 and M07 need analytics events. M08 becomes valuable only when at least one data module contains enough real data.

| ID | Module | Suggested duration | Dependency | Can be scheduled |
|---|---|---|---|---|
| M00 | Platform Foundation | 2–3 weeks | None | Immediately |
| M01 | Product Analytics | 4 weeks | M00 | After Platform Foundation |
| M02 | Performance Monitoring | 3–4 weeks | M00; M01 optional | Any time after Foundation |
| M03 | Error Tracking | 3–4 weeks | M00 | Any time after Foundation |
| M04 | Deployment Intelligence | 2–3 weeks | M00 | Any time after Foundation |
| M05 | Goals and Funnels | 3 weeks | M01 | After Product Analytics |
| M06 | SEO and Content Quality | 3–4 weeks | M00 | Any time after Foundation |
| M07 | Heatmaps and Session Insights | 4–6 weeks | M01 | After Product Analytics |
| M08 | AI Insights | 3–5 weeks | At least one data module | After useful data exists |
| M09 | Alerts and Uptime | 3–4 weeks | M00; data modules optional | Any time after Foundation |
| M10 | Multi-site and Team Hardening | 3–4 weeks | M00 | When sharing the platform |
| M11 | C++ Edge Agent | 3–4 weeks | M00; M09 recommended | Optional specialist module |

---

# 6. Recommended Release Sequence

## Release 0 — Platform Foundation

Create the control plane, local infrastructure and service contracts.

## Release 1 — Product Analytics

Install the tracker on the portfolio and start collecting genuine traffic.

## Release 2 — Performance Monitoring

Use the same collector and platform shell, but create a separate performance service.

## Release 3 — Error Tracking or Deployment Intelligence

Choose based on what you want to learn:

- **Error Tracking:** deeper backend processing and grouping;
- **Deployment Intelligence:** CI/CD integrations and cross-module composition.

## Later releases

Select modules based on interest and portfolio value rather than treating the plan as one enormous sequential assignment.

---

# 7. Module Plans

## M00 — Platform Foundation

**Suggested duration:** 2–3 weeks  
**Dependencies:** None  
**Scheduling:** Immediately

### Goal

Create the shared platform capabilities that every later product module can use without sharing business logic or databases.

### Deliverables

- Monorepo and service templates
- Vue 3 platform shell and API gateway/BFF
- Identity, workspace and site registration
- Versioned event contracts
- Dapr-based pub/sub abstraction
- PostgreSQL, ClickHouse and object-storage development environment
- OpenTelemetry-based logs, metrics and traces
- Docker Compose self-hosted deployment
- Azure Container Apps and Bicep baseline
- CI validation and deployment workflows

### Module acceptance criteria

- A user can sign in and register a website
- Each service can be built and deployed independently
- Services exchange versioned events without direct database access
- The local stack can be started from documented commands
- A minimal Azure development environment can be deployed from Bicep
## M01 — Product Analytics

**Suggested duration:** 4 weeks  
**Dependencies:** M00  
**Scheduling:** After Platform Foundation

### Goal

Measure real page views, sessions, visitors, acquisition sources, devices, countries and custom product events.

### Deliverables

- Small JavaScript/TypeScript tracking SDK
- High-throughput collection endpoint
- Page-view, session and custom-event processing
- Bot and duplicate filtering
- ClickHouse analytics model and rollups
- Overview, pages, sources, technology and geography dashboards
- Live visitors view
- Retention and privacy controls
- Installation guide for anthony-air.nl

### Module acceptance criteria

- The portfolio sends real analytics events
- Events survive temporary processor failures
- Dashboard metrics have documented definitions
- A visitor can be excluded through privacy controls
- Raw and processed event counts can be reconciled
## M02 — Performance Monitoring

**Suggested duration:** 3–4 weeks  
**Dependencies:** M00; M01 optional  
**Scheduling:** Any time after Foundation

### Goal

Measure real-user performance and show where pages, devices or releases are slow.

### Deliverables

- Web Vitals and navigation timing collection
- Performance processing service
- Percentile-based aggregations
- Page, device and release comparison views
- Performance budget configuration
- Regression detection

### Module acceptance criteria

- LCP, INP, CLS and TTFB can be queried by page and device
- Slow samples are traceable without storing sensitive page content
- Performance regressions can be compared against a prior period or release
## M03 — Error Tracking

**Suggested duration:** 3–4 weeks  
**Dependencies:** M00  
**Scheduling:** Any time after Foundation

### Goal

Capture browser and API errors, group similar failures and connect them to affected pages and releases.

### Deliverables

- Browser error SDK integration
- Unhandled exception and promise rejection capture
- Error grouping and fingerprinting
- Source-map upload workflow
- Issue lifecycle and assignment
- Error trends and affected-user dashboard

### Module acceptance criteria

- Repeated occurrences group into one issue
- Sensitive values are scrubbed before storage
- A release can be associated with newly introduced errors
## M04 — Deployment Intelligence

**Suggested duration:** 2–3 weeks  
**Dependencies:** M00  
**Scheduling:** Any time after Foundation

### Goal

Record deployments and correlate product, performance and error changes with a release.

### Deliverables

- GitHub Actions and generic webhook ingestion
- Release and deployment data model
- Deployment timeline
- Version propagation through SDK events
- Before/after comparison API
- Release health summary

### Module acceptance criteria

- Each production deployment appears in the platform
- Analytics, performance and errors can be filtered by release
- A release comparison uses explicit time windows and metric definitions
## M05 — Goals and Funnels

**Suggested duration:** 3 weeks  
**Dependencies:** M01  
**Scheduling:** After Product Analytics

### Goal

Measure conversions such as project views, CV downloads, contact actions and multi-step journeys.

### Deliverables

- Goal definition interface
- Event and page-based goals
- Ordered funnel engine
- Conversion and drop-off dashboards
- Segmentation by source, device and campaign
- Goal history and versioning

### Module acceptance criteria

- A goal can be configured without redeploying the tracked website
- Funnels clearly define ordering and attribution windows
- Conversion values reconcile with underlying events
## M06 — SEO and Content Quality

**Suggested duration:** 3–4 weeks  
**Dependencies:** M00  
**Scheduling:** Any time after Foundation

### Goal

Inspect crawlability, metadata, links and technical SEO without coupling this work to analytics.

### Deliverables

- Scheduled site crawler
- Metadata and canonical checks
- Broken-link detection
- Sitemap and robots.txt validation
- Lighthouse CLI worker
- SEO issue dashboard and history

### Module acceptance criteria

- A registered site can be crawled safely with limits
- Findings include evidence and affected URLs
- Historical scores can be compared between scans
## M07 — Heatmaps and Session Insights

**Suggested duration:** 4–6 weeks  
**Dependencies:** M01  
**Scheduling:** After Product Analytics

### Goal

Understand interaction patterns through privacy-aware click and scroll heatmaps, with session replay as an optional later slice.

### Deliverables

- Normalized click-coordinate collection
- Scroll-depth aggregation
- Responsive viewport grouping
- Click and scroll heatmap renderer
- Aggressive input and text masking
- Optional session replay proof of concept

### Module acceptance criteria

- Heatmaps work across different viewport sizes
- Form values and sensitive text are never collected
- Storage and sampling limits can be configured per site
## M08 — AI Insights

**Suggested duration:** 3–5 weeks  
**Dependencies:** At least one data module  
**Scheduling:** After useful data exists

### Goal

Use Claude to explain changes and suggest actions through controlled, typed analytics tools rather than unrestricted database access.

### Deliverables

- AI provider abstraction
- Typed metric-query tools
- Weekly insight generation
- Period and release comparison
- Anomaly explanation workflow
- Prompt versioning, audit and usage limits

### Module acceptance criteria

- AI answers cite the metrics and periods used
- Claude cannot query arbitrary tables or modify analytics data
- Invalid tool arguments and unsupported conclusions are rejected
## M09 — Alerts and Uptime

**Suggested duration:** 3–4 weeks  
**Dependencies:** M00; data modules optional  
**Scheduling:** Any time after Foundation

### Goal

Notify users about downtime, regressions, traffic anomalies and error spikes.

### Deliverables

- Scheduled uptime checks
- Rule-based alert engine
- Email, Teams and webhook channels
- Silencing and cooldown rules
- Alert history and acknowledgement
- Cross-module metric thresholds

### Module acceptance criteria

- Alerts are deduplicated and rate limited
- Failed notifications are retried
- Rules can reference metrics without directly coupling services
## M10 — Multi-site and Team Hardening

**Suggested duration:** 3–4 weeks  
**Dependencies:** M00  
**Scheduling:** When sharing the platform

### Goal

Turn the personal platform into a safe multi-site product with teams, roles, quotas and lifecycle controls.

### Deliverables

- Workspace roles and invitations
- Per-site API keys and key rotation
- Usage quotas and sampling controls
- Data export and deletion workflows
- Per-module access policies
- Backup, restore and upgrade documentation

### Module acceptance criteria

- One workspace cannot access another workspace's data
- Keys can be rotated without service interruption
- Data can be exported and deleted by site
## M11 — C++ Edge Agent

**Suggested duration:** 3–4 weeks  
**Dependencies:** M00; M09 recommended  
**Scheduling:** Optional specialist module

### Goal

Learn C++ by creating a lightweight host agent that sends system health and availability observations.

### Deliverables

- Cross-platform C++ agent
- CPU, memory and disk metrics
- HTTP and TCP health probes
- Secure enrollment and configuration
- Buffered offline delivery
- Agent status dashboard

### Module acceptance criteria

- The agent reconnects after network loss
- Credentials are stored and transmitted securely
- The module remains optional for normal website analytics


---

# 8. Event and Contract Rules

## 8.1 Envelope

Every published event should contain:

```json
{
  "id": "globally-unique-event-id",
  "type": "analytics.page-view.received.v1",
  "source": "insightflow.collector",
  "subject": "sites/site-id",
  "time": "2026-08-06T14:00:00Z",
  "tenantId": "workspace-id",
  "siteId": "site-id",
  "correlationId": "request-or-session-correlation-id",
  "dataVersion": 1,
  "data": {}
}
```

## 8.2 Compatibility

- Additive fields are preferred.
- Consumers must ignore unknown fields.
- Breaking payload changes require a new event version.
- A service may support multiple input versions during migration.
- Events describe completed facts, not remote commands.
- Commands use explicit APIs or dedicated command topics.
- Every consumer must be idempotent.

## 8.3 Initial event catalogue

| Event | Publisher | Subscribers |
|---|---|---|
| `site.created.v1` | Site Registry | Module services |
| `site.settings.changed.v1` | Site Registry | Collector and module services |
| `analytics.page-view.received.v1` | Collector | Analytics Processor |
| `analytics.custom-event.received.v1` | Collector | Analytics and Conversion |
| `analytics.processed.v1` | Analytics | Conversion, Alerting and AI |
| `performance.sample.received.v1` | Collector | Performance |
| `error.occurrence.received.v1` | Collector | Error Service |
| `error.issue.changed.v1` | Error Service | Alerting and AI |
| `deployment.completed.v1` | Deployment Service | Analytics, Performance, Errors and AI |
| `seo.scan.completed.v1` | SEO Scanner | Alerting and AI |
| `insight.generated.v1` | AI Insights | Dashboard and Notification |
| `notification.requested.v1` | Any module | Notification Service |

---

# 9. Storage Strategy

## PostgreSQL

Use for:

- users and workspaces;
- site configuration;
- roles and keys;
- goal definitions;
- issue lifecycle;
- deployments;
- alert rules;
- audit records.

## ClickHouse

Use for:

- page-view events;
- session and visitor aggregates;
- performance samples;
- error occurrences;
- interaction samples;
- time-series and high-cardinality breakdowns.

## Object storage

Use for:

- source maps;
- Lighthouse reports;
- exports;
- optional replay chunks;
- large diagnostic artifacts.

## Redis

Use only when needed for:

- short-lived live visitor projections;
- distributed rate limits;
- caches;
- ephemeral locks.

Redis must not become an undocumented source of truth.

---

# 10. Privacy and Data Governance

Privacy is a product requirement, not a later legal checkbox.

## Default principles

- no advertising profiles;
- no cross-site tracking;
- no fingerprinting;
- no raw IP retention by default;
- strip sensitive query parameters;
- never collect form values;
- use short retention for interaction data;
- allow per-site opt-out and consent integration;
- support data deletion and export;
- show exactly what the SDK sends in debug mode.

For Dutch deployment, verify the final implementation and cookie/identifier choices against current guidance before launch. The architecture should allow a cookie-free limited mode and a consent-aware persistent mode rather than assuming one setup is lawful for every website.

---

# 11. Repository Structure

```text
insightflow/
├── apps/
│   └── dashboard-web/
├── gateway/
│   └── InsightFlow.Gateway/
├── services/
│   ├── identity-workspace/
│   ├── site-registry/
│   ├── event-collector/
│   ├── analytics/
│   ├── performance/
│   ├── errors/
│   ├── deployments/
│   ├── conversions/
│   ├── seo/
│   ├── session-insights/
│   ├── ai-insights/
│   ├── alerting/
│   └── notifications/
├── agents/
│   └── edge-agent-cpp/
├── packages/
│   ├── browser-sdk/
│   ├── event-contracts/
│   ├── dotnet-service-defaults/
│   └── test-fixtures/
├── infrastructure/
│   ├── compose/
│   ├── dapr/
│   ├── bicep/
│   └── observability/
├── docs/
│   ├── architecture/
│   ├── adr/
│   ├── contracts/
│   ├── privacy/
│   └── runbooks/
└── tests/
    ├── contract/
    ├── integration/
    ├── load/
    └── e2e/
```

---

# 12. Asana Board Design

## 12.1 Recommended hierarchy

```text
Project
└── Module section
    ├── Module milestone
    ├── Epic
    │   ├── Task
    │   └── Task
    └── Epic
```

Use **Sections** for product modules rather than workflow state. Use a custom `Status` field for workflow.

## 12.2 Recommended custom fields

| Field | Values |
|---|---|
| Module | M00–M11 |
| Work Type | Milestone, Epic, Task, Bug, Spike, ADR |
| Status | Backlog, Ready, In Progress, Review, Blocked, Done |
| Priority | Critical, High, Medium, Low |
| Estimate | Hours or development days |
| Release | Foundation, Analytics, Performance, Errors, etc. |
| Risk | Low, Medium, High |
| Architecture Area | UI, API, Worker, Data, Messaging, Infrastructure, Security, AI |

## 12.3 Useful Asana views

- **Board:** grouped by Status;
- **List:** grouped by Module;
- **Timeline:** filtered to Ready and In Progress;
- **Milestones:** module release checkpoints;
- **Architecture:** filtered by Work Type = ADR or Spike;
- **Current Release:** filtered by one module.

## 12.4 Task naming convention

```text
[M01][SDK] Implement SPA page-view tracking
[M01][API] Validate site key and origin
[M02][DATA] Add performance percentile rollups
[M08][AI] Implement typed metric tools
```

The supplied CSV uses readable names without mandatory prefixes, because Module and Work Type are separate fields. Prefixes can be added later if your Asana plan has limited custom-field support.

---

# 13. Definition of Ready

A task is ready when:

- its purpose is clear;
- one service owns it;
- acceptance criteria are stated;
- input and output contracts are known;
- privacy impact has been considered;
- authorization requirements are known;
- dependencies are linked;
- the test approach is identified.

---

# 14. Definition of Done

A feature is done only when:

- acceptance criteria pass;
- service ownership remains clear;
- no service reads another service's database;
- contracts are versioned;
- unit or integration tests exist;
- logs, metrics and traces are added;
- authorization and privacy are reviewed;
- failure and retry behavior are implemented;
- documentation and runbooks are updated;
- local self-hosting still works;
- CI passes;
- the feature is demonstrated with real or deterministic test data.

---

# 15. First Practical Development Slice

Do not begin by building every shared service in full.

Build this vertical slice:

1. Register one site.
2. Generate one publish-only site key.
3. Load the TypeScript SDK on a local Vue page.
4. Track one page view.
5. Validate it in the collector.
6. Publish it through Dapr.
7. Process it in the Analytics Service.
8. Store it in ClickHouse.
9. Query the count through the Analytics Query API.
10. Display it in the Vue dashboard.
11. Trace the event end to end.
12. Repeat the same event ID and prove it is not counted twice.

After that slice works, expand the analytics module instead of building unused infrastructure.

---

# 16. Included Planning Artifacts

The accompanying workbook contains:

- the product roadmap;
- an Asana-ready backlog;
- the service catalogue;
- event contracts;
- suggested board fields.

The CSV is ordered so parent Epic tasks appear before their imported subtasks.
