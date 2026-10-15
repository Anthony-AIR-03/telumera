// node docs/brand/tools/build-kit.cjs — regenerates every SVG in docs/brand/kit/ from the geometry below.
// Filled geometry only (no strokes, no live text). See docs/brand/README.md for the spec this encodes.
const fs = require('fs')
const path = require('path')
const KIT = path.join(__dirname, '..', 'kit')
const f = (n) => +n.toFixed(2)
const C = { ink: '#0E1316', paper: '#EEECE5', signal: '#FF5A1F' }
const write = (n, s) => (fs.writeFileSync(path.join(KIT, n), s), console.log('kit/' + n))

// Polyline -> rectangles (axis-aligned segments only). Butt caps at the open ends, square miter joins.
function rects(pts, w) {
  const h = w / 2, out = []
  for (let i = 0; i < pts.length - 1; i++) {
    const [x1, y1] = pts[i], [x2, y2] = pts[i + 1]
    const s = i === 0 ? 0 : h, e = i === pts.length - 2 ? 0 : h // extend into joins only
    if (y1 === y2) { const a = Math.min(x1, x2), b = Math.max(x1, x2), lo = x1 < x2 ? s : e, hi = x1 < x2 ? e : s
      out.push([a - lo, y1 - h, b + hi, y1 + h]) }
    else { const a = Math.min(y1, y2), b = Math.max(y1, y2), lo = y1 < y2 ? s : e, hi = y1 < y2 ? e : s
      out.push([x1 - h, a - lo, x1 + h, b + hi]) }
  }
  return out
}
const d = (rs) => rs.map(([a, b, c, e]) => `M${f(a)} ${f(b)}H${f(c)}V${f(e)}H${f(a)}Z`).join('')

// ---- Master mark: 256 grid. Crossbar x20..236, y28..108; stem x88..168; stroke 10, pitch 17.5.
// Lines: outer contour (offset 5), inner contour (offset 22.5), centre "probe" line (offset 40).
// Feet: outer 192, inner 210, probe 228 — the stem ends in a stepped point.
const Y = -4 // optical lift: box was 32..232, now 28..228
const outer = [[93, 192], [93, 107], [25, 107], [25, 37], [231, 37], [231, 107], [163, 107], [163, 192]].map(([x, y]) => [x, y + Y])
const inner = [[110.5, 210], [110.5, 89.5], [42.5, 89.5], [42.5, 54.5], [213.5, 54.5], [213.5, 89.5], [145.5, 89.5], [145.5, 210]].map(([x, y]) => [x, y + Y])
const probeH = [[60, 72 + Y], [196, 72 + Y]], probeV = [[128, 72 + Y - 5], [128, 228 + Y]]
const W = 10
const frame = d([...rects(outer, W), ...rects(inner, W)])
const probe = d([...rects(probeH, W), ...rects(probeV, W)])
const markSvg = (a, b, note) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">\n  <!-- Telumera mark "Probe" — ${note} -->\n  <path fill="${a}" d="${frame}"/>\n  <path fill="${b}" d="${probe}"/>\n</svg>\n`
write('mark.svg', markSvg(C.ink, C.ink, 'one colour, Graphite'))
write('mark-signal.svg', markSvg(C.ink, C.signal, 'Graphite with Signal probe line'))
write('mark-reversed.svg', markSvg(C.paper, C.signal, 'reversed: Bone with Signal probe, for Graphite backgrounds'))
write('mark-white.svg', markSvg('#FFFFFF', '#FFFFFF', 'one colour, white'))

