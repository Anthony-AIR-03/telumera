<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'

interface Site {
  id: string
  workspaceId: string
  name: string
  canonicalDomain: string
  environment: string
  createdAt: string
}

interface SiteToken {
  id: string
  token: string
  createdAt: string
  revokedAt: string | null
}

interface ModuleSetting {
  module: string
  enabled: boolean
  updatedAt: string
}

const route = useRoute()
const { t } = useI18n()
const siteId = route.params.id as string

const site = ref<Site | null>(null)
const tokens = ref<SiteToken[]>([])
const modules = ref<ModuleSetting[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const busy = ref(false)

async function loadAll() {
  loading.value = true
  error.value = null
  try {
    const [siteResult, tokensResult, modulesResult] = await Promise.all([
      apiClient.get<Site>(`/sites/${siteId}`),
      apiClient.get<SiteToken[]>(`/sites/${siteId}/tokens`),
      apiClient.get<ModuleSetting[]>(`/sites/${siteId}/modules`),
    ])
    site.value = siteResult
    tokens.value = tokensResult
    modules.value = modulesResult
  } catch {
    error.value = t('sites.loadError')
  } finally {
    loading.value = false
  }
}

async function rotateToken() {
  busy.value = true
  error.value = null
  try {
    await apiClient.post(`/sites/${siteId}/tokens/rotate`)
    await loadAll()
  } catch {
    error.value = t('tokens.rotateError')
  } finally {
    busy.value = false
  }
}

async function revokeToken(tokenId: string) {
  busy.value = true
  error.value = null
  try {
    await apiClient.post(`/sites/${siteId}/tokens/${tokenId}/revoke`)
    await loadAll()
  } catch {
    error.value = t('tokens.revokeError')
  } finally {
    busy.value = false
  }
}

async function toggleModule(module: ModuleSetting) {
  busy.value = true
  error.value = null
  try {
    await apiClient.patch(`/sites/${siteId}/modules/${module.module}`, { Enabled: !module.enabled })
    await loadAll()
  } catch {
    error.value = t('modules.toggleError')
  } finally {
    busy.value = false
  }
}

onMounted(loadAll)
</script>

<template>
  <main>
    <RouterLink v-if="site" :to="`/workspaces/${site.workspaceId}`">{{ t('common.backToWorkspace') }}</RouterLink>
    <h1>{{ site?.name ?? siteId }}</h1>

    <p v-if="error" role="alert">{{ error }}</p>
    <p v-else-if="loading">{{ t('common.loading') }}</p>

    <template v-else>
      <section>
        <h2>{{ t('tokens.title') }}</h2>
        <button type="button" :disabled="busy" @click="rotateToken">{{ t('tokens.rotate') }}</button>

        <ul class="list">
          <li v-for="token in tokens" :key="token.id">
            <code>{{ token.token }}</code>
            <span v-if="token.revokedAt" class="badge">{{ t('tokens.revoked') }}</span>
            <button v-else type="button" :disabled="busy" @click="revokeToken(token.id)">
              {{ t('tokens.revoke') }}
            </button>
          </li>
        </ul>
      </section>

      <section>
        <h2>{{ t('modules.title') }}</h2>
        <ul class="list">
          <li v-for="module in modules" :key="module.module">
            <span>{{ module.module }}</span>
            <label class="toggle">
              <input
                type="checkbox"
                :checked="module.enabled"
                :disabled="busy"
                @change="toggleModule(module)"
              />
              {{ module.enabled ? t('modules.enabled') : t('modules.disabled') }}
            </label>
          </li>
        </ul>
      </section>
    </template>
  </main>
</template>

<style scoped>
section {
  margin-top: 2rem;
}

.list {
  list-style: none;
  margin-top: 1rem;
}

.list li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--color-border);
}

.list li code {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}

.badge {
  font-size: 0.8rem;
  opacity: 0.65;
}

.toggle {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
}
</style>
