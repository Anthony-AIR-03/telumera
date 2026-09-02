import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/LoginView.vue'),
      meta: { requiresAuth: false, layout: 'bare' },
    },
    {
      path: '/',
      name: 'dashboard',
      component: () => import('../views/DashboardView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/workspaces/:id',
      name: 'workspace-detail',
      component: () => import('../views/WorkspaceDetailView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/analytics',
      name: 'analytics-sites',
      component: () => import('../views/AnalyticsSitesView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/sites/:id',
      name: 'site-detail',
      component: () => import('../views/SiteDetailView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/sites/:id/analytics',
      name: 'site-analytics',
      component: () => import('../views/SiteAnalyticsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/sites/:id/analytics/quality',
      name: 'site-analytics-quality',
      component: () => import('../views/SiteQualityView.vue'),
      meta: { requiresAuth: true },
    },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.restore()

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  return true
})

export default router
