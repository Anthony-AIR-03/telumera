import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EngagementTracker } from './engagement'

function setVisibility(state: DocumentVisibilityState): void {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true })
}

beforeEach(() => {
  vi.useFakeTimers()
  setVisibility('visible')
})

afterEach(() => {
  setVisibility('visible')
  vi.useRealTimers()
})

describe('EngagementTracker', () => {
  it('flushes accumulated active time when the tab becomes hidden', () => {
    const onFlush = vi.fn()
    const tracker = new EngagementTracker(onFlush, {
      idleTimeoutMs: 30_000,
      heartbeatIntervalMs: 1_000_000,
    })
    tracker.start()

    vi.advanceTimersByTime(5_000)
    setVisibility('hidden')
    document.dispatchEvent(new Event('visibilitychange'))

    expect(onFlush).toHaveBeenCalledTimes(1)
    expect(onFlush.mock.calls[0]![0]).toBeGreaterThanOrEqual(5_000)
    tracker.stop()
  })

  it('pauses after the idle timeout, so a later heartbeat reports only the active portion', () => {
    const onFlush = vi.fn()
    const tracker = new EngagementTracker(onFlush, {
      idleTimeoutMs: 2_000,
      heartbeatIntervalMs: 5_000,
    })
    tracker.start()

    vi.advanceTimersByTime(5_000)

    expect(onFlush).toHaveBeenCalledTimes(1)
    const [activeMs] = onFlush.mock.calls[0] as [number]
    expect(activeMs).toBeGreaterThanOrEqual(2_000)
    expect(activeMs).toBeLessThan(4_000)
    tracker.stop()
  })

  it('resumes accumulating after an interaction following an idle pause', () => {
    const onFlush = vi.fn()
    const tracker = new EngagementTracker(onFlush, {
      idleTimeoutMs: 2_000,
      heartbeatIntervalMs: 6_000,
    })
    tracker.start()

    vi.advanceTimersByTime(3_000)
    window.dispatchEvent(new Event('mousemove'))
    vi.advanceTimersByTime(3_000)

    expect(onFlush).toHaveBeenCalledTimes(1)
    const [activeMs] = onFlush.mock.calls[0] as [number]
    expect(activeMs).toBeGreaterThanOrEqual(5_000)
    tracker.stop()
  })

  it('does not accumulate active time while the tab is hidden', () => {
    const onFlush = vi.fn()
    const tracker = new EngagementTracker(onFlush, {
      idleTimeoutMs: 30_000,
      heartbeatIntervalMs: 2_000,
    })
    tracker.start()

    vi.advanceTimersByTime(1_000)
    setVisibility('hidden')
    document.dispatchEvent(new Event('visibilitychange'))
    onFlush.mockClear()

    vi.advanceTimersByTime(10_000)

    expect(onFlush).not.toHaveBeenCalled()
    tracker.stop()
  })
})
