# Product Analytics — Definitions and Privacy Model

> Status: Draft (M01.1). Defines exactly what each Product Analytics metric means before any collection
> code exists, per `planning/Telumera_Modular_Project_Plan.md`'s M01.1 backlog epic. Every definition
> below must be implemented exactly as written — a metric whose implementation silently drifts from its
> written definition breaks the Accuracy Principle
> (`docs/architecture/vision-and-scope.md` §5): "every number must be traceable back to what was actually
> collected and what was filtered out and why."
>
> Platform-wide privacy rules already decided in `docs/privacy/privacy-threat-model.md` are referenced,
> not repeated, below — this document specifies the Analytics-module-specific mechanics those rules leave
> open (e.g. the threat model says visitor identifiers are "short-lived pseudonymous... daily or
> configurable"; this document specifies exactly how the daily identifier is computed).

## 1. Page-view rules

A **page view** is tracked when:
- the SDK initializes on a freshly-loaded document (a real browser navigation/reload), or
- the client-side router (Vue Router) completes a navigation to a **different matched route
  component** than the one currently tracked.

**Not** tracked as a new page view:
- a route change where only the query string or hash changes but the matched component is the same
  (e.g. filtering a list via `?sort=price` on the same page) — this is a page-view *update*, not a new
  view, unless the site explicitly configures that route to opt in to per-query-value tracking.
- the router's own initial-navigation event that fires on mount, immediately after the SDK's own
  initial-load page view already fired for the same URL — the SDK must suppress this specific
  double-fire (a well-known SPA integration bug) by comparing the router's first post-mount URL against
  the URL already tracked at init.

**Reloads**: a full browser reload re-runs SDK init from scratch and is indistinguishable from — and
correctly counted as — a new page view.

**Redirects**: only the final URL the browser actually renders is trackable; the SDK never runs during an
HTTP redirect hop, so intermediate redirect URLs are structurally invisible to client-side tracking. This
is a documented limitation, not a bug to fix.

**Canonical URL handling** (applied before a page-view URL is ever sent):
- Strip the fragment/hash (`#...`) by default — hash changes alone don't represent a new page. A site
  using hash-based routing sets `hashRouting: true` in SDK config, which flips this: hash changes are then
  treated as route changes per the rule above, and the fragment is retained as the tracked path.
- Strip a trailing `/` except for the root path (`/about/` and `/about` canonicalize to the same tracked
  page). Path casing is preserved as-is — paths are case-sensitive by specification; only the URL's
  scheme/host are ever case-normalized.
- Query-string handling is governed entirely by §6 (URL and query-string policy), not repeated here.
- Excluded routes: a site can configure a route-exclusion list (glob patterns, e.g. `/admin/**`,
  `/preview/**`) in SDK config. A matched route is never tracked — the SDK drops the event before it's
  ever sent, not after.

## 2. Session rules

A **session** is a run of activity from one visitor (§3), identified by a session ID.

- **Inactivity timeout: 30 minutes.** No tracked event (page view or custom event) for 30 minutes ends
  the session; the next tracked event starts a new one. This matches the near-universal industry default
  (GA4, Plausible, Fathom) — deviating from it without a concrete reason isn't worth the analytics
  discontinuity it would cause if changed later.
- **No forced calendar-day cutoff.** GA (classic)'s midnight session cutoff is exactly the kind of
  "ambiguous term" this epic exists to avoid copying — it's arbitrary and GA4 itself dropped it. A session
  spanning midnight while the visitor stays active is one session, not two. (A session's *visitor*
  attribution still follows the visitor-identifier reset rule in §3, which is a separate concern.)
- **Cross-tab behavior**: the session ID and its last-activity timestamp are stored in `localStorage`
  (shared across tabs/windows of the same browser+site), not `sessionStorage` (which is per-tab and would
  incorrectly start a new session per opened tab). Every tracked event updates the shared last-activity
  timestamp; a tab opened within the timeout window joins the existing session.
- **New campaign context restarts the session.** If a tracked page view arrives with new UTM/referrer
  parameters (§6) different from the session's original entry context — e.g. an ad click landing on a
  browser that already has an active session open in another tab — that starts a new session. Session-level
  acquisition attribution must reflect the context that actually drove that visit, not get silently
  absorbed into an unrelated already-open session.

## 3. Visitor rules

Default mode is **cookie-free and stateless**, matching `docs/privacy/privacy-threat-model.md`'s
"short-lived pseudonymous... daily" identifier requirement exactly:

- The visitor identifier is computed **server-side, in the Collector**, per event — never generated or
  stored client-side in the default mode. Inputs: `hash(siteId + dailySalt + truncatedIp + userAgent)`.
- `dailySalt` rotates once every 24 hours (UTC). Two events from the same real device on the same UTC day
  produce the identical hash (correctly deduplicating "one visitor" within that day); the same device the
  next day produces a **different, uncorrelatable** hash.
- **Explicit limitation, not hidden**: this means "returning visitor" and any cross-day visitor-retention
  metric are structurally unavailable in default mode — the raw identifiers cannot be linked across days
  by design. Any dashboard surfacing a cross-day visitor metric must show this mode's limitation inline
  (Accuracy Principle), not silently compute a number that looks more precise than the underlying data
  supports.
- **Configurable persistent mode** (opt-in, consent-gated per
  `docs/privacy/privacy-threat-model.md`'s consent-and-opt-out row): a real random ID stored client-side
  (`localStorage` or a cookie) with a site-configurable TTL, enabling genuine cross-visit/cross-day
  identification. Never the default — turning it on is an explicit, documented site-owner configuration
  choice, per the threat model's existing rule that persistence beyond the default lifetime is never a
  silent default.

## 4. Bounce and engagement rules

"Bounce rate" is retained only as a *derived*, clearly-labeled secondary metric — its primary definition
is the **engaged session**, an explicit formula rather than GA's famously ambiguous single-interaction
rule (a 10-minute single-page read and an instant back-button both count as "a bounce" under that rule,
which is exactly the kind of ambiguity this task exists to avoid):

**A session is engaged if it meets *any* of:**
- active time (§5) ≥ 10 seconds, or
- ≥ 2 page views, or
- ≥ 1 conversion-worthy event (not yet defined — Goals/Funnels is module M05; until then, only the first
  two criteria apply).

**Bounce rate** = 1 − (engaged sessions ÷ total sessions), published only alongside the engaged-session
rate it's derived from, never as a bare standalone percentage — a bounce rate with no visible engagement
threshold invites exactly the "quality" misreading GA's version causes.

## 5. Active-time measurement

**Active time** = wall-clock time on a page, minus:
- time the tab is not the visible one (`document.visibilityState !== 'visible'`, via the Page Visibility
  API), and
- time with no interaction signal (throttled `mousemove`, `keydown`, `scroll`, `touchstart`) for more than
  **30 continuous seconds** while the tab is visible — the visitor left the tab open and walked away, so
  the timer pauses until the next interaction resumes it.

**Delivery mechanism**: the SDK accumulates active time locally and flushes a final summary on
`visibilitychange` (transitioning to hidden) or `pagehide`, via `navigator.sendBeacon` — not the
unreliable `unload` event, and not a page-view-blocking synchronous request. A single-page-app session
that never triggers a page unload (stays on one route for a very long time) additionally sends a bounded
periodic summary rather than losing all active-time data for that visit; the exact interval is an SDK
implementation detail (M01.2), not fixed here — the requirement is only that active time isn't lost for
long-lived SPA sessions.

## 6. URL and query-string policy

**Allowlist-only, enforced client-side** — a non-allowlisted query parameter is never sent from the
browser at all, the strongest privacy posture available (per
`docs/privacy/privacy-threat-model.md`'s "Referrers and URLs" row).

- **Default allowlist**: `utm_source`, `utm_medium`, `utm_campaign`, `utm_term`, `utm_content`.
- **Deliberately excluded by default**: ad-platform click IDs (`gclid`, `fbclid`, `msclkid`, and
  equivalents) — these are high-entropy, effectively single-use tracking tokens that function as de-facto
  persistent identifiers, which is exactly what the visitor-identifier design in §3 exists to avoid. A
  site owner who needs ad-platform attribution precision enough to accept that trade-off can explicitly
  add one to their site's allowlist — an explicit, documented, per-site configuration choice, never a
  platform default.
- **Denylist safety net, independent of the allowlist**: any parameter whose name case-insensitively
  contains `token`, `password`, `secret`, `key`, `auth`, `session`, or `sid` is stripped even if it would
  otherwise match an allowlist entry — denylist wins on conflict. Defense in depth against a site owner
  accidentally allowlisting something that turns out to carry a credential.
- Path and fragment handling is governed by §1, not repeated here.

## 7. IP and geography policy

- The collecting request's IP address (server-observed, never client-submitted — see the Collection API's
  trusted-metadata rule) is used only for: (a) the transient input to §3's daily visitor-hash, and (b) a
  GeoIP lookup.
- **Geography granularity: country only, by default.** Region/state-level granularity is available as an
  explicit **per-site opt-in** for site owners who need finer geography — country-only is the safer,
  less-identifying default and sufficient for most portfolio-analytics use. This resolves the "confirm
  exact geography-enrichment granularity" open item `docs/privacy/privacy-threat-model.md` flagged before
  the `anthony-air.nl` launch (M01.8) — still worth a final check against current guidance at that point,
  per that document's own note.
- **No raw IP storage — schema-level, not just policy.** No Analytics ClickHouse table has an IP column at
  all. This is a stronger guarantee than "we don't store it by policy": there's no column for a future
  change to accidentally start populating.
- IP truncation before GeoIP lookup (mask the last IPv4 octet / last 80 bits of IPv6) is a SHOULD, not a
  MUST, where the GeoIP provider supports it — defense in depth for the transient in-memory window, but
  not load-bearing since the raw address is never persisted regardless.

## 8. Retention and deletion rules

Specifies the actual default durations `docs/privacy/privacy-threat-model.md` left as "even if only one
global/per-site value" — that document's minimum-capability list (configure a retention period, delete a
site's analytics data, delete a site, rotate/revoke a token, stop collection immediately when disabled)
is the operational mechanism; these are the default values:

- **Raw event retention: 90 days**, configurable per site (shorter or longer — no platform-enforced cap
  for M01; multi-tenant hardening with per-tenant caps is M10's concern, not this one's). Long enough for
  meaningful trend analysis, short enough to bound both privacy exposure and storage cost.
- **Aggregate/rollup retention: indefinite by default.** A computed daily-per-page count carries far less
  privacy risk than a raw event once it's no longer traceable to an individual visit — but an aggregate
  bucket covering too few underlying events on a very low-traffic site can effectively *become* a raw
  event log by another name. M01.5 (Analytics storage and aggregation) must define a minimum
  event-count-per-bucket floor below which a bucket isn't published standalone; the exact floor is an
  aggregation-design decision for that epic, not fixed here.
- **Audit-log retention: 1 year.** Covers admin actions (site creation, token rotation/revocation, module
  toggles) — not visitor data, so lower privacy sensitivity, but not retained forever either.
- Deletion mechanics for M01 are admin/CLI-only (no dashboard UI required yet) — matches
  `docs/privacy/privacy-threat-model.md`'s explicit statement that the polished self-serve version is
  scoped to M10, not M01.
