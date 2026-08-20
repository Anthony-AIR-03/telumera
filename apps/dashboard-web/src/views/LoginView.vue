<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import AppButton from '@/components/AppButton.vue'
import StateMessage from '@/components/StateMessage.vue'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const error = ref<string | null>(null)
const signingIn = ref(false)

async function signIn() {
  error.value = null
  signingIn.value = true
  try {
    await auth.login()
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.push(redirect)
  } catch {
    error.value = t('login.error')
  } finally {
    signingIn.value = false
  }
}
</script>

<template>
  <main
    class="grid min-h-screen place-items-center p-6"
    style="
      background:
        radial-gradient(560px 360px at 15% 10%, var(--color-brand-50), transparent 60%),
        var(--color-neutral-50);
    "
  >
    <div
      class="w-full max-w-sm rounded-[14px] border border-neutral-200 bg-white p-8 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
    >
      <div class="mb-6 flex items-center gap-2.5">
        <span
          class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-brand-500 to-brand-800 font-display text-sm font-bold text-white"
        >
          T
        </span>
        <span class="font-display text-base font-bold tracking-tight">{{ t('app.name') }}</span>
      </div>
      <h1 class="font-display text-xl font-bold text-neutral-900">{{ t('login.title') }}</h1>
      <p class="mt-2 text-sm text-neutral-600">{{ t('login.description') }}</p>
      <StateMessage v-if="error" state="error" :message="error" class="mt-4" />
      <AppButton
        class="mt-6 w-full"
        :loading="signingIn"
        :loading-label="t('login.signingIn')"
        @click="signIn"
      >
        {{ t('login.action') }}
      </AppButton>
    </div>
  </main>
</template>
