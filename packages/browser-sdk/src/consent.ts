import { readStorage, removeStorage, writeStorage } from './storage'

/**
 * "none" (default): tracking may start immediately, no explicit consent call required.
 * "required": nothing is queued or sent until `setConsent(true)` is called — the embedding site's
 * own consent-management UI is responsible for calling it once its own stored consent state allows.
 *
 * `optOut()` is a separate, persistent "don't track me" choice (stored client-side, survives
 * reloads) — unlike `consent`, which is not persisted by the SDK itself and must be re-asserted by
 * the integrator on each page load from their own CMP's stored state.
 */
export type ConsentMode = 'none' | 'required'

export class ConsentManager {
  private readonly storageKey: string
  private granted: boolean

  constructor(siteToken: string, mode: ConsentMode) {
    this.storageKey = `telumera:${siteToken}:optout`
    this.granted = mode === 'none'
  }

  private isOptedOut(): boolean {
    return readStorage(this.storageKey) === '1'
  }

  /** Nothing may be sent while this is false. */
  isSatisfied(): boolean {
    return this.granted && !this.isOptedOut()
  }

  setConsent(granted: boolean): void {
    this.granted = granted
  }

  optOut(): void {
    this.granted = false
    writeStorage(this.storageKey, '1')
  }

  optIn(): void {
    removeStorage(this.storageKey)
    this.granted = true
  }
}
