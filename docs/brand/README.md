# Telumera brand — v1 (approved 2026-10-13)

This folder is the source of truth for Telumera's visual identity: the logo, colour, typography,
module icons, voice, and the direction for the dashboard. Everything needed to recreate or extend the
brand is written down here, so a later session can rebuild it without the conversation that produced it.

| File | What it is |
|---|---|
| `brand-guide.html` | The visual brand guide. Open it directly in a browser (fonts load from Google Fonts). |
| `dashboard-proposal.html` | A static mockup of the analytics dashboard in the new brand. **Not the live app.** All numbers are made up. |
| `kit/` | Final artwork: marks, small cut, favicon, app icon, wordmark, lockups (SVG), and `icons.js` (module icons). |
| `tools/build-kit.cjs` | Regenerates every SVG in `kit/` from the exact geometry below. `node docs/brand/tools/build-kit.cjs` |
| `tools/outline-text.cjs` | Converts text to outlined SVG paths (used once to make the wordmark). Needs `opentype.js`. |
| `tools/wordmark-outline.svg` | The outlined "TELUMERA" source that `build-kit.cjs` reads, so rebuilding doesn't need the font. |

**Status:** the brand is approved. It is **not yet applied to `apps/dashboard-web`**: the shipped
dashboard still uses the earlier teal palette described in `docs/design/housestyle.md`. When the
dashboard is moved to this brand, update `housestyle.md` to match this document (see
[Rolling it out](#rolling-it-out)).

---

## 1. Decisions in one place

| Decision | Choice |
|---|---|
| Mark | **"Probe"**: a T drawn in five parallel lines. The centre line (the probe) runs furthest at the foot and is the only line that takes colour. |
| Wordmark | `TELUMERA`, **Chakra Petch Regular (400)**, all caps, tracking **+260** (em/1000), converted to outlines. |
| Core colours | **Graphite** `#0E1316`, **Bone** `#EEECE5`, **Signal** `#FF5A1F` (accent only). |
| Typefaces | **Chakra Petch** (display, headings, big numbers) · **Red Hat Text** (interface, body) · **Red Hat Mono** (raw data, labels). |
| Icons | 24-unit grid, 1.5 stroke, miter joins, flat (butt) ends, one accent path per icon. |
| Voice | Precise, plain, honest about uncertainty. Calm, not cute. |

## 2. Idea

Telumera's promise (see `docs/architecture/vision-and-scope.md` §5) is **auditable analytics**: every
number can be traced back to what was actually collected. The mark says this visually. It is a T built
from parallel lines like signal traces on a circuit board, and the single centre line (the "probe")
carries through the crossbar and runs furthest at the foot. "Everything we show you can be followed down
to where it was measured." The line-built style also gives the module icons and charts a shared language.

**Deliberately not in the mark:** the mark does not encode where the name "Telumera" comes from. The
owner doesn't want that advertised, and most viewers wouldn't know it anyway. Don't reintroduce
monograms or letter combinations based on the name's origin.

### How we got here (so a later session doesn't repeat it)

- Two rounds of general concepts were rejected as too plain and the wrong vibe. The wanted feel is
  **technical and sharp**, an **abstract symbol plus name**.
- Monograms of several letters (in triangles, squares, rings) were explored, then dropped for the reason
  above.
- The owner pointed to a reference style: a single T drawn in nested parallel lines inside a round
  seal. Four original T forms were drawn in that style: Open, Bars, **Probe**, and Seal. **Probe** was
  chosen, with Chakra Petch for the wordmark over Michroma and Red Hat Display.
- Geist was considered for the wordmark and rejected as overused.

## 3. The mark

### Geometry (256 × 256 grid, master)

All five lines are **10 units** thick and sit on a **17.5-unit pitch** (gap 7.5, slightly narrower
than a line, so the mark holds together as one shape). Coordinates below are line **centrelines**. The
whole drawing is lifted **4 units** above geometric centre for optical balance (already included in the
numbers).

| Element | Value |
|---|---|
| Overall T box | crossbar x 20–236, y 28–108 (216 × 80); stem x 88–168 (80 wide). The crossbar is exactly as tall as the stem is wide. |
| Outer outline (offset 5) | `M93 188 V103 H25 V33 H231 V103 H163 V188`, open at the foot |
| Inner outline (offset 22.5) | `M110.5 206 V85.5 H42.5 V50.5 H213.5 V85.5 H145.5 V206`, open at the foot |
| Probe: crossbar | `M60 68 H196` |
| Probe: stem | `M128 63 V224` (starts at the top edge of the probe crossbar, so it joins it with a clean T) |
| Feet | outer lines end at **188**, inner at **206**, probe at **224**: three equal 18-unit steps |
| Ends / joins | flat (butt) ends at the feet, square (miter) corners |

Final artwork is **filled geometry, not strokes**: `build-kit.cjs` turns each straight segment into a
rectangle, extended by half the line width into each corner so corners are square, and not extended at
the open feet.

Colourways: `mark.svg` (all Graphite) · `mark-signal.svg` (Graphite outlines, Signal probe, the
default) · `mark-reversed.svg` (Bone outlines, Signal probe, for Graphite backgrounds) ·
`mark-white.svg` (all white). **Only the probe ever takes colour**, and only Signal.

### Small cut (16–48 px)

Five lines don't survive at favicon size, so there is a separate drawing on a **16 × 16 pixel grid**:
the outer outline plus the probe, 1 px lines, 1 px gaps, `shape-rendering="crispEdges"`. It is
pixel-exact at 16, 32 and 48 px.

| Part | Pixel cells (x, y, w, h) |
|---|---|
| Outline | `1,1,13,1` · `1,2,1,4` · `13,2,1,4` · `2,5,4,1` · `9,5,4,1` · `5,6,1,6` · `9,6,1,6` |
| Probe | `3,3,9,1` · `7,4,1,11` |

Files: `mark-small.svg` (Graphite + Signal), `mark-small-mono.svg`, `favicon.svg` (outline switches to
Bone under `prefers-color-scheme: dark`; probe stays Signal).

Use the full mark at **48 px and up**, and the small cut **below 48 px**. Never scale the small cut
above 48 px.

### App icon

`app-icon.svg`: a Graphite tile, 256 × 256 with corner radius 56, holding the reversed mark (Bone +
Signal) scaled to 62% at `translate(48.6 50)`.

### Wordmark & lockups

- **Wordmark:** "TELUMERA" in Chakra Petch Regular (400), uppercase, tracking +260 em/1000, outlined
  (`wordmark.svg`, source `tools/wordmark-outline.svg`). Regular rather than Medium so its stem weight
  (~15 units at 140 cap height) sits close to the mark's lines.
- **Horizontal lockup** (default): mark height : cap height = **2 : 1**; gap between mark and name =
  **crossbar height** (80 units of the mark); caps vertically centred on the mark.
  Files: `lockup-horizontal.svg`, `-reversed`, `-mono`.
- **Stacked lockup:** cap height = 0.28 × mark height; gap 64; both centred.
  Files: `lockup-stacked.svg`, `-reversed`.
- **Clear space:** the crossbar's height on every side.
- **Minimum size:** horizontal lockup 120 px / 30 mm wide; full mark 40 px. Smaller than that, use the
  small cut.
- **Backgrounds:** Bone, white or Graphite. On photos, put the mark on a solid Graphite or Bone plate.
- **Don't:** stretch or squash, recolour the probe (or colour any other line), rotate or flip, change
  the order or spacing of the steps at the foot, add effects, or use the small cut above 48 px.

## 4. Colour

The brand is nearly monochrome. **Signal is a marker, not a fill**: the probe line, the primary chart
series, focus rings, and the active item. If a screen has more than a few touches of Signal, it has too
many. **Signal is never used for status**; orange next to a warning reads as a warning.

| Token | Hex | Notes |
|---|---|---|
| Graphite | `#0E1316` | Ink, dark surfaces. ≈ CMYK 70 55 50 85 |
| Bone | `#EEECE5` | Paper, light surfaces. ≈ CMYK 5 4 8 0 |
| Signal | `#FF5A1F` | Accent. ≈ Pantone 1655 C (approximate, check against a swatch book before print). 5.99:1 on Graphite; **2.64:1 on Bone, so never use it for text on light** |
| Signal deep | `#B53A0A` | Signal for **text** on light backgrounds (≥ 4.5:1 on Bone/50) |
| Signal (dark UI text) | `#FF7A45` | Signal links/text on dark surfaces |

**Neutral ramp:** 900 `#0E1316` · 800 `#171E22` · 700 `#242D32` · 600 `#3A454B` · 500 `#5C676D` (muted
text, 4.91:1 on Bone) · 400 `#8A9398` (muted text on Graphite, 5.97:1) · 300 `#B9BFC1` · 200 `#D9DAD6`
· 100 `#EEECE5` · 50 `#F7F6F2`.

**Data series, in this order:** Signal `#FF5A1F` · Graphite 600 `#3A454B` · Cyan `#2B9EB3` · Iris
`#8F7CF2` · comparison/previous period `#B9BFC1`, drawn dashed.

**Status** (always with an icon and a word, never colour alone): OK `#2E9E6A` · Degraded `#E0A526`
(text on light: `#B07A0E`) · Failing `#E5484D` (dashboard light: `#D43B40`, dark: `#F06368`).

## 5. Typography

All three families are open source (SIL OFL), which allows logo use and self-hosting. For the dashboard,
self-host `.woff2` files the same way the current fonts are (see `housestyle.md`), not a Google Fonts
link.

| Role | Face | Use |
|---|---|---|
| Display | Chakra Petch 500 (400 for the wordmark) | Page titles, section headings, KPI numbers (`tabular-nums`) |
| Interface | Red Hat Text 400/500/600 | Labels, body, buttons, tables |
| Data | Red Hat Mono 400/500 | Event IDs, URLs and paths in tables, timestamps, small uppercase labels (letter-spacing .1–.14em) |

Scale: Display 56 · H1 32–34 · H2 17–20 · Body 14–15 (dashboard) / 16 (docs) · Label 11–12 mono uppercase.

## 6. Module icons

`kit/icons.js` exports `TELUMERA_ICONS` (one per roadmap module) and `telumeraIcon(icon, size, accent)`.
Each icon is a base path plus **one accent path `a`**, the icon's "probe", which turns Signal when the
module is active and stays the base colour when idle or planned. Rules: 24-unit grid, 1.5 stroke,
miter joins, butt ends, straight lines (curves only where the object is round, e.g. the gauge).

| ID | Module | Drawing (accent in bold) |
|---|---|---|
| M01 | Product Analytics | baseline and bars, **tallest bar** |
| M02 | Performance | gauge arc, **needle** |
| M03 | Error Tracking | code brackets, **exclamation mark** |
| M04 | Deployments | stacked layers, **up arrow** |
| M05 | Goals & Funnels | narrowing bars, **last bar** |
| M06 | SEO & Content | page with fold, **text line** |
| M07 | Heatmaps | nested squares, **hot centre** |
| M08 | AI Insights | corner marks, **four-point spark** |
| M09 | Alerts & Uptime | pulse line, **dashed threshold** |
| M10 | Teams & Sites | two nodes, **connector** |
| M11 | Edge Agent | chip with pins, **core** |

Planned modules are shown, not hidden: in navigation they appear dimmed with a "Planned" tag, so the
platform's direction is visible. Gotcha in `telumeraIcon`: pass the accent flag as a real boolean
(`!!ic.live`); passing `undefined` falls back to the default `true` and lights every icon.

## 7. Voice

| Principle | Do | Don't |
|---|---|---|
| Say what was measured | "12,480 visitors, counted once per day" | "12.5K happy users!" |
| Show the doubt | "Partial: 6% of events still processing" | "Data may be inaccurate" |
| Calm, not cute | "No visits yet. Add the snippet and we'll start counting." | "Oops! Nothing here 🙈" |

## 8. Dashboard direction (from `dashboard-proposal.html`)

The approved look for `apps/dashboard-web`. The mockup is static HTML; recreate it with the app's
real components and data.

- **Left rail, always Graphite** (`#0E1316`, deeper `#070A0B` in dark mode), 248 px: reversed horizontal
  lockup at 22 px high → site switcher → **Modules** group (all 11 module icons; the live module has a
  2 px Signal bar at the rail's left edge and a lit accent; planned ones are dimmed with a mono
  "PLANNED" tag) → **Workspace** group (Sites, Settings) → user at the bottom. Under 900 px the rail
  becomes a Graphite top bar with the lockup and the site switcher.
- **Page header:** mono breadcrumb (`site / Module`), Chakra Petch title, then on the right a
  "N online now" pill with a square Signal dot blinking in steps (disabled under reduced motion), a
  segmented date range (selected = inverted fg/bg), and a theme toggle.
- **Audit strip** directly under the header, before any number: events accepted · processed · bots
  marked and kept for audit · sampling state · consent/cookie mode · "How these are counted →". This is
  the visible form of the Accuracy Principle and is part of the brand, not decoration.
- **KPI row:** one bordered strip of four tiles (Visitors, Pageviews, Bounce rate, Avg. visit) with mono
  uppercase labels, Chakra Petch numbers, and mono deltas (green for good, red for bad: a falling bounce
  rate is good). The selected KPI drives the chart and gets a 2 px Signal top border.
- **Chart:** Signal line (2 px) over a 12% Signal area; previous period dashed `--comp`; mono axis
  labels; square (not round) point markers; event annotations as a dashed vertical rule with a mono
  label (preview of M04 Deployment Intelligence).
- **Tables:** mono keys with a horizontal share bar behind each row (`--bar`; the top row uses Signal at
  12%); right-aligned tabular numbers with a muted share %.
- **Lower row:** live event feed (mono, time · event · country, new rows flash Signal-soft), devices
  (a stacked bar plus a country table), and "Coming to this site" (the next planned modules).
- **Shape language:** 3–4 px radii, hairline borders, no shadows, square markers.
- **Surface tokens:** light: bg `#EEECE5`, surface `#F7F6F2`, raised `#FFFFFF`, line `#D9DAD6`,
  line-2 `#E6E5E0`, fg `#0E1316`, fg-2 `#3A454B`, muted `#5C676D`, bar `#E3E1D9`. Dark: bg `#0B0F11`,
  surface `#12181B`, raised `#171E22`, line `#242D32`, line-2 `#1D2529`, fg `#EEECE5`, fg-2 `#D9DAD6`,
  muted `#8A9398`, comp `#3A454B`, bar `#1F282D`.

### Rolling it out

Suggested order for applying the brand to `apps/dashboard-web`, one focused commit each:

1. Tokens: replace the `@theme` palette in `src/assets/main.css` with the tokens above (light + dark).
2. Fonts: self-host Chakra Petch, Red Hat Text and Red Hat Mono `.woff2`; drop the old faces.
3. Favicon and app icon from `kit/`; lockup in the shell.
4. App shell: the Graphite rail with module navigation and module icons.
5. Analytics view: audit strip, KPI strip, chart and table styling.
6. Rewrite `docs/design/housestyle.md` to describe the result.

## 9. Rebuilding and extending

- **Change the mark or lockups:** edit the geometry in `tools/build-kit.cjs` and run
  `node docs/brand/tools/build-kit.cjs`. It rewrites every file in `kit/` except `icons.js`. Running it
  unchanged reproduces the approved files byte for byte.
- **Re-outline the wordmark** (only if the type changes): download Chakra Petch 400 as a static TTF
  (Google Fonts CSS API: `https://fonts.googleapis.com/css2?family=Chakra+Petch:wght@400`, requested
  with an old user agent so it returns `.ttf`), then
  `node docs/brand/tools/outline-text.cjs ChakraPetch-400.ttf TELUMERA docs/brand/tools/wordmark-outline.svg 200 260`
  (needs `npm install --no-save opentype.js`).
- **Add a module icon:** add an entry to `kit/icons.js` following §6, with exactly one accent path.

## 10. Open items

- No trademark search has been done. Get a professional search before registering or wider launch.
- PNG exports (favicon `.ico`, PNG icon set, web manifest icons) are not produced yet. Render them from
  `favicon.svg` / `app-icon.svg` when the dashboard adopts the brand.
- The Pantone and CMYK values are approximations; confirm them against a physical swatch before anything
  is printed.