// ---- Small cut: 16-px pixel grid. Outer contour + probe only, 1-px lines, 1-px gaps.
const px = (cells) => cells.map(([x, y, w, h]) => `M${x} ${y}h${w}v${h}h-${w}Z`).join('')
const smallFrame = px([[1, 1, 13, 1], [1, 2, 1, 4], [13, 2, 1, 4], [2, 5, 4, 1], [9, 5, 4, 1], [5, 6, 1, 6], [9, 6, 1, 6]])
const smallProbe = px([[3, 3, 9, 1], [7, 4, 1, 11]])
const small = (bg, a, b, note) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" shape-rendering="crispEdges">\n  <!-- Telumera small cut (16-px grid, use at 16/32/48 px) — ${note} -->\n${bg ? `  <rect width="16" height="16" rx="3" fill="${bg}"/>\n` : ''}  <path fill="${a}" d="${smallFrame}"/>\n  <path fill="${b}" d="${smallProbe}"/>\n</svg>\n`
write('mark-small.svg', small(null, C.ink, C.signal, 'Graphite + Signal'))
write('mark-small-mono.svg', small(null, C.ink, C.ink, 'one colour'))
write('favicon.svg', `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" shape-rendering="crispEdges">\n  <style>.f{fill:${C.ink}}@media (prefers-color-scheme:dark){.f{fill:${C.paper}}}</style>\n  <path class="f" d="${smallFrame}"/>\n  <path fill="${C.signal}" d="${smallProbe}"/>\n</svg>\n`)

// ---- App icon: Graphite tile, mark at 62%.
write('app-icon.svg', `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">\n  <rect width="256" height="256" rx="56" fill="${C.ink}"/>\n  <g transform="translate(48.6 50) scale(.62)"><path fill="${C.paper}" d="${frame}"/><path fill="${C.signal}" d="${probe}"/></g>\n</svg>\n`)

// ---- Wordmark (Chakra Petch 400, tracking 260, outlined) and lockups.
const wm = fs.readFileSync(path.join(__dirname, 'wordmark-outline.svg'), 'utf8')
const wmD = wm.match(/ d="([^"]+)"/)[1]
const [wx, wy, ww, wh] = wm.match(/viewBox="([^"]+)"/)[1].split(' ').map(Number)
write('wordmark.svg', `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${wx} ${wy} ${ww} ${wh}">\n  <!-- TELUMERA, Chakra Petch Regular, tracking +260, outlined -->\n  <path fill="${C.ink}" d="${wmD}"/>\n</svg>\n`)
// Mark content box: x20..236, y24..224 (200 tall). Horizontal lockup: mark 200 tall, cap 100 (1:2), gap = 80 (crossbar height).
function lockupH(a, b, t, name, note) {
  const s = 100 / 140, mx = 0, gap = 80, tx = 216 + gap - wx * s, ty = 50 + 100 // caps centred on the mark: 50..150
  const W2 = 216 + gap + ww * s
  write(name, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${f(W2)} 200">\n  <!-- Horizontal lockup — ${note}. Mark : cap height = 2 : 1, gap = crossbar height. -->\n  <g transform="translate(-20 -24)"><path fill="${a}" d="${frame}"/><path fill="${b}" d="${probe}"/></g>\n  <g transform="translate(${f(tx)} ${f(ty)}) scale(${f(s)})"><path fill="${t}" d="${wmD}"/></g>\n</svg>\n`)
}
lockupH(C.ink, C.signal, C.ink, 'lockup-horizontal.svg', 'Graphite + Signal')
lockupH(C.paper, C.signal, C.paper, 'lockup-horizontal-reversed.svg', 'reversed')
lockupH(C.ink, C.ink, C.ink, 'lockup-horizontal-mono.svg', 'one colour')
function lockupS(a, b, t, name, note) {
  const s = 56 / 140, Wd = ww * s, Wt = Math.max(Wd, 216), gap = 64
  const mxo = (Wt - 216) / 2, txo = (Wt - Wd) / 2 - wx * s
  write(name, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${f(Wt)} ${200 + gap + 56}">\n  <!-- Stacked lockup — ${note} -->\n  <g transform="translate(${f(mxo - 20)} -24)"><path fill="${a}" d="${frame}"/><path fill="${b}" d="${probe}"/></g>\n  <g transform="translate(${f(txo)} ${200 + gap + 56}) scale(${f(s)})"><path fill="${t}" d="${wmD}"/></g>\n</svg>\n`)
}
lockupS(C.ink, C.signal, C.ink, 'lockup-stacked.svg', 'Graphite + Signal')
lockupS(C.paper, C.signal, C.paper, 'lockup-stacked-reversed.svg', 'reversed')
