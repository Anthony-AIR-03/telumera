<script setup lang="ts">
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import AppButton from '@/components/AppButton.vue'

const { t } = useI18n()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

function logout() {
  auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <RouterView v-if="route.meta.layout === 'bare'" />

  <div v-else class="grid min-h-screen grid-cols-[248px_1fr]">
    <aside class="flex flex-col gap-6 border-r border-neutral-200 bg-white px-4 py-5">
      <RouterLink to="/" class="flex items-center gap-2.5 px-2 py-1">
        <span
          class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-brand-500 to-brand-800 font-display text-sm font-bold text-white"
        >
          T
        </span>
        <span class="font-display text-base font-bold tracking-tight">{{ t('app.name') }}</span>
      </RouterLink>

      <nav v-if="auth.isAuthenticated" class="flex flex-col gap-0.5">
        <RouterLink
          to="/"
          class="flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-semibold text-neutral-600 hover:bg-neutral-50 hover:text-neutral-900"
          active-class="bg-brand-50 text-brand-700 hover:bg-brand-50 hover:text-brand-700"
        >
          <svg class="h-[18px] w-[18px] flex-shrink-0" viewBox="0 0 20 20" fill="none">
            <rect
              x="3"
              y="3"
              width="6"
              height="6"
              rx="1.5"
              stroke="currentColor"
              stroke-width="1.6"
            />
            <rect
              x="11"
              y="3"
              width="6"
              height="6"
              rx="1.5"
              stroke="currentColor"
              stroke-width="1.6"
            />
            <rect
              x="3"
              y="11"
              width="6"
              height="6"
              rx="1.5"
              stroke="currentColor"
              stroke-width="1.6"
            />
            <rect
              x="11"
              y="11"
              width="6"
              height="6"
              rx="1.5"
              stroke="currentColor"
              stroke-width="1.6"
            />
          </svg>
          {{ t('nav.dashboard') }}
        </RouterLink>
      </nav>

      <AppButton v-if="auth.isAuthenticated" variant="secondary" class="mt-auto" @click="logout">
        {{ t('nav.logout') }}
      </AppButton>
    </aside>

    <main class="min-w-0">
      <div class="px-8 pt-7 pb-16">
        <RouterView />
      </div>
    </main>
  </div>
</template>
