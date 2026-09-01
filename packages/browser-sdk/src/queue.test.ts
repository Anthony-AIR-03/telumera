import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EventQueue } from './queue'
import type { OutgoingEvent } from './types'

function makeEvent(name: string): OutgoingEvent {
  return {
    id: crypto.randomUUID(),
    name,
    siteToken: 'site-1',
    environment: undefined,
    sessionId: 'session-1',
    visitorId: null,
    url: 'https://example.com/',
    timestamp: new Date().toISOString(),
    properties: {},
  }
}

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('EventQueue batching', () => {
  it('flushes automatically once the batch size is reached', async () => {
    const send = vi.fn().mockResolvedValue(true)
    const queue = new EventQueue({ endpoint: 'x', batchSize: 3, flushIntervalMs: 1_000_000, send })

    queue.enqueue(makeEvent('a'))
    queue.enqueue(makeEvent('b'))
    expect(send).not.toHaveBeenCalled()

    queue.enqueue(makeEvent('c'))
    await vi.advanceTimersByTimeAsync(0)

    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0]![1]).toHaveLength(3)
    queue.stop()
  })

  it('flushes on the timer even below the batch size', async () => {
    const send = vi.fn().mockResolvedValue(true)
    const queue = new EventQueue({ endpoint: 'x', batchSize: 100, flushIntervalMs: 1_000, send })

    queue.enqueue(makeEvent('a'))
    await vi.advanceTimersByTimeAsync(1_000)

    expect(send).toHaveBeenCalledTimes(1)
    queue.stop()
  })

  it('drops the oldest buffered events once the memory cap is exceeded', () => {
    const send = vi.fn().mockResolvedValue(true)
    const queue = new EventQueue({
      endpoint: 'x',
      batchSize: 1000,
      flushIntervalMs: 1_000_000,
      send,
    })

    for (let i = 0; i < 60; i++) queue.enqueue(makeEvent(`e${i}`))

    expect(queue.size).toBe(50)
    queue.stop()
  })
})

describe('EventQueue retry', () => {
  it('retries a failed batch with backoff before succeeding', async () => {
    const send = vi
      .fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true)
    const queue = new EventQueue({ endpoint: 'x', batchSize: 1, flushIntervalMs: 1_000_000, send })

    queue.enqueue(makeEvent('a'))
    await vi.advanceTimersByTimeAsync(2_000)

    expect(send).toHaveBeenCalledTimes(3)
    queue.stop()
  })

  it('gives up after exhausting retries and drops the batch', async () => {
    const send = vi.fn().mockResolvedValue(false)
    const queue = new EventQueue({ endpoint: 'x', batchSize: 1, flushIntervalMs: 1_000_000, send })

    queue.enqueue(makeEvent('a'))
    await vi.advanceTimersByTimeAsync(10_000)

    expect(send).toHaveBeenCalledTimes(4)
    expect(queue.size).toBe(0)
    queue.stop()
  })
})

describe('EventQueue.flushSync', () => {
  it('sends the buffered batch via navigator.sendBeacon and clears the buffer', () => {
    const sendBeacon = vi.fn().mockReturnValue(true)
    vi.stubGlobal('navigator', { ...navigator, sendBeacon })

    const queue = new EventQueue({
      endpoint: 'https://collect.example',
      flushIntervalMs: 1_000_000,
    })
    queue.enqueue(makeEvent('a'))
    queue.flushSync()

    expect(sendBeacon).toHaveBeenCalledTimes(1)
    expect(sendBeacon.mock.calls[0]![0]).toBe('https://collect.example')
    expect(queue.size).toBe(0)
    queue.stop()
  })

  it('does nothing when the buffer is empty', () => {
    const sendBeacon = vi.fn()
    vi.stubGlobal('navigator', { ...navigator, sendBeacon })

    const queue = new EventQueue({
      endpoint: 'https://collect.example',
      flushIntervalMs: 1_000_000,
    })
    queue.flushSync()

    expect(sendBeacon).not.toHaveBeenCalled()
    queue.stop()
  })
})
