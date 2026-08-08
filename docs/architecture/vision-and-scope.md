# Telumera — Product Vision and Scope

> Status: Draft (M00.1)
> Codename in earlier planning material: "InsightFlow" — Telumera is the project's real name.

## 1. Vision

Telumera gives developers one privacy-aware platform to understand who uses their websites, how the
experience performs, what breaks, what changed, and what should be improved next.

It starts as a single module — **Product Analytics** — and grows into a self-hosted, modular
developer-intelligence platform by adding independent capabilities over time:

- performance monitoring
- error tracking
- deployment intelligence
- conversions and funnels
- SEO and content quality
- heatmaps and session insights
- AI-generated insights
- uptime and alerts
- multi-site and team management
- an optional C++ host agent

Each capability is scoped as its own module (see the module roadmap, M00–M11, in
`planning/Telumera_Modular_Project_Plan.md` §5–7) so the project stays useful after the very first
release instead of requiring one large monolithic build before anything is usable.

## 2. Target users

- **Primary (near-term):** the project owner, self-hosting Telumera to monitor `anthony-air.nl`. This is
  simultaneously the first real customer and the environment the platform is developed against.
- **Secondary (long-term):** developers/small teams who want a self-hosted, privacy-first alternative to
  bundled commercial analytics/observability/error-tracking tools, and are willing to run PostgreSQL,
  ClickHouse, and a message broker themselves (or deploy to Azure).

## 3. Core problem

Commercial analytics and observability tools force a choice between:

- privacy-invasive defaults (cross-site tracking, fingerprinting, ad-network data sharing), or
- fragmented tooling (a separate vendor each for analytics, performance, errors, uptime, SEO), or
- opaque metrics with no way to audit what a number actually means or how confident to be in it.

Telumera's core bet is that a single, modular, self-hosted platform can give developers analytics they
can both trust and fully own — at the cost of running (or paying to run) the infrastructure themselves.

## 4. Non-goals

- **Not** aiming for perfect visitor-level precision. See the Accuracy Principle below — this is a
  permanent constraint on the product, not a temporary limitation to engineer away.
- **Not** an advertising or marketing-attribution platform. No cross-site identity graphs, no ad-network
  integrations, no fingerprinting-based deduplication.
- **Not** a general-purpose BI tool. Metrics are scoped to what each module explicitly defines and
  documents — Telumera does not expose ad-hoc querying of raw warehouse tables to end users.
- **Not** initially multi-tenant-hardened. Multi-site/team support is module M10, scheduled once the
  platform is shared beyond a single owner — earlier modules assume a single workspace is the common case
  but must not make multi-workspace support architecturally impossible later.

## 5. Accuracy principle (binding constraint on every module)

"True analytics" does not mean perfect knowledge of every visitor. JavaScript can be blocked, requests
can fail, privacy choices can disable tracking, bots can imitate humans, and multiple devices cannot
safely be assumed to belong to one person.

Every module that reports a metric must therefore expose, alongside the metric itself:

- a documented definition of what the metric means and how it's calculated
- raw accepted-event counts vs. processed-event counts
- duplicate and bot classifications (marked, not silently dropped)
- sampling state, when sampling is active
- data-quality warnings when confidence is reduced
- the privacy/consent configuration in effect for the data shown

The goal is **auditable analytics** — every number must be traceable back to what was actually collected
and what was filtered out and why — not false precision.

## 6. Relationship between analytics, observability, and AI

These three are treated as complementary lenses on the same underlying event stream, not separate
products bolted together:

- **Analytics modules** (Product Analytics, Conversions, SEO, Heatmaps) answer "what happened and how
  much" — they are the primary data producers.
- **Observability modules** (Performance, Errors, Deployment Intelligence, Alerts/Uptime) answer "is it
  working, and did something just break it" — they consume the same collector/event pipeline as
  analytics but own their own storage and processing (see `docs/adr/0001-service-oriented-modular-platform.md`).
- **AI Insights (M08)** sits on top of both, once at least one data module has enough real data to be
  useful. It never queries raw tables directly — only typed, versioned tools scoped to approved metric
  shapes (see `planning/Telumera_Modular_Project_Plan.md` §7, M08) — so AI-generated conclusions stay
  auditable in the same way every other metric is.

## 7. First practical slice

Before building out shared infrastructure in depth, the first vertical slice to get working end-to-end
(per `planning/Telumera_Modular_Project_Plan.md` §15) is:

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

Everything else in M00 (identity, workspace, CI, Bicep, etc.) exists to make this slice possible safely
and repeatably — it is not the goal in itself.
