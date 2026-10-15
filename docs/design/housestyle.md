# Dashboard housestyle

Design conventions for `apps/dashboard-web`, built on **Tailwind CSS v4** (CSS-first config, no
`tailwind.config.js`; tokens live in `src/assets/main.css`). The dashboard follows **brand v1**
(`docs/brand/README.md`, approved 2026-10-13); that README is the source of truth for the identity
itself (mark, colour meaning, type, icons, voice). This doc is the reference for applying it in the
app, so new screens stay consistent instead of re-inventing spacing, colour and component choices.

The approved look is `docs/brand/dashboard-proposal.html` (a static mockup). Check it for an
equivalent pattern before inventing a new one.

## Colour

Tailwind's own colour scale is **wiped** in `main.css` (`--color-*: initial`), so only brand tokens
exist. A stray `text-neutral-500` or `bg-white` generates nothing instead of quietly rendering an
off-brand colour. Every token is a CSS variable that switches with the theme (see Dark mode).

| Utility name | Light | Dark | Use |
|---|---|---|---|
| `page` | `#EEECE5` Bone | `#0B0F11` | Page background (`bg-page`) |
| `surface` | `#F7F6F2` | `#12181B` | Quiet fills: audit strip, segmented controls, hovers, code blocks |
| `raised` | `#FFFFFF` | `#171E22` | Cards, KPI tiles, inputs, drawers |
| `line` / `line-2` | `#D9DAD6` / `#E6E5E0` | `#242D32` / `#1D2529` | Card borders / row dividers and grid lines |
| `fg` / `fg-2` | `#0E1316` / `#3A454B` | `#EEECE5` / `#D9DAD6` | Primary / secondary text |
| `muted` | `#5C676D` | `#8A9398` | Labels, meta text, placeholders |
| `bar` / `comp` | `#E3E1D9` / `#B9BFC1` | `#1F282D` / `#3A454B` | Table share bars / previous-period line |
| `signal` | `#FF5A1F` | same | **Marker only**: the probe, the chart's main series, focus rings, the active item |
| `signal-text` | `#B53A0A` | `#FF7A45` | Signal as **text** (links). Never use `text-signal` for text on light, it's 2.64:1 |
| `signal-soft` | Signal 12% | Signal 16% | Chart area, the top row's share bar, the live-row flash |
| `ok` / `warn` / `bad` | `#2E9E6A` / `#B07A0E` / `#D43B40` | `#3DBA82` / `#E0A526` / `#F06368` | Status and good/bad deltas only. `bad` is also the danger button |
| `rail`, `rail-*` | Graphite rail | deeper `#070A0B` | The left rail and the mobile top bar, Graphite in both themes |
| `series-3` / `series-4` | `#2B9EB3` Cyan / `#8F7CF2` Iris | same | Data series 3 and 4 |

Rules from the brand that the app enforces:

- **Signal is a marker, not a fill.** If a screen has more than a few touches of Signal, it has too
  many. Primary buttons are inverted fg/bg (Graphite on light, Bone on dark), not Signal. The quality
  chart's bulk outcome ("accepted") is the neutral, not Signal, for this reason.
- **Signal is never status.** Good/bad uses `ok`/`bad`, always with a word or an arrow next to it.
  Where a status colour would be small text on a light surface (e.g. "No sampling" in the audit
  strip), only the square marker takes the colour and the word stays `fg-2`, for contrast.
- **Data series order:** Signal, Graphite 600 (`fg-2`), Cyan, Iris, then `comp` for a previous period
  (dashed) or an "Other" bucket. Never a fifth hue: fold the tail into "Other". The `dataviz` skill's
  `validate_palette.js` passes CVD separation, normal-vision floor and contrast for this order in
  light and dark; it flags the Graphite slot on lightness/chroma only because the brand makes series 2
  a deliberate neutral. Legends and text values always name every segment.

## Typography

