import { isDenylistedParamName } from './url'

export const MAX_EVENT_NAME_LENGTH = 100
export const MAX_PROPERTY_COUNT = 25
export const MAX_PROPERTY_KEY_LENGTH = 100
export const MAX_PROPERTY_STRING_LENGTH = 500

export interface ValidationResult {
  valid: boolean
  errors: string[]
  properties: Record<string, unknown>
}

/**
 * Bounds custom-event input so a misbehaving or malicious page can't blow up payload size or leak
 * credential-shaped data through event properties — reuses the same denylist substrings as the
 * query-string policy (§6) as defense in depth on property names.
 */
export function validateEvent(
  name: string,
  properties: Record<string, unknown> = {},
): ValidationResult {
  const errors: string[] = []

  if (typeof name !== 'string' || name.trim().length === 0) {
    errors.push('Event name must be a non-empty string.')
  } else if (name.length > MAX_EVENT_NAME_LENGTH) {
    errors.push(`Event name exceeds ${MAX_EVENT_NAME_LENGTH} characters.`)
  }

  const sanitized: Record<string, unknown> = {}
  const entries = Object.entries(properties ?? {})

  if (entries.length > MAX_PROPERTY_COUNT) {
    errors.push(`Event has more than ${MAX_PROPERTY_COUNT} properties.`)
  }

  for (const [key, value] of entries.slice(0, MAX_PROPERTY_COUNT)) {
    if (key.length > MAX_PROPERTY_KEY_LENGTH) {
      errors.push(`Property name "${key}" exceeds ${MAX_PROPERTY_KEY_LENGTH} characters.`)
      continue
    }
    if (isDenylistedParamName(key)) {
      errors.push(`Property name "${key}" is blocked (matches a denylisted term).`)
      continue
    }
    if (value === null || typeof value === 'boolean' || typeof value === 'number') {
      sanitized[key] = value
      continue
    }
    if (typeof value === 'string') {
      sanitized[key] =
        value.length > MAX_PROPERTY_STRING_LENGTH
          ? value.slice(0, MAX_PROPERTY_STRING_LENGTH)
          : value
      continue
    }
    errors.push(
      `Property "${key}" has an unsupported type; only string, number, boolean and null are allowed.`,
    )
  }

  return { valid: errors.length === 0, errors, properties: sanitized }
}
