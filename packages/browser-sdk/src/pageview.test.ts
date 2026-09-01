import { describe, expect, it, vi } from 'vitest'
import { PageViewTracker } from './pageview'
import type { RouteLike, RouterLike } from './pageview'

function fakeRouter(): { router: RouterLike; fire: (to: RouteLike) => void } {
  let callback: (to: RouteLike, from: RouteLike) => void = () => {}
  const router: RouterLike = {
    afterEach: (cb) => {
      callback = cb
    },
  }
  return { router, fire: (to) => callback(to, { matched: [], fullPath: '' }) }
}

describe('PageViewTracker', () => {
  it('emits a page view on trackInitial', () => {
    const onPageView = vi.fn()
    const tracker = new PageViewTracker(
      { hashRouting: false, queryAllowlist: [], excludeRoutes: [] },
      onPageView,
    )

    tracker.trackInitial(new URL('https://example.com/home'))

    expect(onPageView).toHaveBeenCalledTimes(1)
    expect(onPageView.mock.calls[0]![0].path).toBe('/home')
  })

  it("suppresses the router's own initial-navigation double-fire for the URL already tracked at init", () => {
    const onPageView = vi.fn()
    const tracker = new PageViewTracker(
      { hashRouting: false, queryAllowlist: [], excludeRoutes: [] },
      onPageView,
    )
    tracker.trackInitial(new URL('https://example.com/home'))

    const { router, fire } = fakeRouter()
    tracker.trackRouter(router)
    fire({ matched: [{ path: '/home' }], fullPath: '/home' })

    expect(onPageView).toHaveBeenCalledTimes(1)
  })

  it('tracks a navigation to a different matched component as a new page view', () => {
    const onPageView = vi.fn()
    const tracker = new PageViewTracker(
      { hashRouting: false, queryAllowlist: [], excludeRoutes: [] },
      onPageView,
    )
    tracker.trackInitial(new URL('https://example.com/home'))

    const { router, fire } = fakeRouter()
    tracker.trackRouter(router)
    fire({ matched: [{ path: '/home' }], fullPath: '/home' }) // suppressed double-fire
    fire({ matched: [{ path: '/about' }], fullPath: '/about' }) // real navigation

    expect(onPageView).toHaveBeenCalledTimes(2)
    expect(onPageView.mock.calls[1]![0].path).toBe('/about')
  })

  it('does not double-count a query-only change on the same matched route', () => {
    const onPageView = vi.fn()
    const tracker = new PageViewTracker(
      { hashRouting: false, queryAllowlist: [], excludeRoutes: [] },
      onPageView,
    )
    tracker.trackInitial(new URL('https://example.com/products'))

    const { router, fire } = fakeRouter()
    tracker.trackRouter(router)
    fire({ matched: [{ path: '/products' }], fullPath: '/products' }) // suppressed double-fire
    fire({ matched: [{ path: '/products' }], fullPath: '/products?sort=price' }) // same component

    expect(onPageView).toHaveBeenCalledTimes(1)
  })

  it('drops a page view whose canonical path matches an exclude pattern', () => {
    const onPageView = vi.fn()
    const tracker = new PageViewTracker(
      { hashRouting: false, queryAllowlist: [], excludeRoutes: ['/admin/**'] },
      onPageView,
    )

    tracker.trackInitial(new URL('https://example.com/admin/settings'))

    expect(onPageView).not.toHaveBeenCalled()
  })
})
