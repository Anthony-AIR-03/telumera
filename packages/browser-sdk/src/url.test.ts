import { describe, expect, it } from 'vitest'
import { canonicalizeUrl, filterQueryParams, isExcludedRoute, isDenylistedParamName } from './url'

describe('canonicalizeUrl', () => {
  it('strips a trailing slash except for the root path', () => {
    expect(
      canonicalizeUrl(new URL('https://example.com/about/'), {
        hashRouting: false,
        queryAllowlist: [],
      }).path,
    ).toBe('/about')
    expect(
      canonicalizeUrl(new URL('https://example.com/'), { hashRouting: false, queryAllowlist: [] })
        .path,
    ).toBe('/')
  })

  it('strips the fragment by default', () => {
    const canonical = canonicalizeUrl(new URL('https://example.com/about#section'), {
      hashRouting: false,
      queryAllowlist: [],
    })
    expect(canonical.path).toBe('/about')
  })

  it('treats the fragment as the tracked path when hashRouting is enabled', () => {
    const canonical = canonicalizeUrl(new URL('https://example.com/#/dashboard?tab=sites'), {
      hashRouting: true,
      queryAllowlist: ['tab'],
    })
    expect(canonical.path).toBe('/dashboard')
    expect(canonical.query).toEqual({ tab: 'sites' })
  })

  it('preserves path casing', () => {
    expect(
      canonicalizeUrl(new URL('https://example.com/AboutUs'), {
        hashRouting: false,
        queryAllowlist: [],
      }).path,
    ).toBe('/AboutUs')
  })
})

describe('filterQueryParams', () => {
  it('drops any parameter not on the allowlist', () => {
    expect(filterQueryParams('?utm_source=x&other=y', ['utm_source'])).toEqual({ utm_source: 'x' })
  })

  it('lets the denylist win even over an allowlisted name', () => {
    expect(filterQueryParams('?session_token=abc', ['session_token'])).toEqual({})
  })
})

describe('isDenylistedParamName', () => {
  it('matches case-insensitively on a substring', () => {
    expect(isDenylistedParamName('X-Auth-Header')).toBe(true)
    expect(isDenylistedParamName('utm_source')).toBe(false)
  })
})

describe('isExcludedRoute', () => {
  it('matches a glob pattern with a wildcard segment', () => {
    expect(isExcludedRoute('/admin/users', ['/admin/**'])).toBe(true)
    expect(isExcludedRoute('/public/users', ['/admin/**'])).toBe(false)
  })
})
