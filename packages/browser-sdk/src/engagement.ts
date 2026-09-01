/** Definitions doc §5 — pause after 30s with no interaction signal while the tab is visible. */
const IDLE_TIMEOUT_MS = 30 * 1000
/** Bounded periodic summary so a long-lived SPA session that never unloads isn't lost entirely. */
const HEARTBEAT_INTERVAL_MS = 60 * 1000
const INTERACTION_EVENTS = ['mousemove', 'keydown', 'scroll', 'touchstart'] as const
const INTERACTION_THROTTLE_MS = 1000
const IDLE_CHECK_INTERVAL_MS = 1000

export interface EngagementTrackerOptions {
  now?: () => number
  idleTimeoutMs?: number
  heartbeatIntervalMs?: number
}

/**
 * Definitions doc §5 — active time is wall-clock time on the page minus hidden-tab time and idle
 * time. Delivery is via the caller's flush callback, which the SDK wires to `navigator.sendBeacon`
 * on `visibilitychange`/`pagehide`, plus this tracker's own bounded periodic heartbeat.
 */
export class EngagementTracker {
  private readonly now: () => number
  private readonly idleTimeoutMs: number
  private readonly heartbeatIntervalMs: number
  private readonly onFlush: (activeMs: number) => void

  private activeMs = 0
  private segmentStart: number | null = null
  private lastInteractionAt: number
  private lastInteractionLoggedAt = 0
  private idleCheckTimer: ReturnType<typeof setInterval> | null = null
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null

  constructor(onFlush: (activeMs: number) => void, options: EngagementTrackerOptions = {}) {
    this.onFlush = onFlush
    this.now = options.now ?? Date.now
    this.idleTimeoutMs = options.idleTimeoutMs ?? IDLE_TIMEOUT_MS
    this.heartbeatIntervalMs = options.heartbeatIntervalMs ?? HEARTBEAT_INTERVAL_MS
    this.lastInteractionAt = this.now()
  }

  start(): void {
    if (typeof document === 'undefined') return

    if (document.visibilityState === 'visible') this.resumeSegment()

    document.addEventListener('visibilitychange', this.handleVisibilityChange)
    window.addEventListener('pagehide', this.handlePageHide)
    for (const type of INTERACTION_EVENTS) {
      window.addEventListener(type, this.handleInteraction, { passive: true })
    }
    this.idleCheckTimer = setInterval(() => this.checkIdle(), IDLE_CHECK_INTERVAL_MS)
    this.heartbeatTimer = setInterval(() => this.flush(), this.heartbeatIntervalMs)
  }

  stop(): void {
    document.removeEventListener('visibilitychange', this.handleVisibilityChange)
    window.removeEventListener('pagehide', this.handlePageHide)
    for (const type of INTERACTION_EVENTS) {
      window.removeEventListener(type, this.handleInteraction)
    }
    if (this.idleCheckTimer !== null) clearInterval(this.idleCheckTimer)
    if (this.heartbeatTimer !== null) clearInterval(this.heartbeatTimer)
    this.pauseSegment()
  }

  private handleInteraction = (): void => {
    const now = this.now()
    if (now - this.lastInteractionLoggedAt < INTERACTION_THROTTLE_MS) return
    this.lastInteractionLoggedAt = now
    this.lastInteractionAt = now
    if (this.segmentStart === null && document.visibilityState === 'visible') {
      this.resumeSegment()
    }
  }

  private checkIdle(): void {
    if (this.segmentStart === null) return
    if (this.now() - this.lastInteractionAt > this.idleTimeoutMs) {
      this.pauseSegment()
    }
  }

  private handleVisibilityChange = (): void => {
    if (document.visibilityState === 'hidden') {
      this.pauseSegment()
      this.flush()
    } else {
      this.lastInteractionAt = this.now()
      this.resumeSegment()
    }
  }

  private handlePageHide = (): void => {
    this.pauseSegment()
    this.flush()
  }

  private resumeSegment(): void {
    if (this.segmentStart === null) this.segmentStart = this.now()
  }

  private pauseSegment(): void {
    if (this.segmentStart !== null) {
      this.activeMs += this.now() - this.segmentStart
      this.segmentStart = null
    }
  }

  private flush(): void {
    if (this.segmentStart !== null) {
      const now = this.now()
      this.activeMs += now - this.segmentStart
      this.segmentStart = now
    }
    if (this.activeMs <= 0) return
    const delta = this.activeMs
    this.activeMs = 0
    this.onFlush(delta)
  }
}
