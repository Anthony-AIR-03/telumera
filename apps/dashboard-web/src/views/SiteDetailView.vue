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

// --- Install snippet (M01.8) ---
const collectorOrigin = import.meta.env.VITE_COLLECTOR_ORIGIN ?? 'http://localhost:5103'
const snippetFlavor = ref<'script' | 'esm'>('script')
const activeToken = computed(() => tokens.value.find((tok) => !tok.revokedAt)?.token ?? null)

// Built from parts so the closing script tag is never a literal token in this SFC's script block.
const closeScript = '<' + '/script>'

const snippet = computed(() => {
  const tok = activeToken.value ?? 'YOUR_SITE_TOKEN'
  if (snippetFlavor.value === 'esm') {
    return `import { init } from '@telumera/browser-sdk'

const analytics = init({
  siteToken: '${tok}',
  endpoint: '${collectorOrigin}/v1/events',
})
// SPA only: analytics.trackRouter(router)`
  }
  return `<script src="${collectorOrigin}/telumera.js">${closeScript}
<script>
  const analytics = window.telumera.init({
    siteToken: '${tok}',
    endpoint: '${collectorOrigin}/v1/events',
  })
  // SPA only: analytics.trackRouter(router)
${closeScript}`
})

const copied = ref(false)
let copiedTimer: ReturnType<typeof setTimeout> | undefined
async function copySnippet() {
  try {
    await navigator.clipboard.writeText(snippet.value)
    copied.value = true
    clearTimeout(copiedTimer)
    copiedTimer = setTimeout(() => (copied.value = false), 1400)
  } catch {
    /* clipboard blocked — the user can still select the text */
  }
}
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
        <h2 class="font-display text-sm font-bold text-neutral-900">{{ t('install.title') }}</h2>
        <p class="mt-1 text-sm text-neutral-500">{{ t('install.blurb') }}</p>

        <div
          class="mt-4 rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
        >
          <div class="flex items-center gap-2">
            <button
              v-for="flavor in ['script', 'esm'] as const"
              :key="flavor"
              type="button"
              class="rounded-[9px] px-2.5 py-1 text-xs font-semibold"
              :class="
                snippetFlavor === flavor
                  ? 'bg-brand-50 text-brand-700'
                  : 'text-neutral-500 hover:text-neutral-700'
              "
              @click="snippetFlavor = flavor"
            >
              {{ t(`install.flavor.${flavor}`) }}
            </button>
            <AppButton class="ml-auto" variant="secondary" @click="copySnippet">
              {{ copied ? t('install.copied') : t('install.copy') }}
            </AppButton>
          </div>
          <pre
            class="mt-3 overflow-x-auto rounded-[9px] bg-neutral-100 p-3 font-mono text-xs text-neutral-700"
          ><code>{{ snippet }}</code></pre>
          <p v-if="!activeToken" class="mt-2 text-xs text-neutral-400">
            {{ t('install.noToken') }}
          </p>
        </div>
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
