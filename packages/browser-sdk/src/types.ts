/** Config accepted by `init()`. Only `siteToken` and `endpoint` are required. */
export interface SdkConfig {
  /** Browser site token issued by Site Registry (ADR 0006) — public, write-only, safe to embed. */
  siteToken: string
  /** Collector URL events are POSTed to. No path is assumed; the Collector defines its own route. */
  endpoint: string
  /** Free-text deployment label (e.g. "production", "staging") attached to every event. */
  environment?: string
  /**
   * Whether tracking may start without an explicit `setConsent(true)` call.
   * "none" (default): cookie-free mode starts immediately, persistent visitor mode never activates.
   * "required": nothing is queued or sent until `setConsent(true)` is called.
   */
  consent?: 'none' | 'required'
  /** Fraction of sessions to track, 0–1. Applied once per session at session creation. Default 1. */
  sampleRate?: number
  /** Logs every outgoing payload and validation error to the console and skips the real network send. */
  debug?: boolean
  /** Treat hash changes as route changes and keep the fragment in the tracked path. Default false. */
  hashRouting?: boolean
  /** Glob patterns (e.g. "/admin/**") for routes that are never tracked. */
  excludeRoutes?: string[]
  /** Additional query parameter names to allow through, beyond the default UTM set. */
  queryAllowlist?: string[]
  /**
   * Opts into a real, random, persistent visitor identifier stored client-side with the given TTL
   * (milliseconds). Never enabled by default — see definitions doc §3.
   */
  persistentVisitorId?: { ttlMs: number }
}

export interface ResolvedConfig extends Required<Pick<SdkConfig, 'siteToken' | 'endpoint'>> {
  environment: string | undefined
  consent: 'none' | 'required'
  sampleRate: number
  debug: boolean
  hashRouting: boolean
  excludeRoutes: string[]
  queryAllowlist: string[]
  persistentVisitorId: { ttlMs: number } | undefined
}

export type AcquisitionChannel = 'direct' | 'search' | 'social' | 'referral'

export interface CampaignContext {
  channel: AcquisitionChannel
  referrer: string | null
  utm: Record<string, string>
}

export type EventName = 'page_view' | 'engagement' | (string & {})

export interface OutgoingEvent {
  /** Client-generated, unique per event — lets the Collector dedupe a retried batch. */
  id: string
  name: EventName
  siteToken: string
  environment: string | undefined
  sessionId: string
  visitorId: string | null
  url: string
  timestamp: string
  properties: Record<string, unknown>
}