Three self-hosted faces (`public/fonts/`, `@font-face` in `main.css`; self-hosted rather than a Google
Fonts link because a privacy-first analytics dashboard shouldn't leak its own usage to a third party):

- **Chakra Petch** 500 (`font-display`): page titles, section headings, KPI numbers. 400 only exists
  for completeness; the wordmark itself is outlined SVG, not live text.
- **Red Hat Text** (`--font-sans`, variable): everything else. Applied app-wide by Preflight.
- **Red Hat Mono** (`--font-mono`, variable): raw data (paths, hosts, event names, tokens, dates),
  breadcrumbs, table headers, deltas, and the uppercase micro-labels.

Scale in use: page title 34px (28px on phones), `font-medium`, via `PageHeader`; card/section heading
17px `font-display font-medium` (account screens use `text-lg`); KPI value 32px (26px on phones,
`tabular-nums`); body 14px; label 11px mono uppercase, `.label-mono`. Headings are **500, not bold**:
Chakra Petch is already sharp, and bold reads heavy next to the hairline UI.

## Shape

3–4px radii (`rounded-[4px]` panels, `rounded-[3px]` controls, `rounded-[2px]` inner parts), hairline
borders, **no shadows** anywhere (tooltips are inverted fg/bg instead of floating on a shadow),
square markers (chart points, legend keys, the live dot), 1.5-stroke line icons with miter joins.

## Shared classes (`main.css`, `@layer components`)

Utilities still win over these, so a one-off tweak is a utility next to them.

- **`.card`**: `rounded-[4px] border border-line bg-raised`. Every panel and list container.
  Lists inside a card use `divide-y divide-line-2` on the `<ul>`; rows are
  `flex items-center justify-between gap-3 px-5 py-3.5`. Card headers are
  `flex items-baseline justify-between px-5 pt-4` with an h2 on the left and mono meta on the right.
- **`.label-mono`**: 11px Red Hat Mono, uppercase, `.12em` tracking, `muted`.
- **`.field`**: inputs and selects. Focus shows the global Signal focus ring.
- **`.seg`**: segmented control; put `aria-pressed` on each child `<button>`; the pressed one inverts.
  Used for date presets and the install snippet's flavour switch.
- **`.data-table`**: mono uppercase headers, 38px rows, hairline dividers, `.num` for right-aligned
  tabular numbers, and `td.key` for a share bar behind the key: set `--w` (0–1) on the cell. The first
  body row's bar is `signal-soft`, so sort the rows largest-first (the components do).
- **Global focus ring:** `:focus-visible` is a 2px Signal outline, offset 2px, everywhere.

## Layout

- **Shell (`App.vue`)**: a 248px Graphite rail, always Graphite in both themes: the reversed
  lockup (`BrandLockup.vue`, 22px high) → **Modules** (all 11 roadmap modules from `lib/modules.ts`;
  the shipped one links and, when active, gets a 2px Signal bar at the rail's left edge plus a lit
  icon accent; planned ones are dimmed and tagged "Planned") → **Workspace** (Workspaces) → the user
  (initial, name, Log out) and the theme toggle at the bottom. Under `lg` the rail becomes a Graphite
  top bar (lockup, theme toggle, menu button) with the same nav in a native `<dialog>` drawer. The
  `login` route renders full-bleed (`meta.layout: 'bare'`) on Graphite.
- **Page container**: `px-9 pt-7 pb-16` on desktop, applied once in `App.vue`. No max-width.
- **`PageHeader.vue`**: every screen starts with it: a mono breadcrumb (`crumbs`), the title, a
  `#title-after` slot (badges, the site switcher) and a `#tools` slot on the right.

## Components (`src/components/`)

**`AppButton.vue`**: `variant: 'primary' | 'secondary' | 'danger'`, `loading?`, `loadingLabel?`.
Primary = inverted fg/bg; secondary = `surface` with a `line` border; danger = `bad` outline, solid
`bad` only on hover. Choose by role: primary for the main create/submit action in a section,
secondary for low-emphasis non-destructive actions, danger only for irreversible ones (Revoke token).
Loading is visual-only (square spinner); pass `loadingLabel` only when a translated swap text exists.

**`AppBadge.vue`**: `tone: 'neutral' | 'danger'`. A small mono outline tag for roles, domains, the
"too few to show" privacy-floor flag, event property names, and "Revoked".

**`AppToggle.vue`**: squared on/off switch; on = `fg` fill.

**`StateMessage.vue`**: `state: 'loading' | 'error' | 'empty'`, `message` (already translated).
Error sets `role="alert"`.

**`ModuleIcon.vue`**: renders a module's brand icon from `lib/modules.ts`; `lit` turns its single
accent path Signal. `lib/modules.ts` copies the paths from `docs/brand/kit/icons.js` (the source of
truth); add new modules there first.

**`BrandLockup.vue`**: the horizontal lockup inlined with `currentColor`, so it's Graphite or Bone
from the surrounding text colour while the probe stays Signal. Set its height with a class.

**`ThemeToggle.vue`** + **`lib/theme.ts`**: see Dark mode.

## Role-gating

`WorkspaceDetailView.vue`/`SiteDetailView.vue` fetch the caller's own `role` from
`GET /workspaces/{id}`/`GET /sites/{id}` and gate the UI via `src/lib/roles.ts`'s
`roleAtLeast(current, required)`, mirroring the backend's `Viewer < Developer < Admin < Owner`. This is
**UI-only**; the backend enforces every real check. Create/add forms are hidden (`v-if`) below the
required role; actions on visible things (rotate/revoke token, toggle a module) are `:disabled`.

## Analytics screen (`SiteAnalyticsView.vue`, `src/components/analytics/`)

Top to bottom, matching the approved proposal:

- **Header**: breadcrumb `Workspace / domain / Analytics`, the site name, the **site switcher** inline
  with the title, and in the tools area the **"N online now" pill** (`LivePill.vue`: a square Signal
  dot blinking in steps, held steady under reduced motion; shows the connection state in words when
  it drops) and the **date range** (`AnalyticsDateRangeControl.vue`: 7/30/90-day `.seg` presets,
  custom from/to, shareable link; state in the route query via `lib/date-range.ts`).
- **Audit strip** (`AuditStrip.vue`), before any number: events accepted, duplicates removed, bots
  marked and kept for audit, rejected (from the same `/analytics/quality` endpoint as the Data quality
  screen), "No sampling" (true: nothing in the pipeline samples), "Cookie-free visitor IDs", then
  "Metric definitions" and "How these are counted →" (the Data quality screen). If the counts fail to
  load it says so instead of disappearing. This strip is part of the brand (the Accuracy Principle),
  not decoration; keep it on any future module's main screen.
- **KPI strip**: one `.card` with `grid gap-px bg-line-2` so hairlines sit between tiles at any wrap;
  `MetricCard.vue` tiles with mono labels, Chakra Petch values and mono deltas (`▲ 8.2%` in `ok` or
  `bad` by whether the change is *good*, so a falling bounce rate is green, then muted "vs prior
  period"). Deltas only render when the comparison period actually had sessions. The three count KPIs
  are selectable (`?metric=`); the selected one gets a 2px Signal top bar and drives the chart. Engaged
  rate and bounce rate are always shown as a pair and aren't chartable (no daily series).
- **Chart** (`TrafficChart.vue`): the selected metric as a 2px Signal line over a `signal-soft` area,
  the previous equal-length period (`previousRange()`, same window as the backend's comparison) as a
  dashed `comp` line, straight segments (smoothing implies values between days that were never
  measured), one linear y-axis from 0, mono axis labels, square end marker, crosshair + inverted
  tooltip with both periods' values. Drawn in real pixels from a `ResizeObserver` so labels stay 11px
  on phones. If only the previous period fails to load, the current line still draws.
- **Live now** (`LiveVisitorsPanel.vue`) and **Coming to this site** (`ComingModulesCard.vue`, the next
  planned modules). The page holds one SignalR connection (`lib/use-live-snapshot.ts`) shared by the
  pill and the card; it connects to the analytics origin directly, not through the gateway (a Dapr
  forwarder can't carry a WebSocket). Newly appearing paths flash `signal-soft` once.
- **Breakdown tabs**: underline tabs, active = 2px Signal underline, tab in `?tab=`. Pages (searchable,
  sortable, paginated), Sources (`AcquisitionTable`, "No referrer" for direct/same-site/unparseable),
  Technology (one ring per dimension: square ends, 2px gaps, top share in mono at the centre, legend
  beside it, brand series order, beyond four categories folds into "Other"), Geography (map + table), Events.
- **`GeographyMap.vue`**: equirectangular choropleth from the vendored, pre-projected
  `src/assets/world-countries.json` (`scripts/build-world-map.mjs`), lazy-loaded. Sqrt scale on a
  neutral `bar → fg-2` ramp mixed in CSS (`color-mix`, set via `style`, not the `fill` attribute), so
  it follows the theme; only the single top country is Signal, like the tables' top row.
  `GeographyTable` folds below-privacy-floor countries (<5 visitors) into one "Other" row and shows
  each country's share.
- **`GlossaryDrawer.vue`**: a native `<dialog>`. **`left-auto` is load-bearing** on a right-anchored
  modal `<dialog>`: its UA stylesheet sets `inset: 0`, and `right-0` alone doesn't clear `left: 0`.
- **`SiteSwitcher.vue`**: mono domain button; the menu closes on an outside click (document listener
  scoped to the component's root).

**Data quality** (`SiteQualityView.vue`, `/sites/:id/analytics/quality`): a 6-tile KPI strip
(`QualitySummaryCards`), the stacked daily collector outcomes (`QualityChart`, the mutually exclusive
outcomes only so the stack sums honestly; accepted = neutral, rejected = Iris, duplicate = Cyan,
dropped = Signal), and the API's own `definitions` in a disclosure.

## When to componentize

Componentize a pattern once it's genuinely repeated 3+ times with the same shape; until then, keep it
as a documented class convention here (that's what the `main.css` component classes are). When you
add a component, document its prop contract here.

## Dark mode

Built, from the brand's own dark tokens (README §8), not a mechanical inversion. The theme follows
`prefers-color-scheme` by default; the toggle (rail footer / mobile top bar) pins a choice by setting
`data-theme` on `<html>` and storing it in `localStorage` (`telumera-theme`). A small inline script in
`index.html` applies a pinned choice before first paint. The rail stays Graphite in both themes. Any
new colour must be a token defined in all three blocks in `main.css` (light, OS-dark, pinned dark).

## Icons and favicon

`public/favicon.svg` is the brand small cut (outline turns Bone in dark browser chrome),
`public/favicon.ico` holds 32 and 48 px PNGs of it, and `public/apple-touch-icon.png` is the app icon
at 180 px on a full Graphite square (iOS rounds it). The PNGs were rendered from `docs/brand/kit/` with
headless Chrome; re-render them if the kit changes. No web manifest yet.
