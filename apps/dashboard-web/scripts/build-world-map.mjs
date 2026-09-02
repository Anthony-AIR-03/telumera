// Regenerates src/assets/world-countries.json — a pre-projected { ISO_A2: SVG path d } map used by
// components/analytics/GeographyMap.vue. Run manually when the source needs refreshing:
//
//   curl -sSL -o /tmp/ne110.geojson \
//     https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_admin_0_countries.geojson
//   node apps/dashboard-web/scripts/build-world-map.mjs /tmp/ne110.geojson
//
// Source: Natural Earth 1:110m Admin 0 – Countries (public domain, naturalearthdata.com).
// Projection: plain equirectangular into a 1000x500 viewBox (x = (lon+180)/360*1000,
// y = (90-lat)/180*500) — no projection library, matching the repo's hand-rolled-SVG approach.
// Simplification: Ramer–Douglas–Peucker at 0.5px on the projected points. Antarctica dropped.

import { readFileSync, writeFileSync } from 'node:fs'

const src = process.argv[2]
if (!src) {
  console.error('usage: node build-world-map.mjs <ne_110m_admin_0_countries.geojson>')
  process.exit(1)
}

const W = 1000
const H = 500
const px = (lon) => +(((lon + 180) / 360) * W).toFixed(1)
const py = (lat) => +(((90 - lat) / 180) * H).toFixed(1)

function rdp(pts, eps) {
  if (pts.length < 3) return pts
  const [ax, ay] = pts[0]
  const [bx, by] = pts[pts.length - 1]
  const dx = bx - ax
  const dy = by - ay
  const den = Math.hypot(dx, dy) || 1
  let dmax = 0
  let idx = 0
  for (let i = 1; i < pts.length - 1; i++) {
    const [x, y] = pts[i]
    const d = Math.abs(dy * x - dx * y + bx * ay - by * ax) / den
    if (d > dmax) {
      dmax = d
      idx = i
    }
  }
  if (dmax > eps) {
    return [...rdp(pts.slice(0, idx + 1), eps).slice(0, -1), ...rdp(pts.slice(idx), eps)]
  }
  return [pts[0], pts[pts.length - 1]]
}

function ringToPath(ring) {
  let pts = ring.map(([lon, lat]) => [px(lon), py(lat)])
  if (pts.length > 1) {
    const [a, b] = [pts[0], pts[pts.length - 1]]
    if (a[0] === b[0] && a[1] === b[1]) pts = pts.slice(0, -1) // drop the closing dup; Z re-closes
  }
  if (pts.length < 3) return ''
  pts = rdp(pts, 0.5)
  if (pts.length < 3) return ''
  return 'M' + pts.map(([x, y]) => `${x} ${y}`).join('L') + 'Z'
}

const geo = JSON.parse(readFileSync(src, 'utf8'))
const out = {}
for (const f of geo.features) {
  let iso = f.properties.ISO_A2_EH || f.properties.ISO_A2
  if (!iso || iso === '-99') iso = f.properties.WB_A2
  if (!iso || iso === '-99' || iso === 'AQ') continue
  const geom = f.geometry
  const polys = geom.type === 'Polygon' ? [geom.coordinates] : geom.coordinates
  let d = ''
  for (const poly of polys) {
    for (const ring of poly) d += ringToPath(ring)
  }
  if (d) out[iso] = (out[iso] ?? '') + d
}

const json = JSON.stringify(out)
writeFileSync(new URL('../src/assets/world-countries.json', import.meta.url), json)
console.log(`wrote ${Object.keys(out).length} countries, ${(json.length / 1024).toFixed(1)} KB`)
