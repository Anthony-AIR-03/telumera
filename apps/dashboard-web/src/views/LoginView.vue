<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

function signIn() {
  // Placeholder token until the identity-workspace service issues real ones.
  auth.login('placeholder-token')
  const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
  router.push(redirect)
}
</script>

<template>
  <main>
    <h1>{{ t('login.title') }}</h1>
    <p>{{ t('login.description') }}</p>
    <button type="button" @click="signIn">{{ t('login.action') }}</button>
  </main>
</template>
