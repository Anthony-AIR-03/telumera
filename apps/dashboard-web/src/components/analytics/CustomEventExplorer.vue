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
  <section class="card">
    <div class="flex flex-wrap items-center justify-between gap-3 px-5 pt-4">
      <h2 class="font-display text-[17px] font-medium">{{ t('analytics.events.title') }}</h2>
      <input
        v-model="eventNameFilter"
        type="search"
        :placeholder="t('analytics.events.filterPlaceholder')"
        :aria-label="t('analytics.events.filterPlaceholder')"
        class="field w-full font-mono text-xs sm:w-64"
      />
    </div>

    <StateMessage v-if="error" state="error" :message="error" class="px-5 py-4" />
    <StateMessage
      v-else-if="loading"
      state="loading"
      :message="t('common.loading')"
      class="px-5 py-4"
    />
    <StateMessage
      v-else-if="events && events.length === 0"
      state="empty"
      :message="t('analytics.events.empty')"
      class="px-5 py-4"
    />

    <ul v-else-if="events" class="mt-2.5 border-t border-line-2">
      <li v-for="e in events" :key="e.eventName" class="border-b border-line-2 last:border-b-0">
        <button
          type="button"
          class="flex w-full items-center justify-between gap-3 px-5 py-3 text-left hover:bg-surface"
          :aria-expanded="expanded.has(e.eventName)"
          @click="toggle(e.eventName)"
        >
          <span class="flex min-w-0 items-center gap-2">
            <svg
              width="12"
              height="12"
              viewBox="0 0 12 12"
              fill="none"
              stroke="currentColor"
              stroke-width="1.5"
              class="flex-none text-muted transition-transform"
              :style="{ transform: expanded.has(e.eventName) ? 'rotate(180deg)' : 'rotate(0deg)' }"
              aria-hidden="true"
            >
              <path d="M3 4.5L6 7.5L9 4.5" />
            </svg>
            <code class="truncate font-mono text-[13px] font-medium">{{ e.eventName }}</code>
          </span>
          <span class="flex gap-4 font-mono text-xs text-muted">
            <span class="tabular-nums">{{ t('analytics.events.count', { count: e.count }) }}</span>
            <span class="tabular-nums">{{
              t('analytics.events.uniqueSessions', { count: e.uniqueSessions })
            }}</span>
          </span>
        </button>
        <div v-if="expanded.has(e.eventName)" class="flex flex-wrap gap-1.5 px-5 pb-3.5 pl-10">
          <AppBadge v-for="prop in e.allowedProperties" :key="prop">{{ prop }}</AppBadge>
          <span v-if="e.allowedProperties.length === 0" class="text-xs text-muted">{{
            t('analytics.events.noProperties')
          }}</span>
        </div>
      </li>
    </ul>
  </section>
</template>
