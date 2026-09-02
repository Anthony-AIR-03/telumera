/** Shared by AnalyticsDateRangeControl.vue and SiteAnalyticsView.vue so both derive `from`/`to` from the route query the same way — defaults mirror DateRangeParsing.cs's own default (last 7 days). */

export function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

export function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

export function resolveDateRange(query: { from?: unknown; to?: unknown }): {
  from: string
  to: string
} {
  const to = typeof query.to === 'string' ? query.to : todayIso()
  const from = typeof query.from === 'string' ? query.from : addDays(to, -6)
  return { from, to }
}
