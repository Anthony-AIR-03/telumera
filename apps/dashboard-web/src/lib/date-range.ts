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

/** The equal-length period directly before `range` — the same comparison window the overview
 *  endpoint's `compare=true` uses (DateRangeParsing.GetPreviousPeriod), so chart and KPI deltas agree. */
export function previousRange(range: { from: string; to: string }): { from: string; to: string } {
  const spanDays =
    Math.round(
      (new Date(`${range.to}T00:00:00Z`).getTime() -
        new Date(`${range.from}T00:00:00Z`).getTime()) /
        86_400_000,
    ) + 1
  const to = addDays(range.from, -1)
  return { from: addDays(to, -(spanDays - 1)), to }
}
