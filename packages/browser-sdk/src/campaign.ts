import { filterQueryParams } from './url'
import type { AcquisitionChannel, CampaignContext } from './types'

const SEARCH_ENGINE_HOSTNAMES = [
  'google.com',
  'bing.com',
  'yahoo.com',
  'duckduckgo.com',
  'baidu.com',
  'yandex.com',
  'ecosia.org',
]

const SOCIAL_HOSTNAMES = [
  'facebook.com',
  't.co',
  'twitter.com',
  'x.com',
  'linkedin.com',
  'instagram.com',
  'pinterest.com',
  'reddit.com',
  'tiktok.com',
]

function hostnameMatches(hostname: string, knownHosts: string[]): boolean {
  return knownHosts.some((known) => hostname === known || hostname.endsWith(`.${known}`))
}

export function classifyChannel(referrer: string, currentHostname: string): AcquisitionChannel {
  if (!referrer) return 'direct'

  let referrerHostname: string
  try {
    referrerHostname = new URL(referrer).hostname
  } catch {
    return 'direct'
  }

  if (referrerHostname === currentHostname) return 'direct'
  if (hostnameMatches(referrerHostname, SEARCH_ENGINE_HOSTNAMES)) return 'search'
  if (hostnameMatches(referrerHostname, SOCIAL_HOSTNAMES)) return 'social'
  return 'referral'
}

export function parseCampaignContext(
  referrer: string,
  currentUrl: URL,
  queryAllowlist: string[],
): CampaignContext {
  return {
    channel: classifyChannel(referrer, currentUrl.hostname),
    referrer: referrer || null,
    utm: filterQueryParams(currentUrl.search, queryAllowlist),
  }
}
