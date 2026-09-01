import { beforeEach, describe, expect, it } from 'vitest'
import { VisitorStore } from './visitor'

beforeEach(() => {
  window.localStorage.clear()
})

describe('VisitorStore', () => {
  it('persists the same visitor id within the TTL', () => {
    let now = 0
    const store = new VisitorStore('site-1', 10_000, () => now)

    const first = store.getOrCreate()
    now += 5_000
    const second = store.getOrCreate()

    expect(second).toBe(first)
  })

  it('creates a new visitor id once the TTL expires', () => {
    let now = 0
    const store = new VisitorStore('site-1', 10_000, () => now)

    const first = store.getOrCreate()
    now += 10_001
    const second = store.getOrCreate()

    expect(second).not.toBe(first)
  })

  it('clear() removes the stored visitor id', () => {
    const store = new VisitorStore('site-1', 10_000, () => 0)

    const first = store.getOrCreate()
    store.clear()
    const second = store.getOrCreate()

    expect(second).not.toBe(first)
  })
})
