<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import { roleAtLeast } from '@/lib/roles'
import AppButton from '@/components/AppButton.vue'
import AppBadge from '@/components/AppBadge.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'

interface Workspace {
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

interface Member {
  entraObjectId: string
  displayName: string | null
  email: string | null
  role: string
}

const ROLES = ['Viewer', 'Developer', 'Admin', 'Owner']

const route = useRoute()
const { t } = useI18n()
const workspaceId = route.params.id as string

const workspace = ref<Workspace | null>(null)
const sites = ref<SiteSummary[]>([])
const members = ref<Member[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

const newSiteName = ref('')
const newSiteDomain = ref('')
const creatingSite = ref(false)

const newMemberObjectId = ref('')
const newMemberRole = ref('Viewer')
const addingMember = ref(false)

async function loadAll() {
  loading.value = true
  error.value = null
  try {
    const [workspaceResult, sitesResult, membersResult] = await Promise.all([
      apiClient.get<Workspace>(`/workspaces/${workspaceId}`),
      apiClient.get<SiteSummary[]>(`/workspaces/${workspaceId}/sites`),
      apiClient.get<Member[]>(`/workspaces/${workspaceId}/members`),
    ])
    workspace.value = workspaceResult
    sites.value = sitesResult
    members.value = membersResult
  } catch {
    error.value = t('workspaces.loadError')
  } finally {
    loading.value = false
  }
}

async function createSite() {
  if (!newSiteName.value.trim() || !newSiteDomain.value.trim()) {
    return
  }

  creatingSite.value = true
  error.value = null
  try {
    await apiClient.post('/sites', {
      WorkspaceId: workspaceId,
      Name: newSiteName.value.trim(),
      CanonicalDomain: newSiteDomain.value.trim(),
      AllowedOrigins: [`https://${newSiteDomain.value.trim()}`],
      Environment: 'production',
    })
    newSiteName.value = ''
    newSiteDomain.value = ''
    await loadAll()
  } catch {
    error.value = t('sites.createError')
  } finally {
    creatingSite.value = false
  }
}

async function addMember() {
  if (!newMemberObjectId.value.trim()) {
    return
  }

  addingMember.value = true
  error.value = null
  try {
    await apiClient.post(`/workspaces/${workspaceId}/members`, {
      EntraObjectId: newMemberObjectId.value.trim(),
      Role: newMemberRole.value,
    })
    newMemberObjectId.value = ''
    await loadAll()
  } catch {
    error.value = t('members.addError')
  } finally {
    addingMember.value = false
  }
}

onMounted(loadAll)
</script>

<template>
  <div>
    <PageHeader
      :title="workspace?.name ?? workspaceId"
      :crumbs="[{ label: t('common.workspaces'), to: '/' }, { label: workspace?.name ?? '…' }]"
    >
      <template v-if="workspace" #title-after>
        <AppBadge>{{ workspace.role }}</AppBadge>
      </template>
    </PageHeader>

    <StateMessage v-if="error" state="error" :message="error" class="mt-6" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-6" />

    <template v-else>
      <section class="mt-8">
        <h2 class="font-display text-lg font-medium">{{ t('sites.title') }}</h2>
        <form
          v-if="roleAtLeast(workspace?.role ?? null, 'Developer')"
          class="mt-3 flex flex-wrap gap-2"
          @submit.prevent="createSite"
        >
          <input
            v-model="newSiteName"
            :placeholder="t('sites.namePlaceholder')"
            :aria-label="t('sites.namePlaceholder')"
            required
            class="field min-w-0 flex-1"
          />
          <input
            v-model="newSiteDomain"
            :placeholder="t('sites.domainPlaceholder')"
            :aria-label="t('sites.domainPlaceholder')"
            required
            class="field min-w-0 flex-1"
          />
          <AppButton type="submit" :loading="creatingSite">{{ t('sites.create') }}</AppButton>
        </form>

        <StateMessage
          v-if="sites.length === 0"
          state="empty"
          :message="t('sites.empty')"
          class="mt-4"
        />
        <ul v-else class="card mt-4 divide-y divide-line-2">
          <li
            v-for="site in sites"
            :key="site.id"
            class="flex items-center justify-between gap-3 px-5 py-3.5"
          >
            <RouterLink
              :to="`/sites/${site.id}`"
              class="text-sm font-medium text-fg hover:text-signal-text hover:underline"
            >
              {{ site.name }}
            </RouterLink>
            <AppBadge>{{ site.canonicalDomain }}</AppBadge>
          </li>
        </ul>
      </section>

      <section class="mt-8">
        <h2 class="font-display text-lg font-medium">{{ t('members.title') }}</h2>
        <form
          v-if="roleAtLeast(workspace?.role ?? null, 'Admin')"
          class="mt-3 flex flex-wrap gap-2"
          @submit.prevent="addMember"
        >
          <input
            v-model="newMemberObjectId"
            :placeholder="t('members.objectIdPlaceholder')"
            :aria-label="t('members.objectIdPlaceholder')"
            required
            class="field min-w-0 flex-1 font-mono"
          />
          <select v-model="newMemberRole" :aria-label="t('members.role')" class="field">
            <option v-for="role in ROLES" :key="role" :value="role">{{ role }}</option>
          </select>
          <AppButton type="submit" :loading="addingMember">{{ t('members.add') }}</AppButton>
        </form>

        <ul class="card mt-4 divide-y divide-line-2">
          <li
            v-for="member in members"
            :key="member.entraObjectId"
            class="flex items-center justify-between gap-3 px-5 py-3.5"
          >
            <span class="min-w-0 truncate text-sm">{{
              member.displayName ?? member.entraObjectId
            }}</span>
            <AppBadge>{{ member.role }}</AppBadge>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>
