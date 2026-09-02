<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import { roleAtLeast } from '@/lib/roles'
import AppButton from '@/components/AppButton.vue'
import AppBadge from '@/components/AppBadge.vue'
import AppToggle from '@/components/AppToggle.vue'
import StateMessage from '@/components/StateMessage.vue'

interface Site {
  id: string
  workspaceId: string
  name: string
  canonicalDomain: string
  environment: string
  createdAt: string
  role: string
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

/** Rotate/revoke/toggle all require Developer+ server-side (see services/site-registry/README.md). */
const canManage = computed(() => roleAtLeast(site.value?.role ?? null, 'Developer'))
</script>

<template>
  <main>
    <RouterLink
      v-if="site"
      :to="`/workspaces/${site.workspaceId}`"
      class="text-xs font-semibold text-neutral-400 hover:text-neutral-700"
    >
      {{ t('common.backToWorkspace') }}
    </RouterLink>
    <div class="mt-2 flex items-center justify-between">
      <h1 class="font-display text-xl font-bold text-neutral-900">
        {{ site?.name ?? siteId }}
      </h1>
      <RouterLink v-if="site" :to="`/sites/${siteId}/analytics`">
        <AppButton variant="secondary">{{ t('sites.viewAnalytics') }}</AppButton>
      </RouterLink>
    </div>

    <StateMessage v-if="error" state="error" :message="error" class="mt-6" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-6" />

    <template v-else>
      <section class="mt-8">
        <h2 class="font-display text-sm font-bold text-neutral-900">{{ t('tokens.title') }}</h2>
        <AppButton class="mt-3" :disabled="!canManage" :loading="busy" @click="rotateToken">{{
          t('tokens.rotate')
        }}</AppButton>

        <ul
          class="mt-4 divide-y divide-neutral-200 rounded-[14px] border border-neutral-200 bg-white shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
        >
          <li v-for="token in tokens" :key="token.id" class="flex items-center gap-3 px-5 py-3.5">
            <code
              class="flex-1 overflow-hidden text-ellipsis whitespace-nowrap rounded bg-neutral-100 px-2 py-1 font-mono text-xs text-neutral-700"
            >
              {{ token.token }}
            </code>
            <AppBadge v-if="token.revokedAt" tone="danger">{{ t('tokens.revoked') }}</AppBadge>
            <AppButton
              v-else
              variant="danger"
              :disabled="!canManage"
              :loading="busy"
              @click="revokeToken(token.id)"
            >
              {{ t('tokens.revoke') }}
            </AppButton>
          </li>
        </ul>
      </section>

      <section class="mt-8">
        <h2 class="font-display text-sm font-bold text-neutral-900">
          {{ t('modules.title') }}
        </h2>
        <ul
          class="mt-4 divide-y divide-neutral-200 rounded-[14px] border border-neutral-200 bg-white shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
        >
          <li
            v-for="module in modules"
            :key="module.module"
            class="flex items-center justify-between px-5 py-3.5"
          >
            <span class="text-sm text-neutral-900">{{ module.module }}</span>
            <AppToggle
              :model-value="module.enabled"
              :disabled="!canManage || busy"
              class="text-sm text-neutral-600"
              @update:model-value="toggleModule(module)"
            >
              {{ module.enabled ? t('modules.enabled') : t('modules.disabled') }}
            </AppToggle>
          </li>
        </ul>
      </section>
    </template>
  </main>
</template>
