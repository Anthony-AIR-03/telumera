# packages/browser-sdk

Small, privacy-aware TypeScript SDK loaded on customer sites to send analytics events to the future
Event Collector (M01.3, not yet built). Built in M01.2 (Epic "Browser tracking SDK"), implementing the
client-side behavior specified in `docs/analytics/definitions-and-privacy-model.md` (M01.1) and following
`docs/adr/0006-public-browser-ingestion-tokens.md` (the config field is `siteToken` — a public, write-only
"browser site token", never called an "SDK key") and `docs/privacy/privacy-threat-model.md` (consent gates
sending; debug mode shows exactly what would be sent; no client-side visitor identifier in the default
mode).

## Build outputs

`npm run build` produces three things from a single `src/index.ts` entry point (Vite library mode):

- `dist/browser-sdk.mjs` — an ES module, for bundler-based consumption (`import`).
- `dist/browser-sdk.global.js` — an IIFE that assigns `window.telumera`, for a plain `<script>` tag on a
  site with no build step.
- `dist/index.d.ts` (+ per-module `.d.ts` files) — emitted separately via `tsc -p tsconfig.build.json`,
  since Vite's build only handles the JS.

## Usage

The Event Collector serves this exact bundle at `GET /telumera.js` (M01.8), so both the script and
the ingestion endpoint sit behind one origin. The dashboard's **Install** card
(`apps/dashboard-web` → a site → Install) generates this snippet with the site's active token
already filled in — copy it from there rather than hand-writing it.

```html
<script src="https://collect.telumera.nl/telumera.js"></script>
<script>
  const analytics = window.telumera.init({
    siteToken: 'the-site-registry-issued-token',
    endpoint: 'https://collect.telumera.nl/v1/events',
    consent: 'required', // or 'none' to start tracking immediately
  })

  // Only needed on an SPA — wires up automatic page-view tracking on navigation.
  analytics.trackRouter(router) // any object with an `afterEach` method shaped like Vue Router's

  analytics.track('signup_completed', { plan: 'pro' })
</script>
```

Or as an ES module: `import { init } from '@telumera/browser-sdk'` (same `init` config).
Locally the collector serves `/telumera.js` from this package's own `dist/` output — run
`npm run build:js -w @telumera/browser-sdk` first, or the container build bakes it in.

## Config (`SdkConfig`, see `src/types.ts`)

| Field | Required | Notes |
| --- | --- | --- |
| `siteToken` | yes | Issued by Site Registry (`POST /sites/{id}/tokens/rotate`). Public by design. |
| `endpoint` | yes | Collector URL. No path is assumed — the Collector defines its own route. |
| `consent` | no | `'none'` (default, tracking starts immediately) or `'required'` (nothing sends until `setConsent(true)`). |
| `sampleRate` | no | 0–1, default 1. Decided once per session, at session creation. |
| `debug` | no | Logs every outgoing payload and validation error to the console **and skips the real network send** — debug implies dry-run. |
| `hashRouting` | no | Treats the URL fragment as the tracked path (for hash-based routing). |
| `excludeRoutes` | no | Glob patterns (e.g. `/admin/**`) that are never tracked. |
| `queryAllowlist` | no | Extra query params to allow through, beyond the default UTM set. |
| `persistentVisitorId` | no | Opts into a real, random, client-stored visitor ID with the given `ttlMs`. Never on by default — see definitions doc §3. |

## What each source module owns

- `pageview.ts` — page-view tracking, including the router-double-fire suppression and the
  matched-route-vs-query-only-change distinction from definitions doc §1.
- `session.ts` — 30-minute-inactivity, cross-tab (`localStorage`), campaign-restart session rules (§2).
- `visitor.ts` — the opt-in, consent-gated persistent visitor ID only; default mode has no client-side
  visitor ID at all (§3), since that's computed server-side by the Collector.
- `campaign.ts` / `url.ts` — referrer/UTM parsing and the query-string allowlist/denylist (§6).
- `engagement.ts` — active-time measurement, idle/visibility handling (§5).
- `events.ts` — the public `track()` API's validation limits and blocked property names.
- `queue.ts` — batching, retry with backoff, and the `sendBeacon`-based unload flush.
- `consent.ts` — the consent/opt-out gate every event passes through before being sent.
- `debug.ts` — the debug/dry-run transport.

## Testing

Vitest + jsdom (`npm run test`). No Playwright/real-browser automation — jsdom-backed unit tests are the
strategy for this package, matching the rest of the repo's TS tooling.

## Not in this package

The Event Collector itself (M01.3) — this SDK only needs a configurable `endpoint` URL, no assumption
about the Collector's actual route. Publishing to an npm registry — no registry/publish workflow exists in
this repo yet.
