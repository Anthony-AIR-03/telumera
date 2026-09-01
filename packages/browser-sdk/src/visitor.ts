import { readJson, removeStorage, writeJson } from './storage'

interface StoredVisitor {
  id: string
  expiresAt: number
}

/**
 * Definitions doc §3 — default mode has NO client-side visitor identifier at all; the Collector
 * computes a daily, uncorrelatable hash server-side from IP/UA/salt. This store only exists for the
 * opt-in, consent-gated "persistent mode" a site owner explicitly enables via
 * `SdkConfig.persistentVisitorId`.
 */
export class VisitorStore {
  private readonly storageKey: string
  private readonly ttlMs: number
  private readonly now: () => number

  constructor(siteToken: string, ttlMs: number, now: () => number = Date.now) {
    this.storageKey = `telumera:${siteToken}:visitor`
    this.ttlMs = ttlMs
    this.now = now
  }

  getOrCreate(): string {
    const now = this.now()
    const existing = readJson<StoredVisitor>(this.storageKey)
    if (existing !== null && existing.expiresAt > now) {
      return existing.id
    }

    const visitor: StoredVisitor = { id: crypto.randomUUID(), expiresAt: now + this.ttlMs }
    writeJson(this.storageKey, visitor)
    return visitor.id
  }

  clear(): void {
    removeStorage(this.storageKey)
  }
}
