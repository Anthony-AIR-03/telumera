<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { BrowserAuthError } from '@azure/msal-browser'
import { useAuthStore } from '@/stores/auth'
import AppButton from '@/components/AppButton.vue'
import BrandLockup from '@/components/BrandLockup.vue'
import StateMessage from '@/components/StateMessage.vue'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const error = ref<string | null>(null)
const signingIn = ref(false)
const signingOut = ref(false)

/** Closing the account picker without choosing isn't a failure worth an alarming message. */
function isUserCancelled(e: unknown): boolean {
  return e instanceof BrowserAuthError && e.errorCode === 'user_cancelled'
}

async function signIn() {
  error.value = null
  signingIn.value = true
  try {
    await auth.login()
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.push(redirect)
  } catch (e) {
    if (!isUserCancelled(e)) {
      error.value = t('login.error')
    }
  } finally {
    signingIn.value = false
  }
}

async function signOutOfMicrosoft() {
  error.value = null
  signingOut.value = true
  try {
    await auth.forgetAccount()
  } catch (e) {
    if (!isUserCancelled(e)) {
      error.value = t('login.signOutError')
    }
  } finally {
    signingOut.value = false
  }
}
</script>

<template>
  <main class="grid min-h-screen place-items-center bg-rail p-6 text-rail-fg">
    <div class="w-full max-w-sm">
      <BrandLockup class="mx-auto mb-8 h-7" />
      <div class="card p-8 text-fg">
        <h1 class="font-display text-[28px] leading-tight font-medium">{{ t('login.title') }}</h1>
        <p class="mt-2 text-sm text-fg-2">{{ t('login.description') }}</p>
        <StateMessage v-if="error" state="error" :message="error" class="mt-4" />
        <AppButton
          class="mt-6 w-full"
          :loading="signingIn"
          :loading-label="t('login.signingIn')"
          @click="signIn"
        >
          {{ t('login.action') }}
        </AppButton>
        <div v-if="error" class="mt-4 border-t border-line-2 pt-4">
          <p class="text-xs text-muted">{{ t('login.wrongAccountHint') }}</p>
          <AppButton
            variant="secondary"
            class="mt-2 w-full"
            :loading="signingOut"
            @click="signOutOfMicrosoft"
          >
            {{ t('login.signOutAction') }}
          </AppButton>
        </div>
      </div>
      <p class="mt-6 text-center font-mono text-xs text-rail-muted">{{ t('login.tagline') }}</p>
    </div>
  </main>
</template>
