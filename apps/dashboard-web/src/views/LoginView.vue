<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

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
  <main>
    <h1>{{ t('login.title') }}</h1>
    <p>{{ t('login.description') }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <button type="button" :disabled="signingIn" @click="signIn">
      {{ signingIn ? t('login.signingIn') : t('login.action') }}
    </button>
  </main>
</template>
