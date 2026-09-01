import { parseCampaignContext } from './campaign'
import { resolveConfig } from './config'
import { ConsentManager } from './consent'
import { createDebugTransport, logDroppedEvent, logValidationErrors } from './debug'
import { EngagementTracker } from './engagement'
import { validateEvent } from './events'
import { PageViewTracker } from './pageview'
import { EventQueue } from './queue'
import { SessionStore } from './session'
import { VisitorStore } from './visitor'
import type { CanonicalUrl } from './url'
import type { RouterLike } from './pageview'
import type { OutgoingEvent, ResolvedConfig, SdkConfig } from './types'

export type {
  SdkConfig,
  ResolvedConfig,
  AcquisitionChannel,
  CampaignContext,
  OutgoingEvent,
} from './types'
export type { RouterLike, RouteLike } from './pageview'

export class TelumeraClient {
  private readonly config: ResolvedConfig
  private readonly consentManager: ConsentManager
  private readonly sessionStore: SessionStore
  private readonly visitorStore: VisitorStore | null
  private readonly queue: EventQueue
  private readonly pageViewTracker: PageViewTracker
  private readonly engagementTracker: EngagementTracker

  constructor(config: SdkConfig) {
    this.config = resolveConfig(config)
    this.consentManager = new ConsentManager(this.config.siteToken, this.config.consent)
    this.sessionStore = new SessionStore(this.config.siteToken)
    this.visitorStore = this.config.persistentVisitorId
      ? new VisitorStore(this.config.siteToken, this.config.persistentVisitorId.ttlMs)
      : null

    this.queue = new EventQueue({
      endpoint: this.config.endpoint,
      send: this.config.debug ? createDebugTransport() : undefined,
    })

    this.pageViewTracker = new PageViewTracker(
      {
        hashRouting: this.config.hashRouting,
        queryAllowlist: this.config.queryAllowlist,
        excludeRoutes: this.config.excludeRoutes,
      },
      (canonical) => this.enqueuePageView(canonical),
    )

    this.engagementTracker = new EngagementTracker((activeMs) => this.enqueueEngagement(activeMs))

    if (typeof window !== 'undefined') {
      this.pageViewTracker.trackInitial(new URL(window.location.href))
      this.engagementTracker.start()
      // Registered after the engagement tracker's own pagehide/visibilitychange listeners, so its
      // final engagement event is already enqueued by the time this synchronous flush runs.
      window.addEventListener('pagehide', () => this.queue.flushSync())
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') this.queue.flushSync()
      })
    }
  }

  /** Wires up SPA page-view tracking against a Vue Router instance (duck-typed, no hard import). */
  trackRouter(router: RouterLike): void {
    this.pageViewTracker.trackRouter(router)
  }

  track(name: string, properties: Record<string, unknown> = {}): void {
    const result = validateEvent(name, properties)
    if (!result.valid) {
      if (this.config.debug) logValidationErrors(`track("${name}")`, result.errors)
      return
    }
    this.enqueueCustom(name, result.properties)
  }

  setConsent(granted: boolean): void {
    this.consentManager.setConsent(granted)
  }

  optOut(): void {
    this.consentManager.optOut()
    this.visitorStore?.clear()
  }

  optIn(): void {
    this.consentManager.optIn()
  }

  flush(): Promise<void> {
    return this.queue.flush()
  }

  private buildEvent(name: string, properties: Record<string, unknown>): OutgoingEvent | null {
    if (!this.consentManager.isSatisfied()) {
      if (this.config.debug) logDroppedEvent(name, 'consent not satisfied')
      return null
    }

    const context = parseCampaignContext(
      document.referrer,
      new URL(window.location.href),
      this.config.queryAllowlist,
    )
    const session = this.sessionStore.touch(context, () => Math.random() < this.config.sampleRate)
    if (!session.sampled) {
      if (this.config.debug) logDroppedEvent(name, 'session not sampled')
      return null
    }

    return {
      name,
      siteToken: this.config.siteToken,
      environment: this.config.environment,
      sessionId: session.id,
      visitorId: this.visitorStore ? this.visitorStore.getOrCreate() : null,
      url: window.location.href,
      timestamp: new Date().toISOString(),
      properties,
    }
  }

  private enqueuePageView(canonical: CanonicalUrl): void {
    const event = this.buildEvent('page_view', { path: canonical.path, query: canonical.query })
    if (event) this.queue.enqueue(event)
  }

  private enqueueEngagement(activeMs: number): void {
    const event = this.buildEvent('engagement', { activeMs })
    if (event) this.queue.enqueue(event)
  }

  private enqueueCustom(name: string, properties: Record<string, unknown>): void {
    const event = this.buildEvent(name, properties)
    if (event) this.queue.enqueue(event)
  }
}

export function init(config: SdkConfig): TelumeraClient {
  return new TelumeraClient(config)
}
