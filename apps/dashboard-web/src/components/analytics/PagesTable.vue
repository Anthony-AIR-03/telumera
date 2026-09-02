<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppBadge from '@/components/AppBadge.vue'
import StateMessage from '@/components/StateMessage.vue'
import type { PagedResult, PageMetric } from '@/lib/analytics-types'

const props = defineProps<{
  siteId: string
  from: string
  to: string
}>()

const { t } = useI18n()

const result = ref<PagedResult<PageMetric> | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const search = ref('')
const page = ref(1)
const sort = ref<'views' | 'visitors' | 'entries' | 'exits'>('views')
const sortDir = ref<'asc' | 'desc'>('desc')
const pageSize = 25

async function load() {
  loading.value = true
  error.value = null
  try {
    const query = new URLSearchParams({
      from: props.from,
      to: props.to,
      search: search.value,
      sort: sort.value,
      sortDir: sortDir.value,
      page: String(page.value),
      pageSize: String(pageSize),
    })
    result.value = await apiClient.get<PagedResult<PageMetric>>(
      `/sites/${props.siteId}/analytics/pages?${query}`,
    )
  } catch {
    error.value = t('analytics.pages.loadError')
  } finally {
    loading.value = false
  }
}

function toggleSort(column: typeof sort.value) {
  if (sort.value === column) {
    sortDir.value = sortDir.value === 'desc' ? 'asc' : 'desc'
  } else {
    sort.value = column
    sortDir.value = 'desc'
  }
  page.value = 1
}

watch(() => [props.siteId, props.from, props.to, sort.value, sortDir.value, page.value], load, {
  immediate: true,
})
// Search is debounced separately from the other filters, which should refetch immediately.
let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  page.value = 1
  clearTimeout(searchTimer)
  searchTimer = setTimeout(load, 300)
})

const maxViews = computed(() =>
  result.value && result.value.items.length > 0
    ? Math.max(...result.value.items.map((p) => p.views))
    : 1,
)
</script>

<template>
  <div
    class="rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
  >
    <input
      v-model="search"
      :placeholder="t('analytics.pages.searchPlaceholder')"
      class="w-64 rounded-[9px] border border-neutral-300 px-3 py-2 text-sm placeholder-neutral-400 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
    />

    <StateMessage v-if="error" state="error" :message="error" class="mt-4" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-4" />
    <StateMessage
      v-else-if="result && result.items.length === 0"
      state="empty"
      :message="t('analytics.pages.empty')"
      class="mt-4"
    />

    <div v-else-if="result" class="mt-4 overflow-x-auto">
      <table class="w-full border-collapse text-sm">
        <thead>
          <tr class="text-xs font-semibold text-neutral-500">
            <th scope="col" class="px-2 pb-2.5 text-left">{{ t('analytics.pages.path') }}</th>
            <th
              scope="col"
              class="cursor-pointer px-2 pb-2.5 text-right"
              @click="toggleSort('views')"
            >
              {{ t('analytics.pages.views') }}
              <span class="ml-0.5" :class="sort === 'views' ? 'opacity-100' : 'opacity-40'">{{
                sort === 'views' && sortDir === 'asc' ? '▲' : '▼'
              }}</span>
            </th>
            <th
              scope="col"
              class="cursor-pointer px-2 pb-2.5 text-right"
              @click="toggleSort('visitors')"
            >
              {{ t('analytics.pages.visitors') }}
            </th>
            <th
              scope="col"
              class="cursor-pointer px-2 pb-2.5 text-right"
              @click="toggleSort('entries')"
            >
              {{ t('analytics.pages.entries') }}
            </th>
            <th
              scope="col"
              class="cursor-pointer px-2 pb-2.5 text-right"
              @click="toggleSort('exits')"
            >
              {{ t('analytics.pages.exits') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-right">{{ t('analytics.pages.engaged') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="p in result.items" :key="p.path" class="border-t border-neutral-200">
            <td class="px-2 py-2.5">
              <code class="font-mono text-xs text-neutral-700">{{ p.path }}</code>
              <div class="mt-1 h-1.5 rounded bg-neutral-100">
                <div
                  class="h-full rounded bg-brand-300"
                  :style="{ width: `${(p.views / maxViews) * 100}%` }"
                />
              </div>
              <AppBadge v-if="p.isBelowPrivacyFloor" tone="neutral" class="mt-1">{{
                t('analytics.privacyFloor')
              }}</AppBadge>
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ p.views.toLocaleString('en-US') }}
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ p.visitors.toLocaleString('en-US') }}
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ p.entries.toLocaleString('en-US') }}
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ p.exits.toLocaleString('en-US') }}
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ p.engagedViews.toLocaleString('en-US') }}
            </td>
          </tr>
        </tbody>
      </table>

      <div
        v-if="result.total > pageSize"
        class="mt-4 flex items-center justify-between text-xs text-neutral-500"
      >
        <span>{{
          t('analytics.pages.pageOf', {
            page: result.page,
            total: Math.ceil(result.total / pageSize),
          })
        }}</span>
        <div class="flex gap-2">
          <button
            type="button"
            class="rounded-[7px] border border-neutral-200 px-2.5 py-1 font-semibold disabled:cursor-not-allowed disabled:opacity-40"
            :disabled="page <= 1"
            @click="page--"
          >
            {{ t('analytics.pages.prev') }}
          </button>
          <button
            type="button"
            class="rounded-[7px] border border-neutral-200 px-2.5 py-1 font-semibold disabled:cursor-not-allowed disabled:opacity-40"
            :disabled="page * pageSize >= result.total"
            @click="page++"
          >
            {{ t('analytics.pages.next') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
