// node docs/brand/tools/outline-text.cjs <font.ttf> <text> <out.svg> [size=200] [tracking=0 (em/1000)]
// Needs opentype.js (npm install --no-save opentype.js). Used to make tools/wordmark-outline.svg.
// Writes the text as outlined paths (no <text>), tightly cropped, height = cap/x-box + padding.
const opentype = require('opentype.js')
const fs = require('fs')
const [, , fontPath, text, out, sizeArg, trackArg] = process.argv
const size = Number(sizeArg || 200)
const tracking = Number(trackArg || 0)
const font = opentype.loadSync(fontPath)
let x = 0
const parts = []
const glyphs = font.stringToGlyphs(text)
glyphs.forEach((g, i) => {
  const p = g.getPath(x, 0, size)
  parts.push(p.toPathData(2))
  let adv = g.advanceWidth * (size / font.unitsPerEm)
  if (i < glyphs.length - 1) adv += font.getKerningValue(g, glyphs[i + 1]) * (size / font.unitsPerEm)
  x += adv + (tracking / 1000) * size
})
const full = font.getPath(text, 0, 0, size)
const d = parts.join(' ')
// bbox from all glyph paths
const tmp = new opentype.Path()
glyphs.reduce((xx, g, i) => {
  const p = g.getPath(xx, 0, size)
  tmp.extend(p)
  let adv = g.advanceWidth * (size / font.unitsPerEm)
  if (i < glyphs.length - 1) adv += font.getKerningValue(g, glyphs[i + 1]) * (size / font.unitsPerEm)
  return xx + adv + (tracking / 1000) * size
}, 0)
const bb = tmp.getBoundingBox()
const pad = 0
const w = bb.x2 - bb.x1, h = bb.y2 - bb.y1
fs.writeFileSync(
  out,
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${(bb.x1 - pad).toFixed(2)} ${(bb.y1 - pad).toFixed(2)} ${(w + 2 * pad).toFixed(2)} ${(h + 2 * pad).toFixed(2)}"><path fill="#000" d="${d}"/></svg>\n`,
)
console.log(out, 'bbox', bb.x1.toFixed(1), bb.y1.toFixed(1), bb.x2.toFixed(1), bb.y2.toFixed(1), 'xHeight', ((font.tables.os2.sxHeight * size) / font.unitsPerEm).toFixed(1))
void full
