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
const SORTABLE = ['views', 'visitors', 'entries', 'exits'] as const

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
  <section class="card">
    <div class="flex flex-wrap items-center justify-between gap-3 px-5 pt-4">
      <h2 class="font-display text-[17px] font-medium">{{ t('analytics.pages.title') }}</h2>
      <input
        v-model="search"
        type="search"
        :placeholder="t('analytics.pages.searchPlaceholder')"
        :aria-label="t('analytics.pages.searchPlaceholder')"
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
      v-else-if="result && result.items.length === 0"
      state="empty"
      :message="t('analytics.pages.empty')"
      class="px-5 py-4"
    />

    <div v-else-if="result" class="mt-2.5 overflow-x-auto">
      <table class="data-table">
        <thead>
          <tr>
            <th scope="col" class="w-full pl-5">{{ t('analytics.pages.path') }}</th>
            <th
              v-for="col in SORTABLE"
              :key="col"
              scope="col"
              class="num"
              :aria-sort="
                sort === col ? (sortDir === 'asc' ? 'ascending' : 'descending') : undefined
              "
            >
              <button
                type="button"
                class="inline-flex items-center gap-1 uppercase hover:text-fg"
                :class="{ 'text-fg': sort === col }"
                @click="toggleSort(col)"
              >
                {{ t(`analytics.pages.${col}`) }}
                <span aria-hidden="true" :class="sort === col ? 'opacity-100' : 'opacity-0'">{{
                  sort === col && sortDir === 'asc' ? '▲' : '▼'
                }}</span>
              </button>
            </th>
            <th scope="col" class="num pr-5">{{ t('analytics.pages.engaged') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="p in result.items" :key="p.path">
            <td class="key pl-5" :style="{ '--w': (p.views / maxViews).toFixed(3) }">
              <span class="inline-flex max-w-[48ch] items-center gap-2">
                <span class="truncate">{{ p.path }}</span>
                <AppBadge v-if="p.isBelowPrivacyFloor">{{ t('analytics.privacyFloor') }}</AppBadge>
              </span>
            </td>
            <td class="num">{{ p.views.toLocaleString('en-US') }}</td>
            <td class="num">{{ p.visitors.toLocaleString('en-US') }}</td>
            <td class="num">{{ p.entries.toLocaleString('en-US') }}</td>
            <td class="num">{{ p.exits.toLocaleString('en-US') }}</td>
            <td class="num pr-5">{{ p.engagedViews.toLocaleString('en-US') }}</td>
          </tr>
        </tbody>
      </table>

      <div
        v-if="result.total > pageSize"
        class="flex items-center justify-between gap-3 border-t border-line-2 px-5 py-3 font-mono text-xs text-muted"
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
            class="rounded-[3px] border border-line px-2.5 py-1 font-sans font-medium text-fg hover:border-fg-2 disabled:cursor-not-allowed disabled:opacity-40"
            :disabled="page <= 1"
            @click="page--"
          >
            {{ t('analytics.pages.prev') }}
          </button>
          <button
            type="button"
            class="rounded-[3px] border border-line px-2.5 py-1 font-sans font-medium text-fg hover:border-fg-2 disabled:cursor-not-allowed disabled:opacity-40"
            :disabled="page * pageSize >= result.total"
            @click="page++"
          >
            {{ t('analytics.pages.next') }}
          </button>
        </div>
      </div>
    </div>
  </section>
</template>
