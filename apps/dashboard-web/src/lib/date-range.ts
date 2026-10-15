import type { TimeSeriesPoint } from '@/lib/analytics-types'

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

/** One point per day of `range`, zero-filling the days the time-series endpoint omits (it only
 *  returns days that had traffic). Without this the chart spaces points evenly, so a quiet day
 *  vanishes from the x-axis and the previous-period line pairs up the wrong days. */
export function fillDailyGaps(
  points: TimeSeriesPoint[],
  range: { from: string; to: string },
): TimeSeriesPoint[] {
  const byDay = new Map(points.map((p) => [p.bucket.slice(0, 10), p]))
  const filled: TimeSeriesPoint[] = []
  for (let day = range.from; day <= range.to; day = addDays(day, 1)) {
    filled.push(
      byDay.get(day) ?? { bucket: `${day}T00:00:00Z`, sessions: 0, visitors: 0, views: 0 },
    )
  }
  return filled
}
