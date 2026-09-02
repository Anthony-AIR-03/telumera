<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppBadge from '@/components/AppBadge.vue'
import StateMessage from '@/components/StateMessage.vue'

interface WorkspaceSummary {
  id: string
  name: string
  createdAt: string
  role: string
}

interface SiteSummary {
  id: string
  name: string
  canonicalDomain: string
  environment: string
  createdAt: string
}

interface WorkspaceGroup {
  workspace: WorkspaceSummary
  sites: SiteSummary[]
}

const { t } = useI18n()
const groups = ref<WorkspaceGroup[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

/**
 * No dedicated "all my sites" backend endpoint exists — every workspace membership already
 * qualifies for Viewer-level analytics access (matches every M01.6 endpoint's own threshold), so
 * this fans out client-side: GET /workspaces, then each workspace's GET /workspaces/{id}/sites in
 * parallel. Workspaces with no sites are dropped — they have nothing to show on a page whose whole
 * job is "sites you can view analytics for."
 */
async function loadAll() {
  loading.value = true
  error.value = null
  try {
    const workspaces = await apiClient.get<WorkspaceSummary[]>('/workspaces')
    const results = await Promise.all(
      workspaces.map(async (workspace) => ({
        workspace,
        sites: await apiClient.get<SiteSummary[]>(`/workspaces/${workspace.id}/sites`),
      })),
    )
    groups.value = results.filter((group) => group.sites.length > 0)
  } catch {
    error.value = t('analyticsSites.loadError')
  } finally {
    loading.value = false
  }
}

onMounted(loadAll)
</script>

<template>
  <main>
    <h1 class="font-display text-xl font-bold text-neutral-900">{{ t('analyticsSites.title') }}</h1>

    <StateMessage v-if="error" state="error" :message="error" class="mt-6" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-6" />
    <StateMessage
      v-else-if="groups.length === 0"
      state="empty"
      :message="t('analyticsSites.empty')"
      class="mt-6"
    />

    <template v-else>
      <section v-for="group in groups" :key="group.workspace.id" class="mt-8 first:mt-6">
        <h2 class="font-display text-sm font-bold text-neutral-900">{{ group.workspace.name }}</h2>
        <ul
          class="mt-3 divide-y divide-neutral-200 rounded-[14px] border border-neutral-200 bg-white shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
        >
          <li
            v-for="site in group.sites"
            :key="site.id"
            class="flex items-center justify-between px-5 py-3.5"
          >
            <RouterLink
              :to="`/sites/${site.id}/analytics`"
              class="text-sm font-semibold text-brand-700 hover:text-brand-800"
            >
              {{ site.name }}
            </RouterLink>
            <AppBadge>{{ site.canonicalDomain }}</AppBadge>
          </li>
        </ul>
      </section>
    </template>
  </main>
</template>
