import type { Transport } from './queue'

/**
 * Debug mode implies dry-run: log exactly what would be sent, but never actually send it. A site
 * owner who wants to watch real traffic while debugging points `endpoint` at a staging Collector
 * instead of toggling a second flag.
 */
export function createDebugTransport(): Transport {
  return async (endpoint, events) => {
    console.info('[telumera] (debug) would POST to', endpoint, events)
    return true
  }
}

export function logValidationErrors(context: string, errors: string[]): void {
  if (errors.length === 0) return
  console.warn(`[telumera] (debug) ${context} rejected:`, errors)
}

export function logDroppedEvent(context: string, reason: string): void {
  console.warn(`[telumera] (debug) ${context} dropped: ${reason}`)
}
