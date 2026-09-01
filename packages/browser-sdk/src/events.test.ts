import { describe, expect, it } from 'vitest'
import { MAX_PROPERTY_COUNT, MAX_PROPERTY_STRING_LENGTH, validateEvent } from './events'

describe('validateEvent', () => {
  it('accepts a well-formed name and properties', () => {
    const result = validateEvent('signup_completed', { plan: 'pro', trial: false, seats: 3 })
    expect(result.valid).toBe(true)
    expect(result.properties).toEqual({ plan: 'pro', trial: false, seats: 3 })
  })

  it('rejects an empty event name', () => {
    expect(validateEvent('').valid).toBe(false)
    expect(validateEvent('   ').valid).toBe(false)
  })

  it('rejects too many properties', () => {
    const properties = Object.fromEntries(
      Array.from({ length: MAX_PROPERTY_COUNT + 1 }, (_, i) => [`p${i}`, i]),
    )
    expect(validateEvent('big_event', properties).valid).toBe(false)
  })

  it('truncates an over-long string property instead of rejecting the whole event', () => {
    const longValue = 'x'.repeat(MAX_PROPERTY_STRING_LENGTH + 50)
    const result = validateEvent('note_added', { note: longValue })
    expect(result.valid).toBe(true)
    expect(result.properties.note).toHaveLength(MAX_PROPERTY_STRING_LENGTH)
  })

  it('blocks a property name that matches the denylist', () => {
    const result = validateEvent('login', { auth_token: 'abc123' })
    expect(result.valid).toBe(false)
    expect(result.errors.some((e) => e.includes('auth_token'))).toBe(true)
  })

  it('rejects an unsupported property value type', () => {
    const result = validateEvent('weird_event', { nested: { a: 1 } })
    expect(result.valid).toBe(false)
  })
})
