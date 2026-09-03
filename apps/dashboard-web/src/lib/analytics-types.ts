/** Mirrors services/analytics/AnalyticsQueryDtos.cs exactly — one shared file since several components need overlapping pieces of these. */

export interface OverviewMetrics {
  visitors: number
  sessions: number
  views: number
  engagedSessions: number
  engagedSessionRate: number
  bounceRate: number
  avgSessionDurationSeconds: number
  totalActiveSeconds: number
  isBelowPrivacyFloor: boolean
}

export interface OverviewResponse {
  from: string
  to: string
  current: OverviewMetrics
  previous: OverviewMetrics | null
  definitions: Record<string, string>
  visitorCountCaveat: string
}

export interface TimeSeriesPoint {
  bucket: string
  sessions: number
  visitors: number
  views: number
}

export interface TimeSeriesResponse {
  interval: string
  points: TimeSeriesPoint[]
}

export interface PageMetric {
  path: string
  views: number
  visitors: number
  entries: number
  exits: number
  engagedViews: number
  isBelowPrivacyFloor: boolean
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface AcquisitionMetric {
  channel: string
  /** Referring domain (no scheme/path/query) — the fallback "source" when a visit carried no utm_* params. Null for direct / same-site / unparseable referrers. */
  referrerHost: string | null
  utmSource: string | null
  utmMedium: string | null
  utmCampaign: string | null
  sessions: number
  visitors: number
  engagedSessions: number
  isBelowPrivacyFloor: boolean
}

export interface TechnologyMetric {
  deviceCategory: string
  browserCategory: string
  osCategory: string
  sessions: number
  visitors: number
  views: number
  isBelowPrivacyFloor: boolean
}

export interface GeographyMetric {
  country: string | null
  sessions: number
  visitors: number
  views: number
  isBelowPrivacyFloor: boolean
}

export interface CustomEventMetric {
  eventName: string
  count: number
  uniqueSessions: number
  allowedProperties: string[]
}

/** Pushed over SignalR by services/analytics LiveHub — mirrors LiveSnapshot / LivePage. */
export interface LiveSnapshot {
  activeVisitors: number
  pages: LivePage[]
}

export interface LivePage {
  path: string
  visitors: number
}

/** Mirrors services/analytics QualityResponse. `dimensions` / `totals` keys: accepted,
 *  rejected_validation, rejected_unknown_token, rejected_origin, rejected_module_disabled,
 *  duplicate, dropped_overload, bot, delayed. */
export interface QualityResponse {
  from: string
  to: string
  series: QualityDayPoint[]
  totals: Record<string, number>
  deadLetterQueueDepth: number
  definitions: Record<string, string>
}

export interface QualityDayPoint {
  date: string
  dimensions: Record<string, number>
}
