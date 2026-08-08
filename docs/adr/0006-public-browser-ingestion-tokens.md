# ADR 0006: Public Browser Ingestion Tokens

- **Status:** Accepted
- **Date:** 2026-08-07
- **Related:** `docs/architecture/bounded-contexts-and-data-ownership.md`, `docs/architecture/vision-and-scope.md` §7

## Context

The Site Registry issues a per-site "SDK key" / "publish-only site key" that the browser SDK embeds and
sends with every event. Because this key is loaded into a script running in a public website's page, it
is visible to any visitor who opens their browser's network tab or views page source — it cannot be
treated as, or documented as if it were, a secret. The existing "publish-only" framing already implies
restricted capability, but nothing currently states explicitly that this credential is public by design
and must never be handled with secret-credential practices (e.g. it doesn't need rotation-on-suspected-leak
in the same way a real secret would, because it was never confidential to begin with).

This is a distinct credential class from anything used for server-side integrations (the C++ edge agent,
server-side SDKs, CI/deployment webhooks, administrative API access), which do need real secrets.

## Decision

Two explicitly separate credential classes:

### Browser site token (public)

- Public by design — treated the same as a public API key, never as a secret.
- Identifies a site, nothing more.
- Can only submit telemetry (write-only from the token's perspective) — it grants no read access to
  analytics data and no administrative capability.
- Protected by **origin validation, payload validation, rate limits/quotas, and abuse controls**, not by
  the token's confidentiality. Origin validation is a useful signal but must not be documented or relied
  on as strong authentication — a non-browser client can forge an `Origin`/`Referer` header, so origin
  checks are a defense-in-depth layer against casual misuse, not an access-control guarantee.
- Naming: referred to consistently as a **browser site token** / **public ingestion token** in docs and
  code — "SDK key" is avoidable because "key" implies a secret in most engineers' default mental model.
- Rotation exists (`site.key.rotated.v1`, per the Site Registry event list in
  `docs/architecture/bounded-contexts-and-data-ownership.md`) for operational reasons (a site owner wants
  to invalidate an old embed, e.g. after removing the SDK from a decommissioned page) — not because the
  token being observed by a visitor is treated as a leak/incident.

### Secret credentials

Real secrets, issued and stored per Telumera's normal secret-handling practice (Docker
secrets/environment files self-hosted, Azure Key Vault + managed identity on Azure — per
`docs/adr/0002-dapr-pubsub-abstraction.md`), used for:

- C++ Edge Agent enrollment (M11);
- server-side SDKs;
- CI/deployment webhooks (release/deployment reporting);
- administrative/dashboard-adjacent integrations.

These are never embedded in browser-delivered code.

## Consequences

- **Positive:** removes the risk of someone treating the browser token as confidential and building
  security controls around it that don't actually hold (e.g. "rotate immediately if it appears in a
  public repo" — it's expected to be publicly visible, that's not an incident).
- **Positive:** makes the actual abuse-resistance requirement explicit and correctly placed — quotas,
  payload validation, and origin checks at the Collector, not credential secrecy.
- **Negative:** requires the Collector (and any rate-limiting/abuse-control layer in front of it) to be
  built with the assumption that the token *will* leak/be observed on day one, rather than deferring abuse
  controls until "if the key ever leaks."
