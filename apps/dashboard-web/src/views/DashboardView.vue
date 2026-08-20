<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'

interface WorkspaceSummary {
  id: string
  name: string
  createdAt: string
  role: string
}

const { t } = useI18n()
const workspaces = ref<WorkspaceSummary[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

const newWorkspaceName = ref('')
const creating = ref(false)

async function loadWorkspaces() {
  loading.value = true
  error.value = null
  try {
    workspaces.value = await apiClient.get<WorkspaceSummary[]>('/workspaces')
  } catch {
    error.value = t('workspaces.loadError')
  } finally {
    loading.value = false
  }
}

async function createWorkspace() {
  if (!newWorkspaceName.value.trim()) {
    return
  }

  creating.value = true
  error.value = null
  try {
    await apiClient.post('/workspaces', { Name: newWorkspaceName.value.trim() })
    newWorkspaceName.value = ''
    await loadWorkspaces()
  } catch {
    error.value = t('workspaces.createError')
  } finally {
    creating.value = false
  }
}

onMounted(loadWorkspaces)
</script>

<template>
  <main>
    <h1>{{ t('workspaces.title') }}</h1>

    <form @submit.prevent="createWorkspace">
      <input v-model="newWorkspaceName" :placeholder="t('workspaces.namePlaceholder')" required />
      <button type="submit" :disabled="creating">{{ t('workspaces.create') }}</button>
    </form>

    <p v-if="error" role="alert">{{ error }}</p>
    <p v-else-if="loading">{{ t('common.loading') }}</p>
    <p v-else-if="workspaces.length === 0">{{ t('workspaces.empty') }}</p>

    <ul v-else class="list">
      <li v-for="workspace in workspaces" :key="workspace.id">
        <RouterLink :to="`/workspaces/${workspace.id}`">{{ workspace.name }}</RouterLink>
        <span class="badge">{{ workspace.role }}</span>
      </li>
    </ul>
  </main>
</template>

<style scoped>
form {
  display: flex;
  gap: 0.5rem;
  margin-top: 1rem;
}

.list {
  list-style: none;
  margin-top: 1.5rem;
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
