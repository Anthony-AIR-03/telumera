import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SESSION_TIMEOUT_MS, SessionStore } from './session'
import type { CampaignContext } from './types'

const direct: CampaignContext = { channel: 'direct', referrer: null, utm: {} }
const searchCampaign: CampaignContext = {
  channel: 'search',
  referrer: 'https://google.com',
  utm: { utm_source: 'google' },
}

beforeEach(() => {
  window.localStorage.clear()
})

describe('SessionStore', () => {
  it('creates a new session on first touch and decides sampling once', () => {
    const store = new SessionStore('site-1', () => 0)
    const decide = vi.fn(() => true)

    const session = store.touch(direct, decide)

    expect(session.id).toBeTruthy()
    expect(session.sampled).toBe(true)
    expect(decide).toHaveBeenCalledTimes(1)
  })

  it('keeps the same session id on a subsequent touch within the timeout', () => {
    let now = 0
    const store = new SessionStore('site-1', () => now)

    const first = store.touch(direct, () => true)
    now += 60_000
    const second = store.touch(direct, () => true)

    expect(second.id).toBe(first.id)
  })

  it('starts a new session after the 30-minute inactivity timeout', () => {
    let now = 0
    const store = new SessionStore('site-1', () => now)

    const first = store.touch(direct, () => true)
    now += SESSION_TIMEOUT_MS + 1
    const second = store.touch(direct, () => true)

    expect(second.id).not.toBe(first.id)
  })

  it('restarts the session when the campaign context changes', () => {
    let now = 0
    const store = new SessionStore('site-1', () => now)

    const first = store.touch(direct, () => true)
    now += 1_000
    const second = store.touch(searchCampaign, () => true)

    expect(second.id).not.toBe(first.id)
  })

  it('does not re-decide sampling on a touch that continues the same session', () => {
    let now = 0
    const store = new SessionStore('site-1', () => now)
    const decide = vi.fn(() => true)

    store.touch(direct, decide)
    now += 1_000
    store.touch(direct, decide)

    expect(decide).toHaveBeenCalledTimes(1)
  })
})
