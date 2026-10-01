<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import AppButton from '@/components/AppButton.vue'
import AppNavLinks from '@/components/AppNavLinks.vue'

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

function logout() {
  auth.logout()
  router.push({ name: 'login' })
}

function logoutFromMobileNav() {
  mobileNavOpen.value = false
  logout()
}
</script>

<template>
  <RouterView v-if="route.meta.layout === 'bare'" />

  <div v-else class="min-h-screen lg:grid lg:grid-cols-[248px_1fr]">
    <header
      class="flex items-center justify-between border-b border-neutral-200 bg-white px-4 py-3 lg:hidden"
    >
      <RouterLink to="/" class="flex items-center gap-2.5">
        <span
          class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-brand-500 to-brand-800 font-display text-sm font-bold text-white"
        >
          T
        </span>
        <span class="font-display text-base font-bold tracking-tight">{{ t('app.name') }}</span>
      </RouterLink>

      <button
        v-if="auth.isAuthenticated"
        type="button"
        class="-m-2.5 flex h-11 w-11 items-center justify-center rounded-lg text-neutral-600 hover:bg-neutral-50"
        :aria-label="t('nav.openMenu')"
        aria-haspopup="dialog"
        @click="mobileNavOpen = true"
      >
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" aria-hidden="true">
          <path
            d="M3 5.5h14M3 10h14M3 14.5h14"
            stroke="currentColor"
            stroke-width="1.6"
            stroke-linecap="round"
          />
        </svg>
      </button>
    </header>

    <aside class="hidden flex-col gap-6 border-r border-neutral-200 bg-white px-4 py-5 lg:flex">
      <RouterLink to="/" class="flex items-center gap-2.5 px-2 py-1">
        <span
          class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-brand-500 to-brand-800 font-display text-sm font-bold text-white"
        >
          T
        </span>
        <span class="font-display text-base font-bold tracking-tight">{{ t('app.name') }}</span>
      </RouterLink>

      <AppNavLinks v-if="auth.isAuthenticated" />

      <AppButton v-if="auth.isAuthenticated" variant="secondary" class="mt-auto" @click="logout">
        {{ t('nav.logout') }}
      </AppButton>
    </aside>

    <!--
      Mirrors GlossaryDrawer.vue's documented gotcha, mirrored for a LEFT-docked panel: a modal
      <dialog>'s UA stylesheet sets `inset: 0`. Here `left-0` already matches that default, so
      `right-auto` is what clears the opposite edge and lets `max-w` cap the panel's width.
    -->
    <dialog
      ref="mobileNavRef"
      class="fixed top-0 left-0 right-auto m-0 h-screen max-h-none w-full max-w-[280px] border-0 bg-white p-0 shadow-[8px_0_32px_rgba(15,21,18,0.16)] backdrop:bg-neutral-900/30 lg:hidden"
      @close="mobileNavOpen = false"
      @cancel="mobileNavOpen = false"
    >
      <div class="flex items-center justify-between border-b border-neutral-200 px-5 py-4">
        <span class="font-display text-base font-bold tracking-tight">{{ t('app.name') }}</span>
        <button
          type="button"
          class="-m-2.5 flex h-11 w-11 items-center justify-center rounded-lg text-neutral-600 hover:bg-neutral-100"
          :aria-label="t('nav.closeMenu')"
          @click="mobileNavRef?.close()"
        >
          <svg width="16" height="16" viewBox="0 0 20 20" fill="none" aria-hidden="true">
            <path
              d="M5 5l10 10M15 5L5 15"
              stroke="currentColor"
              stroke-width="1.8"
              stroke-linecap="round"
            />
          </svg>
        </button>
      </div>

      <div class="flex flex-col gap-6 px-4 py-5">
        <AppNavLinks @navigate="mobileNavOpen = false" />
        <AppButton variant="secondary" @click="logoutFromMobileNav">
          {{ t('nav.logout') }}
        </AppButton>
      </div>
    </dialog>

    <main class="min-w-0">
      <div class="px-4 pt-6 pb-16 sm:px-6 lg:px-8 lg:pt-7">
        <RouterView />
      </div>
    </main>
  </div>
</template>
