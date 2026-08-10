<script setup lang="ts">
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()

function logout() {
  auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <header>
    <RouterLink to="/" class="brand">{{ t('app.name') }}</RouterLink>
    <nav v-if="auth.isAuthenticated">
      <RouterLink to="/">{{ t('nav.dashboard') }}</RouterLink>
      <button type="button" @click="logout">{{ t('nav.logout') }}</button>
    </nav>
  </header>

  <RouterView />
</template>

<style scoped>
header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.5rem;
  border-bottom: 1px solid var(--color-border);
}

.brand {
  font-weight: 600;
  font-size: 1.1rem;
}

nav {
  display: flex;
  align-items: center;
  gap: 1rem;
}

nav button {
  font: inherit;
  color: inherit;
  background: none;
  border: none;
  cursor: pointer;
}
</style>
