import { canonicalizeUrl, isExcludedRoute, type CanonicalUrl } from './url'

export interface RouteLike {
  matched: { path: string }[]
  fullPath: string
}

/** Duck-typed against Vue Router's Router interface — kept as an optional peer, not a hard import. */
export interface RouterLike {
  afterEach(callback: (to: RouteLike, from: RouteLike) => void): void
  currentRoute?: { value: RouteLike }
}

export interface PageViewTrackerOptions {
  hashRouting: boolean
  queryAllowlist: string[]
  excludeRoutes: string[]
}

function routeKey(route: RouteLike): string {
  return route.matched.map((record) => record.path).join('>')
}

/**
 * Definitions doc §1: a page view fires on fresh document load, or when a router navigation resolves
 * to a *different matched route component* than the one currently tracked — a query/hash-only change
 * on the same component is a page-view update, not a new view. The router's own initial-navigation
 * afterEach call must not double-count the SDK's own init-time page view for the same URL.
 */
export class PageViewTracker {
  private readonly options: PageViewTrackerOptions
  private readonly onPageView: (canonical: CanonicalUrl) => void
  private lastMatchedKey: string | null = null
  private initialUrlKey: string | null = null

  constructor(options: PageViewTrackerOptions, onPageView: (canonical: CanonicalUrl) => void) {
    this.options = options
    this.onPageView = onPageView
  }

  trackInitial(url: URL): void {
    this.initialUrlKey = this.canonicalKey(url)
    this.emit(url)
  }

  trackRouter(router: RouterLike): void {
    router.afterEach((to) => {
      const matchedKey = routeKey(to)
      const url = new URL(to.fullPath, window.location.origin)
      const urlKey = this.canonicalKey(url)

      if (this.lastMatchedKey === null && urlKey === this.initialUrlKey) {
        // The router's own initial navigation resolving to the URL the SDK already tracked at init.
        this.lastMatchedKey = matchedKey
        return
      }

      if (matchedKey === this.lastMatchedKey) return
      this.lastMatchedKey = matchedKey
      this.emit(url)
    })
  }

  private canonicalKey(url: URL): string {
    const canonical = canonicalizeUrl(url, this.options)
    return `${canonical.path}?${new URLSearchParams(canonical.query).toString()}`
  }

  private emit(url: URL): void {
    const canonical = canonicalizeUrl(url, this.options)
    if (isExcludedRoute(canonical.path, this.options.excludeRoutes)) return
    this.onPageView(canonical)
  }
}
