import { DEFAULT_QUERY_ALLOWLIST } from './url'
import type { ResolvedConfig, SdkConfig } from './types'

export function resolveConfig(config: SdkConfig): ResolvedConfig {
  if (!config.siteToken) {
    throw new Error('[telumera] init() requires a siteToken.')
  }
  if (!config.endpoint) {
    throw new Error('[telumera] init() requires an endpoint.')
  }

  const sampleRate = config.sampleRate ?? 1
  if (sampleRate < 0 || sampleRate > 1) {
    throw new Error('[telumera] sampleRate must be between 0 and 1.')
  }

  return {
    siteToken: config.siteToken,
    endpoint: config.endpoint,
    environment: config.environment,
    consent: config.consent ?? 'none',
    sampleRate,
    debug: config.debug ?? false,
    hashRouting: config.hashRouting ?? false,
    excludeRoutes: config.excludeRoutes ?? [],
    queryAllowlist: [...DEFAULT_QUERY_ALLOWLIST, ...(config.queryAllowlist ?? [])],
    persistentVisitorId: config.persistentVisitorId,
  }
}
