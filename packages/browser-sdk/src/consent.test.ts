import { beforeEach, describe, expect, it } from 'vitest'
import { ConsentManager } from './consent'

beforeEach(() => {
  window.localStorage.clear()
})

describe('ConsentManager', () => {
  it('is satisfied immediately in "none" mode', () => {
    const consent = new ConsentManager('site-1', 'none')
    expect(consent.isSatisfied()).toBe(true)
  })

  it('is not satisfied in "required" mode until setConsent(true) is called', () => {
    const consent = new ConsentManager('site-1', 'required')
    expect(consent.isSatisfied()).toBe(false)
    consent.setConsent(true)
    expect(consent.isSatisfied()).toBe(true)
  })

  it('optOut() persists across a new instance for the same site token', () => {
    const first = new ConsentManager('site-1', 'none')
    first.optOut()
    expect(first.isSatisfied()).toBe(false)

    const second = new ConsentManager('site-1', 'none')
    expect(second.isSatisfied()).toBe(false)
  })

  it('optIn() clears a persisted opt-out', () => {
    const consent = new ConsentManager('site-1', 'none')
    consent.optOut()
    consent.optIn()
    expect(consent.isSatisfied()).toBe(true)
  })

  it('scopes opt-out storage per site token', () => {
    const siteA = new ConsentManager('site-a', 'none')
    siteA.optOut()

    const siteB = new ConsentManager('site-b', 'none')
    expect(siteB.isSatisfied()).toBe(true)
  })
})
