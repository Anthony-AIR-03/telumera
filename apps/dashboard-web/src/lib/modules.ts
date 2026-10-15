/**
 * The module roadmap as the dashboard shows it, with each module's brand icon. Paths are copied from
 * docs/brand/kit/icons.js (the source of truth; README §6): a 24-unit grid, 1.5 stroke, miter
 * joins, butt ends, and exactly one accent path `a` that turns Signal when the module is active.
 *
 * `route` is set only for modules that have shipped UI. Planned modules stay visible (dimmed, tagged
 * "Planned") so the platform's direction is part of the navigation, per the brand direction.
 */
export interface PlatformModule {
  id: string
  name: string
  path: string
  accent: string
  route?: string
}

export const MODULES: PlatformModule[] = [
  {
    id: 'M01',
    name: 'Product Analytics',
    path: 'M4 20H20M7 20V14M11 20V9M15 20V12',
    accent: 'M19 20V5',
    route: '/analytics',
  },
  { id: 'M02', name: 'Performance', path: 'M4 17a8 8 0 1 1 16 0', accent: 'M12 17L16.5 10.5' },
  {
    id: 'M03',
    name: 'Error Tracking',
    path: 'M8 5H5V19H8M16 5H19V19H16',
    accent: 'M12 7.5V13.5M12 15.5V17',
  },
  {
    id: 'M04',
    name: 'Deployments',
    path: 'M4 15L12 19L20 15M4 11L12 15L20 11',
    accent: 'M12 3V11M9 6L12 3L15 6',
  },
  { id: 'M05', name: 'Goals & Funnels', path: 'M4 6H20M7 12H17', accent: 'M10 18H14' },
  { id: 'M06', name: 'SEO & Content', path: 'M6 3H14L18 7V21H6ZM9 11H15', accent: 'M9 15H15' },
  { id: 'M07', name: 'Heatmaps', path: 'M4 4H20V20H4ZM8 8H16V16H8Z', accent: 'M11 11H13V13H11Z' },
  {
    id: 'M08',
    name: 'AI Insights',
    path: 'M4 20H8M4 4H8M16 4H20M16 20H20',
    accent: 'M12 5L13.6 10.4L19 12L13.6 13.6L12 19L10.4 13.6L5 12L10.4 10.4Z',
  },
  {
    id: 'M09',
    name: 'Alerts & Uptime',
    path: 'M3 14H7L9 9L12 18L14 12L15 14H21',
    accent: 'M3 6H5M8 6H10M13 6H15M18 6H21',
  },
  {
    id: 'M10',
    name: 'Teams & Sites',
    path: 'M4 4H10V10H4ZM14 14H20V20H14Z',
    accent: 'M7 10V17H14',
  },
  {
    id: 'M11',
    name: 'Edge Agent',
    path: 'M7 7H17V17H7ZM10 3V7M14 3V7M10 17V21M14 17V21M3 10H7M3 14H7M17 10H21M17 14H21',
    accent: 'M10 10H14V14H10Z',
  },
]

/** The next modules a site will be able to turn on — shown in the "Coming to this site" card. */
export const NEXT_MODULE_IDS = ['M02', 'M03', 'M04', 'M09']
