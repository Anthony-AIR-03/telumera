<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppButton from '@/components/AppButton.vue'
import AppBadge from '@/components/AppBadge.vue'
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
  <main>
    <h1 class="font-display text-xl font-bold text-neutral-900">
      {{ t('workspaces.title') }}
    </h1>

    <form class="mt-4 flex gap-2" @submit.prevent="createWorkspace">
      <input
        v-model="newWorkspaceName"
        :placeholder="t('workspaces.namePlaceholder')"
        required
        class="flex-1 rounded-[9px] border border-neutral-300 px-3 py-2 text-sm placeholder-neutral-400 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
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

    <ul
      v-else
      class="mt-6 divide-y divide-neutral-200 rounded-[14px] border border-neutral-200 bg-white shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
    >
      <li
        v-for="workspace in workspaces"
        :key="workspace.id"
        class="flex items-center justify-between px-5 py-3.5"
      >
        <RouterLink
          :to="`/workspaces/${workspace.id}`"
          class="text-sm font-semibold text-brand-700 hover:text-brand-800"
        >
          {{ workspace.name }}
        </RouterLink>
        <AppBadge>{{ workspace.role }}</AppBadge>
      </li>
    </ul>
  </main>
</template>
