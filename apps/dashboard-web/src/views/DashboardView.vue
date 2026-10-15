<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppButton from '@/components/AppButton.vue'
import AppBadge from '@/components/AppBadge.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'

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
  <div>
    <PageHeader :title="t('workspaces.title')" />

    <form class="mt-6 flex max-w-xl flex-wrap gap-2" @submit.prevent="createWorkspace">
      <input
        v-model="newWorkspaceName"
        :placeholder="t('workspaces.namePlaceholder')"
        :aria-label="t('workspaces.namePlaceholder')"
        required
        class="field min-w-0 flex-1"
      />
      <AppButton type="submit" :loading="creating">{{ t('workspaces.create') }}</AppButton>
    </form>

    <StateMessage v-if="error" state="error" :message="error" class="mt-6" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-6" />
    <StateMessage
      v-else-if="workspaces.length === 0"
      state="empty"
      :message="t('workspaces.empty')"
      class="mt-6"
    />

    <ul v-else class="card mt-6 divide-y divide-line-2">
      <li
        v-for="workspace in workspaces"
        :key="workspace.id"
        class="flex items-center justify-between gap-3 px-5 py-3.5"
      >
        <RouterLink
          :to="`/workspaces/${workspace.id}`"
          class="text-sm font-medium text-fg hover:text-signal-text hover:underline"
        >
          {{ workspace.name }}
        </RouterLink>
        <AppBadge>{{ workspace.role }}</AppBadge>
      </li>
    </ul>
  </div>
</template>
