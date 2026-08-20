<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'

interface Workspace {
  id: string
  name: string
  createdAt: string
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
    <RouterLink to="/">{{ t('common.backToWorkspaces') }}</RouterLink>
    <h1>{{ workspace?.name ?? workspaceId }}</h1>

    <p v-if="error" role="alert">{{ error }}</p>
    <p v-else-if="loading">{{ t('common.loading') }}</p>

    <template v-else>
      <section>
        <h2>{{ t('sites.title') }}</h2>
        <form @submit.prevent="createSite">
          <input v-model="newSiteName" :placeholder="t('sites.namePlaceholder')" required />
          <input v-model="newSiteDomain" :placeholder="t('sites.domainPlaceholder')" required />
          <button type="submit" :disabled="creatingSite">{{ t('sites.create') }}</button>
        </form>

        <p v-if="sites.length === 0">{{ t('sites.empty') }}</p>
        <ul v-else class="list">
          <li v-for="site in sites" :key="site.id">
            <RouterLink :to="`/sites/${site.id}`">{{ site.name }}</RouterLink>
            <span class="badge">{{ site.canonicalDomain }}</span>
          </li>
        </ul>
      </section>

      <section>
        <h2>{{ t('members.title') }}</h2>
        <form @submit.prevent="addMember">
          <input v-model="newMemberObjectId" :placeholder="t('members.objectIdPlaceholder')" required />
          <select v-model="newMemberRole">
            <option v-for="role in ROLES" :key="role" :value="role">{{ role }}</option>
          </select>
          <button type="submit" :disabled="addingMember">{{ t('members.add') }}</button>
        </form>

        <ul class="list">
          <li v-for="member in members" :key="member.entraObjectId">
            <span>{{ member.displayName ?? member.entraObjectId }}</span>
            <span class="badge">{{ member.role }}</span>
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

form {
  display: flex;
  gap: 0.5rem;
  margin-top: 1rem;
  flex-wrap: wrap;
}

.list {
  list-style: none;
  margin-top: 1rem;
}

.list li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--color-border);
}

.badge {
  font-size: 0.8rem;
  opacity: 0.65;
}
</style>
