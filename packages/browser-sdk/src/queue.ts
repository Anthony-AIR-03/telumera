import type { OutgoingEvent } from './types'

/** Bounded memory: past this many buffered events, the oldest are dropped, not the newest. */
const MAX_QUEUE_SIZE = 50
const DEFAULT_BATCH_SIZE = 10
const DEFAULT_FLUSH_INTERVAL_MS = 5000
const MAX_RETRIES = 3
const RETRY_BASE_DELAY_MS = 500

export type Transport = (endpoint: string, events: OutgoingEvent[]) => Promise<boolean>

export interface QueueOptions {
  endpoint: string
  batchSize?: number
  flushIntervalMs?: number
  /** Observes every batch right before it's sent — used by debug mode. */
  onSend?: (events: OutgoingEvent[]) => void
  /** Injectable for tests; defaults to a `fetch`-based POST. */
  send?: Transport
}

async function defaultFetchTransport(endpoint: string, events: OutgoingEvent[]): Promise<boolean> {
  try {
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ events }),
      keepalive: true,
    })
    return response.ok
  } catch {
    return false
  }
}

/**
 * In-memory batching queue: flushes on a size threshold or a timer, retries a failed batch with
 * exponential backoff a bounded number of times, then drops it — an unbounded retry queue would
 * itself become a memory/privacy liability on a page that never navigates away.
 */
export class EventQueue {
  private buffer: OutgoingEvent[] = []
  private timer: ReturnType<typeof setInterval> | null = null
  private readonly endpoint: string
  private readonly batchSize: number
  private readonly flushIntervalMs: number
  private readonly onSend: ((events: OutgoingEvent[]) => void) | undefined
  private readonly send: Transport

  constructor(options: QueueOptions) {
    this.endpoint = options.endpoint
    this.batchSize = options.batchSize ?? DEFAULT_BATCH_SIZE
    this.flushIntervalMs = options.flushIntervalMs ?? DEFAULT_FLUSH_INTERVAL_MS
    this.onSend = options.onSend
    this.send = options.send ?? defaultFetchTransport
    this.timer = setInterval(() => void this.flush(), this.flushIntervalMs)
  }

  /** Current buffered (not-yet-sent) event count — for tests/observability only. */
  get size(): number {
    return this.buffer.length
  }

  enqueue(event: OutgoingEvent): void {
    this.buffer.push(event)
    if (this.buffer.length > MAX_QUEUE_SIZE) {
      this.buffer.splice(0, this.buffer.length - MAX_QUEUE_SIZE)
    }
    if (this.buffer.length >= this.batchSize) {
      void this.flush()
    }
  }

  stop(): void {
    if (this.timer !== null) {
      clearInterval(this.timer)
      this.timer = null
    }
  }

  async flush(): Promise<void> {
    if (this.buffer.length === 0) return
    const batch = this.buffer.splice(0, this.buffer.length)
    this.onSend?.(batch)
    await this.sendWithRetry(batch)
  }

  /** Best-effort synchronous flush for page unload (`pagehide`/`visibilitychange`) — no retry. */
  flushSync(): void {
    if (this.buffer.length === 0) return
    const batch = this.buffer.splice(0, this.buffer.length)
    this.onSend?.(batch)
    if (typeof navigator !== 'undefined' && typeof navigator.sendBeacon === 'function') {
      const blob = new Blob([JSON.stringify({ events: batch })], { type: 'application/json' })
      navigator.sendBeacon(this.endpoint, blob)
    }
  }

  private async sendWithRetry(batch: OutgoingEvent[], attempt = 0): Promise<void> {
    const ok = await this.send(this.endpoint, batch)
    if (ok || attempt >= MAX_RETRIES) return
    const delayMs = RETRY_BASE_DELAY_MS * 2 ** attempt
    await new Promise((resolve) => setTimeout(resolve, delayMs))
    await this.sendWithRetry(batch, attempt + 1)
  }
}
