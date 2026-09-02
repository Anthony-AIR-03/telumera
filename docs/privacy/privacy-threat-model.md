# Telumera — Privacy Threat Model

> Status: Draft (M00.1)
> Applies platform-wide; each module's own docs should reference this rather than re-deriving privacy
> rules independently.

## Principle

Privacy is a product requirement, not a later legal checkbox. It's designed in at M00, before any
tracking code exists, and every later module (M01–M11) must be checked against it before shipping.

## Threats and identifiers considered

For every category below: what could Telumera collect, why that's risky, and what's already decided as
the mitigation.

| Category | Risk if handled carelessly | Decided mitigation |
|---|---|---|
| **Persistent identifiers** (cookies, device IDs) | Enables cross-session or cross-site tracking of an individual without clear consent | No cross-site tracking; no fingerprinting; visitor identifiers are **short-lived pseudonymous identifiers** (daily or configurable) — pseudonymous, not anonymous: under GDPR this is still personal data, just data that can't be attributed to a specific visitor without additional information Telumera doesn't hold. Persistence beyond the default lifetime is an explicit, documented configuration choice, not a silent default. Each identifier's lifetime, scope (per-site vs. cross-site — cross-site is out of scope per the Non-goals in `docs/architecture/vision-and-scope.md`), reset behavior, and which metrics depend on it must be documented per module before that module ships. |
| **Raw IP addresses** | Precise geolocation and a strong quasi-identifier if retained | No raw IP retention by default; IP is used only transiently for coarse enrichment (e.g. country/region) then discarded |
| **Referrers and URLs** | Can leak session tokens, search queries, or other sensitive query-string content embedded in a URL | Strip sensitive query parameters before storage; document an allowlist-based query-string policy per module (e.g. Analytics' campaign-parameter allowlist) rather than storing full raw URLs |
| **Form values / DOM content** (relevant to Session Insights / Heatmaps, M07) | Could capture passwords, personal data, or any sensitive input the user types | Never collect form values; blocked elements/attributes are defined explicitly before any interaction-collection code ships (M07.1); aggressive input and text masking is a hard requirement, not a toggle |
| **Session replay** (optional, M07.4) | Highest-risk category — a replay can reconstruct much more than an aggregate metric | Treated as a separate, explicitly higher-risk slice: written privacy design and masking behavior required before implementation; not enabled by default; every viewing action is logged; a privacy review happens before release |
| **Device/browser fingerprints** | High-entropy combinations of device signals can re-identify a visitor even without cookies | Use bounded categories (e.g. "mobile / Chrome / Android") rather than storing full high-entropy fingerprint strings |
| **Bot and duplicate traffic** | Inflates/corrupts metrics, and can look like a privacy issue if bot traffic is mistaken for real visitors | Bot/duplicate events are marked, not silently deleted, so data quality is inspectable (ties into the Accuracy Principle in `docs/architecture/vision-and-scope.md`) |
| **Consent and opt-out state** | Sending events before consent is confirmed, or ignoring an opt-out, breaks the platform's core privacy promise | The SDK does not send events until the configured privacy condition is satisfied; a cookie-free limited mode and a consent-aware persistent mode are both supported architecturally rather than assuming one setup is lawful everywhere |
| **AI access to underlying data** (M08) | An LLM given broad database access could surface or infer sensitive information beyond what any dashboard would show | AI Insights never gets raw database access — only typed, versioned metric-query tools scoped to approved shapes and limits (see `docs/architecture/bounded-contexts-and-data-ownership.md`); every AI request is audited |
| **Cross-module data leakage** | A module reading another module's raw tables could recombine data in ways neither module's privacy design accounted for | Enforced structurally by the no-shared-database rule (`docs/adr/0001-service-oriented-modular-platform.md`) — cross-module access only via published APIs/events, which are the place privacy review should happen |

## Retention and deletion

- Retention is short by default for high-risk interaction data (heatmaps/session data), and explicitly
  configurable per site for raw-event, aggregate, and audit data more broadly (see M01.1's retention and
  deletion rules).
- A **polished, multi-tenant** data export/deletion workflow (self-serve UI, cross-site batch operations,
  audit trail of who exported/deleted what) is a required platform capability of M10 ("Data can be
  exported and deleted by site") — that full workflow is legitimately scoped to M10, once multi-site/team
  support exists.
- That does **not** mean zero deletion capability before M10. `anthony-air.nl` starts sending real visitor
  traffic at M01.8, and once real personal data (even pseudonymous) is being retained about real visitors,
  basic lifecycle control has to exist from that point — waiting until M10 would mean months of collecting
  data the platform can't yet delete. **Minimum M01 requirements, before real traffic is retained:**
  - configure a retention period (even if only one global/per-site value, not yet per-metric);
  - delete all analytics data for a site, and delete a site itself;
  - rotate or revoke a site's browser ingestion token (see
    `docs/adr/0006-public-browser-ingestion-tokens.md`);
  - stop collection immediately when a site or module is disabled — no further events accepted or
    processed for it.
  These can be operational/admin-only (CLI, direct API call, no dashboard UI) — the M10 requirement is
  for the polished self-serve version, not the first version.

## Transparency

- Debug mode: the SDK must be able to show exactly what it sends, so a site owner (or Telumera's own
  developer) can verify collection behavior directly rather than trusting documentation alone.
- Every metric surfaced in a dashboard must be traceable to a documented definition (Accuracy Principle,
  `docs/architecture/vision-and-scope.md`) — this is itself a privacy control, since it prevents metrics
  from silently including data a user wouldn't expect (e.g. counting opted-out visitors).

## Open items to resolve before the `anthony-air.nl` launch (M01.8)

- Verify the final SDK/cookie/identifier implementation against current Dutch/EU guidance before go-live
  — the architecture must support a cookie-free limited mode and a consent-aware persistent mode, but
  which mode is lawful for a given deployment needs a concrete legal check at launch time, not just at
  design time.
- ~~Confirm the exact geography-enrichment granularity (country vs. region) against the same guidance.~~
  **Resolved (M01.8):** country-only, via an in-process lookup against a local MaxMind-format database
  keyed on the already-truncated IP, storing only the ISO country code (never the IP). Region-level is
  an unbuilt per-site opt-in. See `docs/analytics/definitions-and-privacy-model.md` §7.
- Confirm the query-string allowlist for campaign parameters doesn't inadvertently include anything
  identifying before the SDK ships to a real site.
