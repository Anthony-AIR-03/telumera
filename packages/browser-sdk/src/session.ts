import { readJson, writeJson } from './storage'
import type { CampaignContext } from './types'

/** Definitions doc §2 — 30-minute inactivity timeout, no calendar-day cutoff. */
export const SESSION_TIMEOUT_MS = 30 * 60 * 1000

interface StoredSession {
  id: string
  lastActivityAt: number
  campaignKey: string
  sampled: boolean
}

export interface SessionResult {
  id: string
  /** Whether this session was chosen to be tracked — decided once, at session creation. */
  sampled: boolean
}

/**
 * Identifies the acquisition context a session started under. Only channel + UTM params
 * participate — two internal navigations both classified "direct" with no UTM params produce the
 * same key, so ordinary in-site browsing never spuriously restarts a session.
 */
function campaignKey(context: CampaignContext): string {
  const utmEntries = Object.entries(context.utm).sort(([a], [b]) => a.localeCompare(b))
  return `${context.channel}|${JSON.stringify(utmEntries)}`
}

function createSessionId(): string {
  return crypto.randomUUID()
}

/**
 * Cross-tab session state, backed by localStorage (not sessionStorage, which is per-tab and would
 * incorrectly start a new session per opened tab — definitions doc §2).
 */
export class SessionStore {
  private readonly storageKey: string
  private readonly now: () => number

  constructor(siteToken: string, now: () => number = Date.now) {
    this.storageKey = `telumera:${siteToken}:session`
    this.now = now
  }

  /**
   * Returns the current session, creating or restarting it per §2's rules. `decideSampled` is only
   * invoked when a new session is created — the sampling decision is made once per session and then
   * held for its lifetime, not re-rolled on every event.
   */
  touch(context: CampaignContext, decideSampled: () => boolean): SessionResult {
    const now = this.now()
    const key = campaignKey(context)
    const existing = readJson<StoredSession>(this.storageKey)

    const expired = existing !== null && now - existing.lastActivityAt > SESSION_TIMEOUT_MS
    const newCampaign = existing !== null && existing.campaignKey !== key

    if (existing === null || expired || newCampaign) {
      const session: StoredSession = {
        id: createSessionId(),
        lastActivityAt: now,
        campaignKey: key,
        sampled: decideSampled(),
      }
      writeJson(this.storageKey, session)
      return session
    }

    writeJson(this.storageKey, { ...existing, lastActivityAt: now })
    return existing
  }
}
