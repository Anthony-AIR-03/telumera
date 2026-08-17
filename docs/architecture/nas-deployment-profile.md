# NAS Deployment Profile

> Status: Draft (M00.3). Describes how the local self-hosted runtime
> (`infrastructure/compose/`) differs when deployed to the owner's NAS for `anthony-air.nl`
> (`docs/architecture/vision-and-scope.md` §2). Not deployed yet — this is the plan to deploy against once
> there's an actual service worth exposing (M00.4+). No reverse proxy or TLS tooling was decided anywhere
> else in the project's docs before this file; the choices below are a recommendation, not a constraint
> inherited from an earlier decision.

## 1. Scope

`infrastructure/compose/docker-compose.yml` is built for local development: every port is published to
`localhost`, there are no resource limits, and there's no TLS or reverse proxy — anyone with access to the
developer's own machine can already reach everything. None of that is acceptable once the same stack runs
on a NAS reachable from the internet. This document is the delta between "local dev" and "NAS", not a
replacement compose file — see §6 for why.

## 2. Topology assumptions

- Single physical NAS running Docker + Compose v2 — the NAS OS itself (a vendor NAS OS or a small
  self-built box) is irrelevant as long as that holds; this document makes no OS-specific assumptions.
  Same portability target as local dev
  (`docs/adr/0002-dapr-pubsub-abstraction.md`'s "Docker Compose" row). No Kubernetes, no multi-node —
  matches "one physical server per engine" from `docs/adr/0005-context-level-data-isolation.md`.
- The NAS is very likely **not dedicated to Telumera** — it plausibly already runs other Docker workloads
  (a personal site, game servers, other self-hosted apps) with their own reverse proxy and container
  network. Telumera's compose override must be additive: its own service names on `telumera-net`, its own
  subdomain/host-header route through whatever already owns 80/443 (or whatever ingress mechanism is
  already in place — see below), and no assumption that container/network names or those ports are unclaimed.
  Check what's already running on the actual NAS (`docker ps`, `docker network ls`) before assuming a name
  is free.
- One public domain/subdomain pointed at the NAS (e.g. a subdomain of `anthony-air.nl`), via whatever DNS
  provider anthony-air.nl already uses.
- The NAS sits behind a home/office router the owner controls. Two ingress patterns are both valid; which
  one fits depends on the specific router/ISP/NAS and what's already running there:
  - **Port-forwarding** 80/443 to the NAS's reverse proxy (see §4) — works when the router allows
    forwarding and the ISP doesn't put the connection behind CGNAT.
  - **Tunnel-based ingress** (e.g. Cloudflare Tunnel, Tailscale Funnel) — an outbound-only connection from
    a lightweight agent on the NAS to the tunnel provider, which proxies public HTTPS traffic in. No
    inbound port-forward is needed at all, which sidesteps CGNAT entirely and keeps the router's forwarding
    table empty. If a tunnel is already the ingress pattern for another site on the same NAS, prefer adding
    Telumera's subdomain to that existing tunnel over opening a parallel port-forward path — one ingress
    mechanism per NAS is easier to reason about than two overlapping ones.

## 3. Network exposure

Only a reverse proxy is reachable from outside the NAS's own Docker network. Everything else —
PostgreSQL, ClickHouse, RabbitMQ (AMQP + management UI), Redis, MinIO, Dapr sidecars/placement — stays on
the internal Compose network with **no host port publishing**, unlike local dev where every port is
published to `localhost` for debugging convenience. Concretely, the NAS profile drops every `ports:` entry
in `docker-compose.yml` except the reverse proxy's `80`/`443`, and reaches internal services only via
their Compose service name (already how services reach each other locally, since they're all on
`telumera-net`).

Router-level port forwarding is limited to `80` and `443` → the NAS's reverse proxy. If the NAS ships its
own admin UI (Synology DSM, QNAP QTS, etc.), that stays off the forwarded ports entirely — it's managed
over the LAN/VPN, never exposed alongside Telumera.

## 4. Reverse proxy and TLS

**Recommendation: [Caddy](https://caddyserver.com/).** Reasoning, for a single-operator NAS deployment
specifically:

- Automatic HTTPS (ACME/Let's Encrypt) with zero manual certificate handling — the smallest operational
  surface for one person maintaining this alongside everything else.
- Config is a short, readable `Caddyfile` rather than a templated nginx config + a separate certbot
  container/cron to keep in sync.
- Handles HTTP→HTTPS redirect and certificate renewal without extra containers.

Sketch (one entry per exposed host — today that's nothing, since `gateway/` doesn't exist yet; this is
what it looks like once it does):

```
telumera.anthony-air.nl {
    reverse_proxy gateway:8080
}
```

**Certificate issuance mode** depends on whether port 80 can actually be forwarded:

- **HTTP-01 (default, if port 80 is forwardable):** Caddy answers the ACME challenge directly on port 80.
  Simplest option — use it unless CGNAT rules it out.
- **DNS-01 (if the NAS is behind CGNAT or port 80 isn't available):** Caddy proves domain ownership via a
  DNS TXT record instead, using a DNS provider plugin for whatever anthony-air.nl's DNS is hosted on. This
  needs an API token for that DNS provider stored the same way as every other NAS secret (§5) — never
  committed.

Either way, only the reverse proxy container terminates TLS; traffic to `gateway` (and any other internal
service) stays on the internal Docker network.

**If tunnel-based ingress is used instead of port-forwarding** (§2): TLS may already terminate at the
tunnel provider's edge, with traffic reaching the NAS over the tunnel's own encrypted transport rather than
arriving as public HTTPS on 80/443. In that mode:

- Caddy's role shifts from *public TLS terminator* to *internal router* — the tunnel daemon forwards
  requests to it (typically plain HTTP over the Docker network), and Caddy still does the host-header-based
  `reverse_proxy` to `gateway:8080`, just without needing its own ACME certificate for the public hostname.
- The HTTP-01/DNS-01 choice above doesn't apply, since nothing needs to answer a public ACME challenge on
  80 — the tunnel provider's own certificate covers the public hostname.
- If the NAS already runs a GUI-managed reverse proxy for other sites (e.g. Nginx Proxy Manager), adding a
  proxy host there for Telumera's subdomain is a valid alternative to introducing Caddy as a second reverse
  proxy — evaluate against what's already running on the actual NAS rather than defaulting to Caddy.
- Whether to reuse an existing tunnel/proxy or run a second one is a call to make against whatever's
  already deployed on the NAS — see §2.

## 5. Secrets

Same mechanism as local dev — Docker secrets/environment files, per
`docs/adr/0002-dapr-pubsub-abstraction.md`'s portability table — but **different values**. The NAS's
`.env` is never the developer's local `.env`, is never committed, and lives only on the NAS (or in a
password manager the owner controls). If DNS-01 is used (§4), the DNS provider's API token goes in the
same file, under the same "never committed" rule. This document doesn't change the mechanism, just states
explicitly that "local secrets" and "NAS secrets" are two different files with two different sets of
values — see `docs/runbooks/local-environment.md`'s secrets model for the local half.

## 6. Resource limits

Local dev intentionally has none — the priority there is fast iteration, not resource discipline. On a
NAS shared with the owner's other workloads (file serving, other containers), each service should get a
`deploy.resources.limits` (or plain `mem_limit`/`cpus` under Compose v2, which honors them without
Swarm). Starting points, generous enough not to cause spurious OOM kills but bounded enough that one
runaway container can't starve the NAS:

| Service | Memory limit | Notes |
|---|---|---|
| `postgres` | 512 MB | Low volume at this project's current scale (transactional/config data only) |
| `clickhouse` | 1–2 GB | The most memory-hungry component once real event volume exists; revisit once M01 ships |
| `rabbitmq` | 256 MB | |
| `redis` | 128 MB | Ephemeral/derived state only (`docs/adr/0003-postgresql-plus-clickhouse.md`) — never sized for durability |
| `minio` | 256 MB | |
| Dapr sidecars | 128 MB each | One per service, so this adds up — factor into the total as services are added |

These are starting points to tune against the actual NAS's available RAM once it's chosen, not
benchmarked numbers.

## 7. Backups

Nothing here is automated yet — this is the inventory of what needs a backup story before the NAS
deployment is real, matching the Definition of Done's "documentation and runbooks are updated" bar
(`planning/Telumera_Modular_Project_Plan.md` §14):

| Data | Mechanism | Notes |
|---|---|---|
| PostgreSQL (all `telumera_*` databases) | `pg_dump`/`pg_dumpall`, scheduled | Per-context databases (`docs/adr/0005`) back up independently — a context can be restored without touching another's data |
| ClickHouse (all `telumera_*` databases) | `clickhouse-backup` or `BACKUP` statement to the MinIO bucket below | Same per-context independence as Postgres |
| RabbitMQ | Definitions export (exchanges/queues/users), not message bodies | Messages are transient by design (ADR 0002) — a lost in-flight message during a backup window is not a data-loss event the way a lost Postgres row would be |
| MinIO | Object storage itself needs its own backup target (a second disk, or off-NAS) — it can't back itself up | Bucket-per-context convention (ADR 0005) applies here too |
| `infrastructure/`, `docs/`, `.env` (NAS-specific) | Git (everything except `.env`) + a password manager for `.env` | The compose/Dapr config is already versioned; only the NAS's actual secret values need a separate backup story |

Retention policy, backup destination (a second NAS volume vs. off-site), and restore testing are
deliberately left open — revisit once there's real user data at stake, i.e. once M01 actually ships
events into this stack.

## 8. Why this isn't a second compose file (yet)

A `docker-compose.nas.yml` override is the obvious next artifact, but writing one now would mean
maintaining an override for a deployment target with no services running behind it (`gateway/` and
`services/*` are still empty — see `infrastructure/compose/README.md`) and no way to test it against a
real NAS from this environment. This document captures the decisions (reverse proxy choice, TLS mode,
network exposure, resource limits, backup inventory) so the override file is a mechanical translation of
already-made decisions when it's actually needed — expected around the time `gateway/` exists (M00.4) and
there's a first real vertical-slice deployment target (`docs/architecture/vision-and-scope.md` §7).
