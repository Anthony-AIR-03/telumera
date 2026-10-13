<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import AppNavLinks from '@/components/AppNavLinks.vue'
import BrandLockup from '@/components/BrandLockup.vue'
import ThemeToggle from '@/components/ThemeToggle.vue'

const { t } = useI18n()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const mobileNavOpen = ref(false)
const mobileNavRef = ref<HTMLDialogElement | null>(null)

/** Same native <dialog> pattern as GlossaryDrawer.vue: Esc-to-close, a backdrop, and focus
 *  trapping for free, no hand-rolled modal logic. */
watch(mobileNavOpen, (isOpen) => {
  if (isOpen) {
    mobileNavRef.value?.showModal()
  } else {
    mobileNavRef.value?.close()
  }
})

const userName = computed(() => auth.account?.name || auth.account?.username || '')
const userInitial = computed(() => userName.value.trim().charAt(0).toUpperCase() || '·')

function logout() {
  mobileNavOpen.value = false
  auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <RouterView v-if="route.meta.layout === 'bare'" />

  <div v-else class="min-h-screen lg:grid lg:grid-cols-[248px_minmax(0,1fr)]">
    <!-- Under lg the Graphite rail collapses into a Graphite top bar with a menu drawer. -->
    <header
      class="sticky top-0 z-20 flex items-center justify-between bg-rail px-4 py-3 text-rail-fg lg:hidden"
    >
      <RouterLink to="/" :aria-label="t('app.name')">
        <BrandLockup class="h-5" />
      </RouterLink>
      <div class="flex items-center gap-1">
        <ThemeToggle class="text-rail-muted hover:bg-rail-hover hover:text-rail-fg" />
        <button
          v-if="auth.isAuthenticated"
          type="button"
          class="-mr-2 grid h-11 w-11 place-items-center rounded-[3px] text-rail-muted hover:bg-rail-hover hover:text-rail-fg"
          :aria-label="t('nav.openMenu')"
          aria-haspopup="dialog"
          @click="mobileNavOpen = true"
        >
          <svg
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
            aria-hidden="true"
          >
            <path d="M4 7H20M4 12H20M4 17H20" />
          </svg>
        </button>
      </div>
    </header>

    <aside
      class="sticky top-0 hidden h-screen flex-col gap-[22px] overflow-y-auto bg-rail px-3.5 py-5 text-rail-fg lg:flex"
    >
      <RouterLink to="/" class="block self-start px-2 pt-1 pb-1.5" :aria-label="t('app.name')">
        <BrandLockup class="h-[22px]" />
      </RouterLink>

      <AppNavLinks v-if="auth.isAuthenticated" />

      <div
        v-if="auth.isAuthenticated"
        class="mt-auto flex items-center gap-2.5 border-t border-rail-line px-1 pt-3"
      >
        <span
          class="grid h-7 w-7 flex-none place-items-center rounded-[3px] bg-[#242D32] font-display text-xs font-medium"
          aria-hidden="true"
          >{{ userInitial }}</span
        >
        <div class="min-w-0 flex-1">
          <div class="truncate text-sm font-medium">{{ userName }}</div>
          <button
            type="button"
            class="font-mono text-xs text-rail-muted hover:text-rail-fg"
            @click="logout"
          >
            {{ t('nav.logout') }}
          </button>
        </div>
        <ThemeToggle class="text-rail-muted hover:bg-rail-hover hover:text-rail-fg" />
      </div>
    </aside>

    <!--
      Mirrors GlossaryDrawer.vue's documented gotcha, mirrored for a LEFT-docked panel: a modal
      <dialog>'s UA stylesheet sets `inset: 0`. Here `left-0` already matches that default, so
      `right-auto` is what clears the opposite edge and lets `max-w` cap the panel's width.
    -->
    <dialog
      ref="mobileNavRef"
      class="fixed top-0 right-auto left-0 m-0 h-screen max-h-none w-full max-w-[280px] border-0 bg-rail p-0 text-rail-fg backdrop:bg-graphite/50 lg:hidden"
      @close="mobileNavOpen = false"
      @cancel="mobileNavOpen = false"
    >
      <div class="flex h-full flex-col">
        <div class="flex items-center justify-between border-b border-rail-line px-5 py-4">
          <BrandLockup class="h-5" />
          <button
            type="button"
            class="-mr-2.5 grid h-11 w-11 place-items-center rounded-[3px] text-rail-muted hover:bg-rail-hover hover:text-rail-fg"
            :aria-label="t('nav.closeMenu')"
            @click="mobileNavRef?.close()"
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.5"
              aria-hidden="true"
            >
              <path d="M5 5L19 19M19 5L5 19" />
            </svg>
          </button>
        </div>

        <div class="flex-1 overflow-y-auto px-3.5 py-5">
          <AppNavLinks @navigate="mobileNavOpen = false" />
        </div>

        <div class="flex items-center gap-2.5 border-t border-rail-line px-5 py-3">
          <div class="min-w-0 flex-1 truncate text-sm font-medium">{{ userName }}</div>
          <button
            type="button"
            class="font-mono text-xs text-rail-muted hover:text-rail-fg"
            @click="logout"
          >
            {{ t('nav.logout') }}
          </button>
        </div>
      </div>
    </dialog>

    <main class="min-w-0">
      <div class="px-4 pt-5 pb-[72px] sm:px-6 lg:px-9 lg:pt-7 lg:pb-16">
        <RouterView />
      </div>
    </main>
  </div>
</template>
