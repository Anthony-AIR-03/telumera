# Dashboard housestyle

Design conventions for `apps/dashboard-web`, built on **Tailwind CSS v4** (CSS-first config — no
`tailwind.config.js`; tokens live in `src/assets/main.css`'s `@theme` block). This doc is the
reference for keeping new screens (M01+) visually consistent with the existing ones rather than
each one re-inventing spacing/color/component choices.

## Color

The brand scale is custom; `neutral-*` is **also** custom — overridden in `main.css`'s `@theme`
block to a slight green-gray tint (matching brand-500's hue) rather than Tailwind's pure-gray
default, so every `neutral-*` class in the app already resolves to the right color with no template
changes needed. `red-*` (danger) is Tailwind's stock scale, unmodified.

| Token | Hex | Usage |
|---|---|---|
| `brand-50` | `#E6FBF3` | Tinted backgrounds, subtle highlight |
| `brand-300` | `#6BDDB4` | Borders/accents that need to read as "brand" but not a solid fill |
| `brand-500` | `#00BD7E` | The exact pre-existing teal — wordmark, links. Do not change; changing it changes the "Telumera" brand color. |
| `brand-700` | `#008055` | Solid button fills, active nav/link text — passes WCAG AA against white text (`brand-500` does not, ~2.45:1) |
| `brand-800` | `#006642` | Hover/active state for `brand-700` fills |
| `neutral-50` | `#F6F8F7` | Page background |
| `neutral-100` | `#EEF1F0` | Sunken surfaces (token `<code>` background) |
| `neutral-200` | `#E7E9E7` | Card borders and dividers |
| `neutral-400`/`500` | `#838F88` | Muted text, placeholders (both steps are the same hex — the app only needed one muted step) |
| `neutral-600` | `#4B5750` | Secondary text |
| `neutral-900` | `#0F1512` | Primary text |

Danger: `red-50`/`red-700` (soft pill by default — background `red-50`, text `red-700` — solid
`red-700` fill only on hover/active). The only destructive actions in the app are revoking a token
and the danger-variant `AppButton`. Don't introduce a second "warning" or "caution" color without a
real destructive action that needs it.

## Typography

Three self-hosted typefaces (`.woff2` files in `public/fonts/`, `@font-face` rules + `@theme` tokens
in `src/assets/main.css` — self-hosted rather than a Google Fonts CDN link deliberately, since this is
a privacy-first analytics platform's own dashboard and shouldn't leak dashboard usage to a third party
just to render its own UI):

- **Sora** (`--font-display`, weights 600/700) — headings only, via the `font-display` utility class.
  No auto-apply; add it explicitly at each `h1`/`h2`.
- **Public Sans** (`--font-sans`, weights 400/500/600) — everything else. This is Tailwind's
  `--font-sans` theme key, so Preflight applies it app-wide automatically; no `body {}` rule needed.
- **JetBrains Mono** (`--font-mono`, weight 400) — token codes and anywhere `font-mono`/`<code>` is
  already used. Also Tailwind's auto-applying key (`--font-mono`).

Scale in use — headings are **bold (700), not semibold**, and a step smaller than they first look:
`text-xl font-bold font-display` (page title, e.g. "Workspaces"/a workspace or site name — 20px/700,
not 24px/600), `text-sm font-bold font-display` (section heading within a page, e.g. "Sites"/"Members"
— 14px/700), `text-xl font-bold font-display` (card title, e.g. the login card), `text-sm font-semibold`
(list item primary text/links — a workspace or site name in a row), `text-xs font-semibold` (breadcrumb
back-links), `text-sm` (body text, form labels), `text-xs` (badges, token/code display). The heading
weight is a common mistake to get wrong — `font-semibold` looks reasonable in isolation but reads
noticeably flatter than the approved design next to real content; always `font-bold` on headings.

## Layout

- **Shell**: a fixed left sidebar (`App.vue`) — brand mark/wordmark, nav (currently just
  "Workspaces"; more items land as modules ship), a role-aware secondary "Log out" button pinned to
  the bottom. The `login` route renders full-bleed with no sidebar/header via `meta: { layout: 'bare'
  }` (`route-meta.d.ts`) — `App.vue` branches on `route.meta.layout === 'bare'`.
- **Page container**: applied once in `App.vue` around `<RouterView />` (non-bare layout only) —
  `px-8 pt-7 pb-16`. **No `max-width`/`mx-auto`** — the approved design runs content the full width
  of the space next to the sidebar; a centered narrow column was tried once and visibly shrank the
  whole app compared to the design reference. Don't repeat this in individual views.
- **Card**: `rounded-[14px] border border-neutral-200 bg-white shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]`
  — used for both standalone panels (the login card) and list containers. The radius and shadow are
  arbitrary values, not Tailwind's `rounded-lg`/`shadow-sm` defaults — copy this exact string,
  Tailwind's built-in steps land visibly tighter/flatter than the approved design. Lists inside a card
  use `divide-y divide-neutral-200` on the `<ul>` instead of a border on each `<li>`.
- **List row**: `flex items-center justify-between px-5 py-3.5` on each `<li>`.
- **Form input/select**: copy this exact class string rather than inventing a variant —
  `rounded-[9px] border border-neutral-300 px-3 py-2 text-sm placeholder-neutral-400 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500`.
  There are only a handful of inputs in the app; this is a documented convention, not a wrapper
  component (see "When to componentize" below).

## Components (`src/components/`)

**`AppButton.vue`** — `variant: 'primary' | 'secondary' | 'danger'` (default `primary`), `loading?:
boolean`, `loadingLabel?: string`. `rounded-[9px]`, `font-bold` (not `font-semibold` — same rule as
headings). `danger` is a soft pill by default (`bg-red-50 text-red-700`), solid `red-700` fill only
on hover — not solid red at rest. Variant is chosen by role, not by guessing what "looks right":
- `primary` — the main create/submit action in a section (Sign in, Create workspace, Add site, Add
  member, Issue new token).
- `secondary` — lower-emphasis, non-destructive actions (Log out — ending a session isn't
  destructive to data, so it shouldn't read as a warning).
- `danger` — irreversible/destructive actions only (Revoke token).

Loading is **visual-only by default** (spinner, disabled, reduced opacity) — it does not synthesize
loading copy. Pass `loadingLabel` only when a translated swap-text string genuinely exists in
`en.json` (today, only `LoginView` does, via `login.signingIn`). Don't add new "…ing" copy to
`en.json` just to wire up `loadingLabel` elsewhere — a spinner is enough.

**`AppBadge.vue`** — `tone: 'neutral' | 'danger'` (default `neutral`). Small pill (`font-bold`, not
`font-semibold`) for role/status text (workspace role, member role, site domain) and one status flag
(`danger` tone for "Revoked").

**`StateMessage.vue`** — `state: 'loading' | 'error' | 'empty'`, `message: string`. Renders the
loading/error/empty condition every data-fetching view needs. `message` must already be a translated
`t('...')` string — this component holds no copy of its own, and always sets `role="alert"` for the
error state.

**`AppToggle.vue`** — `modelValue: boolean`, `disabled?: boolean`, `v-model` + default slot for the
label. Replaces a plain `<input type="checkbox">` for on/off settings (module enable/disable).

## Role-gating

`WorkspaceDetailView.vue`/`SiteDetailView.vue` fetch the caller's own `role` on their workspace/site
directly from `GET /workspaces/{id}`/`GET /sites/{id}` (added specifically for this), and gate the UI
via `src/lib/roles.ts`'s `roleAtLeast(current, required)`, which mirrors the real, ordered `Role` enum
(`services/identity-workspace/Role.cs`: `Viewer < Developer < Admin < Owner`). This is **UI-only** —
the backend enforces every real check independently regardless of what the frontend thinks the role
is; a stale/wrong client-side role surfaces as a normal caught `ApiError`, not a security gap.

Two patterns, chosen by whether there's anything to look at read-only:
- **`v-if` the whole section** for create/add forms (nothing meaningful to show a Viewer) — the
  create-site form requires `Developer`, the add-member form requires `Admin`.
- **`:disabled` on the action** for mutating something already visible (rotate/revoke token, toggle a
  module) — Viewers should still see the tokens/modules list, just not act on it.

## When to componentize

Componentize a pattern once it's **genuinely repeated 3+ times** with the same shape (this is why
`AppBadge`/`StateMessage` exist but form inputs don't — inputs appear in only ~6 places across the
whole app and vary enough that a wrapper would be pure ceremony). Until then, keep it as a documented
Tailwind utility-class convention in this doc and copy it verbatim. When you do add a component,
document its prop contract here in the same style as the three above.

## Dark mode

**Deliberately deferred, not dead code.** The app briefly had leftover, unstyled dark-mode CSS
custom properties from the `create-vue` scaffold; this pass removed them rather than attempt to
design and verify a second color mode alongside the first real light-mode pass. When dark mode is
actually built, it should get the same deliberate palette/contrast treatment this doc gives light
mode — not a mechanical `dark:` class sprinkle.
