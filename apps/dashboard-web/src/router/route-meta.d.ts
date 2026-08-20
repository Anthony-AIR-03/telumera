export {}

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth: boolean
    /** 'bare' renders full-bleed with no sidebar/header chrome — only the login route uses this. */
    layout?: 'bare'
  }
}
