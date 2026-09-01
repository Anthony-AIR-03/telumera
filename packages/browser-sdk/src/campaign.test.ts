import { describe, expect, it } from 'vitest'
import { classifyChannel, parseCampaignContext } from './campaign'

describe('classifyChannel', () => {
  it('classifies an empty referrer as direct', () => {
    expect(classifyChannel('', 'example.com')).toBe('direct')
  })

  it('classifies a same-host referrer as direct', () => {
    expect(classifyChannel('https://example.com/other-page', 'example.com')).toBe('direct')
  })

  it('classifies a known search engine referrer as search', () => {
    expect(classifyChannel('https://www.google.com/search?q=x', 'example.com')).toBe('search')
  })

  it('classifies a known social referrer as social', () => {
    expect(classifyChannel('https://t.co/abc', 'example.com')).toBe('social')
  })

  it('classifies an unrecognized external referrer as referral', () => {
    expect(classifyChannel('https://some-blog.example.net/post', 'example.com')).toBe('referral')
  })

  it('falls back to direct for an unparseable referrer', () => {
    expect(classifyChannel('not-a-url', 'example.com')).toBe('direct')
  })
})

describe('parseCampaignContext', () => {
  it('extracts allowlisted utm params and classifies the channel', () => {
    const context = parseCampaignContext(
      'https://www.google.com/',
      new URL('https://example.com/landing?utm_source=google&utm_medium=cpc&irrelevant=1'),
      ['utm_source', 'utm_medium'],
    )

    expect(context.channel).toBe('search')
    expect(context.utm).toEqual({ utm_source: 'google', utm_medium: 'cpc' })
  })
})
