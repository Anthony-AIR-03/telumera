<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import { roleAtLeast } from '@/lib/roles'
import AppButton from '@/components/AppButton.vue'
import AppBadge from '@/components/AppBadge.vue'
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
  <main>
    <RouterLink to="/" class="text-xs font-semibold text-neutral-400 hover:text-neutral-700">
      {{ t('common.backToWorkspaces') }}
    </RouterLink>
    <h1 class="font-display mt-2 text-xl font-bold text-neutral-900">
      {{ workspace?.name ?? workspaceId }}
    </h1>

    <StateMessage v-if="error" state="error" :message="error" class="mt-6" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-6" />

    <template v-else>
      <section class="mt-8">
        <h2 class="font-display text-sm font-bold text-neutral-900">{{ t('sites.title') }}</h2>
        <form
          v-if="roleAtLeast(workspace?.role ?? null, 'Developer')"
          class="mt-3 flex flex-wrap gap-2"
          @submit.prevent="createSite"
        >
          <input
            v-model="newSiteName"
            :placeholder="t('sites.namePlaceholder')"
            required
            class="flex-1 rounded-[9px] border border-neutral-300 px-3 py-2 text-sm placeholder-neutral-400 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          />
          <input
            v-model="newSiteDomain"
            :placeholder="t('sites.domainPlaceholder')"
            required
            class="flex-1 rounded-[9px] border border-neutral-300 px-3 py-2 text-sm placeholder-neutral-400 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          />
          <AppButton type="submit" :loading="creatingSite">{{ t('sites.create') }}</AppButton>
        </form>

        <StateMessage
          v-if="sites.length === 0"
          state="empty"
          :message="t('sites.empty')"
          class="mt-4"
        />
        <ul
          v-else
          class="mt-4 divide-y divide-neutral-200 rounded-[14px] border border-neutral-200 bg-white shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
        >
          <li
            v-for="site in sites"
            :key="site.id"
            class="flex items-center justify-between px-5 py-3.5"
          >
            <RouterLink
              :to="`/sites/${site.id}`"
              class="text-sm font-semibold text-brand-700 hover:text-brand-800"
            >
              {{ site.name }}
            </RouterLink>
            <AppBadge>{{ site.canonicalDomain }}</AppBadge>
          </li>
        </ul>
      </section>

      <section class="mt-8">
        <h2 class="font-display text-sm font-bold text-neutral-900">
          {{ t('members.title') }}
        </h2>
        <form
          v-if="roleAtLeast(workspace?.role ?? null, 'Admin')"
          class="mt-3 flex flex-wrap gap-2"
          @submit.prevent="addMember"
        >
          <input
            v-model="newMemberObjectId"
            :placeholder="t('members.objectIdPlaceholder')"
            required
            class="flex-1 rounded-[9px] border border-neutral-300 px-3 py-2 text-sm placeholder-neutral-400 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          />
          <select
            v-model="newMemberRole"
            class="rounded-[9px] border border-neutral-300 px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
          >
            <option v-for="role in ROLES" :key="role" :value="role">{{ role }}</option>
          </select>
          <AppButton type="submit" :loading="addingMember">{{ t('members.add') }}</AppButton>
        </form>

        <ul
          class="mt-4 divide-y divide-neutral-200 rounded-[14px] border border-neutral-200 bg-white shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
        >
          <li
            v-for="member in members"
            :key="member.entraObjectId"
            class="flex items-center justify-between px-5 py-3.5"
          >
            <span class="text-sm text-neutral-900">{{
              member.displayName ?? member.entraObjectId
            }}</span>
            <AppBadge>{{ member.role }}</AppBadge>
          </li>
        </ul>
      </section>
    </template>
  </main>
</template>
