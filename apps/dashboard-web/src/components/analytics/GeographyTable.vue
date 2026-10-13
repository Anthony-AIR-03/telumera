<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppBadge from '@/components/AppBadge.vue'
import StateMessage from '@/components/StateMessage.vue'
import type { GeographyMetric } from '@/lib/analytics-types'

const props = defineProps<{
  siteId: string
  from: string
  to: string
}>()

const { t } = useI18n()

const rows = ref<GeographyMetric[] | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    rows.value = await apiClient.get<GeographyMetric[]>(
      `/sites/${props.siteId}/analytics/geography?from=${props.from}&to=${props.to}`,
    )
  } catch {
    error.value = t('analytics.geography.loadError')
  } finally {
    loading.value = false
  }
}

watch(() => [props.siteId, props.from, props.to], load, { immediate: true })

/** Until a GeoIP database is installed (M01.8), or for a site with no located events, every row's country is null — a table would be one meaningless "Unknown: 100%" row. Show that plainly instead. */
const hasRealData = computed(
  () => rows.value !== null && rows.value.some((r) => r.country !== null),
)

const regionNames =
  typeof Intl !== 'undefined' && 'DisplayNames' in Intl
    ? new Intl.DisplayNames(['en'], { type: 'region' })
    : null

function countryLabel(code: string | null): string {
  if (!code) return t('analytics.geography.unknown')
  try {
    return regionNames?.of(code.toUpperCase()) ?? code
  } catch {
    return code
  }
}

/** Below-privacy-floor rows (<5 visitors) collapse into a single "Other" row rather than listing a
 *  handful of individually identifiable countries — "marked, not hidden" (definitions §8 addendum). */
const displayRows = computed(() => {
  const source = rows.value ?? []
  const named = source.filter((r) => r.country !== null && !r.isBelowPrivacyFloor)
  const below = source.filter((r) => r.country !== null && r.isBelowPrivacyFloor)
  const result = named
    .sort((a, b) => b.visitors - a.visitors)
    .map((r) => ({ label: countryLabel(r.country), row: r, isOther: false }))
  if (below.length > 0) {
    result.push({
      label: t('analytics.geography.other'),
      isOther: true,
      row: {
        country: null,
        sessions: below.reduce((s, r) => s + r.sessions, 0),
        visitors: below.reduce((s, r) => s + r.visitors, 0),
        views: below.reduce((s, r) => s + r.views, 0),
        isBelowPrivacyFloor: true,
      } as GeographyMetric,
    })
  }
  return result
})

const totalVisitors = computed(() => displayRows.value.reduce((sum, e) => sum + e.row.visitors, 0))
const maxVisitors = computed(() => Math.max(1, ...displayRows.value.map((e) => e.row.visitors)))
function share(visitors: number): string {
  return totalVisitors.value === 0 ? '' : `${Math.round((100 * visitors) / totalVisitors.value)}%`
}
</script>

<template>
  <section class="card">
    <header class="flex items-baseline justify-between gap-3 px-5 pt-4">
      <h2 class="font-display text-[17px] font-medium">
        {{ t('analytics.geography.tableTitle') }}
      </h2>
      <span class="font-mono text-xs text-muted">{{ t('analytics.geography.tableMeta') }}</span>
    </header>

    <StateMessage v-if="error" state="error" :message="error" class="px-5 py-4" />
    <StateMessage
      v-else-if="loading"
      state="loading"
      :message="t('common.loading')"
      class="px-5 py-4"
    />

    <div
      v-else-if="rows && !hasRealData"
      class="flex flex-col items-center gap-1.5 px-5 py-11 text-center"
    >
      <div class="text-sm font-semibold text-fg-2">
        {{ t('analytics.geography.notAvailable') }}
      </div>
      <p class="max-w-sm text-xs text-muted">{{ t('analytics.geography.notAvailableDetail') }}</p>
    </div>

    <div v-else-if="rows" class="mt-2.5 overflow-x-auto">
      <table class="data-table">
        <thead>
          <tr>
            <th scope="col" class="w-full pl-5">{{ t('analytics.geography.country') }}</th>
            <th scope="col" class="num">{{ t('analytics.geography.visitors') }}</th>
            <th scope="col" class="num">{{ t('analytics.geography.sessions') }}</th>
            <th scope="col" class="num pr-5">{{ t('analytics.geography.views') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(entry, i) in displayRows" :key="i">
            <td
              class="key min-w-[200px] pl-5"
              :class="{ 'text-muted': entry.isOther }"
              :style="{ '--w': (entry.row.visitors / maxVisitors).toFixed(3) }"
            >
              <span class="inline-flex items-center gap-2">
                {{ entry.label }}
                <AppBadge v-if="entry.isOther">{{ t('analytics.privacyFloor') }}</AppBadge>
              </span>
            </td>
            <td class="num whitespace-nowrap">
              {{ entry.row.visitors.toLocaleString('en-US')
              }}<small class="ml-2 font-normal text-muted">{{ share(entry.row.visitors) }}</small>
            </td>
            <td class="num">{{ entry.row.sessions.toLocaleString('en-US') }}</td>
            <td class="num pr-5">{{ entry.row.views.toLocaleString('en-US') }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
