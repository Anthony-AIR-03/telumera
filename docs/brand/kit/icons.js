// Telumera module icons — 24 grid, 1.5 stroke, miter joins, butt caps. Path "a" is the Signal accent.
window.TELUMERA_ICONS = [
  { id: 'M01', name: 'Product Analytics', live: true, p: 'M4 20H20M7 20V14M11 20V9M15 20V12', a: 'M19 20V5' },
  { id: 'M02', name: 'Performance', p: 'M4 17a8 8 0 1 1 16 0', a: 'M12 17L16.5 10.5' },
  { id: 'M03', name: 'Error Tracking', p: 'M8 5H5V19H8M16 5H19V19H16', a: 'M12 7.5V13.5M12 15.5V17' },
  { id: 'M04', name: 'Deployments', p: 'M4 15L12 19L20 15M4 11L12 15L20 11', a: 'M12 3V11M9 6L12 3L15 6' },
  { id: 'M05', name: 'Goals & Funnels', p: 'M4 6H20M7 12H17', a: 'M10 18H14' },
  { id: 'M06', name: 'SEO & Content', p: 'M6 3H14L18 7V21H6ZM9 11H15', a: 'M9 15H15' },
  { id: 'M07', name: 'Heatmaps', p: 'M4 4H20V20H4ZM8 8H16V16H8Z', a: 'M11 11H13V13H11Z' },
  { id: 'M08', name: 'AI Insights', p: 'M4 20H8M4 4H8M16 4H20M16 20H20', a: 'M12 5L13.6 10.4L19 12L13.6 13.6L12 19L10.4 13.6L5 12L10.4 10.4Z' },
  { id: 'M09', name: 'Alerts & Uptime', p: 'M3 14H7L9 9L12 18L14 12L15 14H21', a: 'M3 6H5M8 6H10M13 6H15M18 6H21' },
  { id: 'M10', name: 'Teams & Sites', p: 'M4 4H10V10H4ZM14 14H20V20H14Z', a: 'M7 10V17H14' },
  { id: 'M11', name: 'Edge Agent', p: 'M7 7H17V17H7ZM10 3V7M14 3V7M10 17V21M14 17V21M3 10H7M3 14H7M17 10H21M17 14H21', a: 'M10 10H14V14H10Z' },
]
window.telumeraIcon = (ic, size = 24, accent = true) =>
  `<svg viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="miter" stroke-linecap="butt" aria-hidden="true"><path d="${ic.p}"/><path d="${ic.a}" ${accent ? 'stroke="var(--signal)"' : ''}/></svg>`
