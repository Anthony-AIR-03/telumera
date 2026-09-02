<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppBadge from '@/components/AppBadge.vue'
import StateMessage from '@/components/StateMessage.vue'
import type { CustomEventMetric } from '@/lib/analytics-types'

const props = defineProps<{
  siteId: string
  from: string
  to: string
}>()

const { t } = useI18n()

const events = ref<CustomEventMetric[] | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const eventNameFilter = ref('')
const expanded = ref<Set<string>>(new Set())

async function load() {
  loading.value = true
  error.value = null
  try {
    const query = new URLSearchParams({ from: props.from, to: props.to })
    if (eventNameFilter.value.trim()) {
      query.set('eventName', eventNameFilter.value.trim())
    }
    events.value = await apiClient.get<CustomEventMetric[]>(
      `/sites/${props.siteId}/analytics/events?${query}`,
    )
  } catch {
    error.value = t('analytics.events.loadError')
  } finally {
    loading.value = false
  }
}

watch(() => [props.siteId, props.from, props.to], load, { immediate: true })

let filterTimer: ReturnType<typeof setTimeout> | undefined
watch(eventNameFilter, () => {
  clearTimeout(filterTimer)
  filterTimer = setTimeout(load, 300)
})

function toggle(name: string) {
  if (expanded.value.has(name)) {
    expanded.value.delete(name)
  } else {
    expanded.value.add(name)
  }
  // Set mutation alone doesn't trigger a template re-render — reassign to a new Set.
  expanded.value = new Set(expanded.value)
}
</script>

<template>
  <div
    class="rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
  >
    <input
      v-model="eventNameFilter"
      :placeholder="t('analytics.events.filterPlaceholder')"
      class="w-64 rounded-[9px] border border-neutral-300 px-3 py-2 text-sm placeholder-neutral-400 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
    />

    <StateMessage v-if="error" state="error" :message="error" class="mt-4" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-4" />
    <StateMessage
      v-else-if="events && events.length === 0"
      state="empty"
      :message="t('analytics.events.empty')"
      class="mt-4"
    />

    <ul v-else-if="events" class="mt-4 divide-y divide-neutral-200">
      <li v-for="e in events" :key="e.eventName">
        <button
          type="button"
          class="flex w-full items-center justify-between gap-3 py-3 text-left"
          :aria-expanded="expanded.has(e.eventName)"
          @click="toggle(e.eventName)"
        >
          <span class="flex items-center gap-2">
            <svg
              width="13"
              height="13"
              viewBox="0 0 20 20"
              fill="none"
              class="flex-shrink-0 text-neutral-400 transition-transform"
              :style="{ transform: expanded.has(e.eventName) ? 'rotate(180deg)' : 'rotate(0deg)' }"
              aria-hidden="true"
            >
              <path
                d="M5 7.5L10 12.5L15 7.5"
                stroke="currentColor"
                stroke-width="1.6"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
            <code class="font-mono text-xs font-semibold text-neutral-900">{{ e.eventName }}</code>
          </span>
          <span class="flex gap-4 text-xs text-neutral-600">
            <span class="tabular-nums">{{ t('analytics.events.count', { count: e.count }) }}</span>
            <span class="tabular-nums">{{
              t('analytics.events.uniqueSessions', { count: e.uniqueSessions })
            }}</span>
          </span>
        </button>
        <div v-if="expanded.has(e.eventName)" class="flex flex-wrap gap-1.5 pb-3.5 pl-5">
          <AppBadge v-for="prop in e.allowedProperties" :key="prop">{{ prop }}</AppBadge>
          <span v-if="e.allowedProperties.length === 0" class="text-xs text-neutral-400">{{
            t('analytics.events.noProperties')
          }}</span>
        </div>
      </li>
    </ul>
  </div>
</template>
