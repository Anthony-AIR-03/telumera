<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import { resolveDateRange } from '@/lib/date-range'
import StateMessage from '@/components/StateMessage.vue'
import AnalyticsDateRangeControl from '@/components/analytics/AnalyticsDateRangeControl.vue'
import SiteSwitcher from '@/components/analytics/SiteSwitcher.vue'
import MetricCard from '@/components/analytics/MetricCard.vue'
import LiveVisitorsPanel from '@/components/analytics/LiveVisitorsPanel.vue'
import TrafficChart from '@/components/analytics/TrafficChart.vue'
import PagesTable from '@/components/analytics/PagesTable.vue'
import AcquisitionTable from '@/components/analytics/AcquisitionTable.vue'
import TechnologyBreakdown from '@/components/analytics/TechnologyBreakdown.vue'
import GeographyTable from '@/components/analytics/GeographyTable.vue'
import CustomEventExplorer from '@/components/analytics/CustomEventExplorer.vue'
import GlossaryDrawer from '@/components/analytics/GlossaryDrawer.vue'
import type { OverviewResponse, TimeSeriesResponse } from '@/lib/analytics-types'

// Async: pulls in the ~68KB vendored world-map paths, only when the geography tab is opened.
const GeographyMap = defineAsyncComponent(() => import('@/components/analytics/GeographyMap.vue'))

interface SiteSummary {
  id: string
  name: string
  canonicalDomain: string
  workspaceId: string
}

const route = useRoute()
const { t } = useI18n()
const siteId = computed(() => route.params.id as string)
const range = computed(() => resolveDateRange(route.query))

const site = ref<SiteSummary | null>(null)
const siblingSites = ref<SiteSummary[]>([])
const siteLoading = ref(true)
const siteError = ref<string | null>(null)

const overview = ref<OverviewResponse | null>(null)
const overviewLoading = ref(true)
const overviewError = ref<string | null>(null)

const timeSeries = ref<TimeSeriesResponse | null>(null)
const chartLoading = ref(true)
const chartError = ref<string | null>(null)

const TABS = ['pages', 'acquisition', 'technology', 'geography', 'events'] as const
type Tab = (typeof TABS)[number]
const activeTab = computed<Tab>(() => {
  const requested = route.query.tab
  return TABS.includes(requested as Tab) ? (requested as Tab) : 'pages'
})

const glossaryOpen = ref(false)

async function loadSite() {
  siteLoading.value = true
  siteError.value = null
  try {
    site.value = await apiClient.get<SiteSummary>(`/sites/${siteId.value}`)
    const sites = await apiClient.get<SiteSummary[]>(`/workspaces/${site.value.workspaceId}/sites`)
    siblingSites.value = sites.filter((s) => s.id !== siteId.value)
  } catch {
    siteError.value = t('analytics.loadError')
  } finally {
    siteLoading.value = false
  }
}

async function loadOverview() {
  overviewLoading.value = true
  overviewError.value = null
  try {
    overview.value = await apiClient.get<OverviewResponse>(
      `/sites/${siteId.value}/analytics/overview?from=${range.value.from}&to=${range.value.to}&compare=true`,
    )
  } catch {
    overviewError.value = t('analytics.overviewError')
  } finally {
    overviewLoading.value = false
  }
}

async function loadTimeSeries() {
  chartLoading.value = true
  chartError.value = null
  try {
    timeSeries.value = await apiClient.get<TimeSeriesResponse>(
      `/sites/${siteId.value}/analytics/timeseries?from=${range.value.from}&to=${range.value.to}&interval=day`,
    )
  } catch {
    chartError.value = t('analytics.chartError')
  } finally {
    chartLoading.value = false
  }
}

watch(siteId, loadSite, { immediate: true })
watch(
  [siteId, range],
  () => {
    loadOverview()
    loadTimeSeries()
  },
  { immediate: true },
)

function formatNumber(value: number): string {
  return value.toLocaleString('en-US')
}
function formatPercent(value: number): string {
  return `${(value * 100).toFixed(1)}%`
}

/**
 * current/previous compared as a percentage swing; `favorableWhenUp` lets bounce rate (where a
 * decrease is the good direction) share this same helper. The caller decides whether the previous
 * period had any real data at all (`hasPriorData`) — checking a single metric's own previous value
 * against 0 isn't reliable: bounceRate's own empty-period fallback is 100% (1 - engagedRate's 0%),
 * not 0%, so it would never trip a "previous === 0" guard even with zero prior sessions. Confirmed
 * live: a fresh site's first-ever week showed "Bounce rate ↓ 0.0% vs prior period" before this fix.
 */
function deltaFor(
  current: number,
  previous: number,
  favorableWhenUp: boolean,
  hasPriorData: boolean,
): { text: string; favorable: boolean } | null {
  if (!hasPriorData) return null
  const change = previous === 0 ? (current === 0 ? 0 : 1) : (current - previous) / previous
  const up = change >= 0
  return {
    text: t('analytics.overview.deltaLabel', { percent: Math.abs(change * 100).toFixed(1) }),
    favorable: up === favorableWhenUp,
  }
}

const cards = computed(() => {
  if (!overview.value) return []
  const cur = overview.value.current
  const prev = overview.value.previous
  const hasPriorData = (prev?.sessions ?? 0) > 0
  return [
    {
      label: t('analytics.overview.visitors'),
      value: formatNumber(cur.visitors),
      delta: prev && deltaFor(cur.visitors, prev.visitors, true, hasPriorData),
    },
    {
      label: t('analytics.overview.sessions'),
      value: formatNumber(cur.sessions),
      delta: prev && deltaFor(cur.sessions, prev.sessions, true, hasPriorData),
    },
    {
      label: t('analytics.overview.views'),
      value: formatNumber(cur.views),
      delta: prev && deltaFor(cur.views, prev.views, true, hasPriorData),
    },
    {
      label: t('analytics.overview.engagedRate'),
      value: formatPercent(cur.engagedSessionRate),
      delta: prev && deltaFor(cur.engagedSessionRate, prev.engagedSessionRate, true, hasPriorData),
    },
    {
      label: t('analytics.overview.bounceRate'),
      value: formatPercent(cur.bounceRate),
      delta: prev && deltaFor(cur.bounceRate, prev.bounceRate, false, hasPriorData),
    },
  ]
})
</script>

<template>
  <main>
    <div class="flex flex-wrap items-center gap-2.5">
      <div>
        <RouterLink
          v-if="site"
          :to="`/workspaces/${site.workspaceId}`"
          class="text-xs font-semibold text-neutral-400 hover:text-neutral-700"
        >
          {{ t('common.backToWorkspace') }}
        </RouterLink>
        <h1 class="font-display mt-2 text-xl font-bold text-neutral-900">
          {{ site?.name ?? siteId }}
        </h1>
      </div>
      <SiteSwitcher
        v-if="site"
        :current-site-id="siteId"
        :current-domain="site.canonicalDomain"
        :sibling-sites="siblingSites"
      />
    </div>

    <StateMessage v-if="siteError" state="error" :message="siteError" class="mt-6" />
    <StateMessage
      v-else-if="siteLoading"
      state="loading"
      :message="t('common.loading')"
      class="mt-6"
    />

    <template v-else>
      <LiveVisitorsPanel :site-id="siteId" class="mt-5" />

      <div class="mt-4 flex flex-wrap items-center gap-2.5">
        <AnalyticsDateRangeControl />
        <RouterLink
          :to="{ name: 'site-analytics-quality', params: { id: siteId }, query: route.query }"
          class="ml-auto text-sm font-semibold text-brand-700 hover:text-brand-800"
        >
          {{ t('analytics.quality.link') }}
        </RouterLink>
        <button
          type="button"
          class="border-none bg-transparent text-sm font-semibold text-brand-700 hover:text-brand-800"
          @click="glossaryOpen = true"
        >
          {{ t('analytics.glossary.link') }}
        </button>
      </div>

      <StateMessage v-if="overviewError" state="error" :message="overviewError" class="mt-6" />
      <StateMessage
        v-else-if="overviewLoading"
        state="loading"
        :message="t('common.loading')"
        class="mt-6"
      />
      <template v-else-if="overview">
        <div class="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <MetricCard
            v-for="card in cards"
            :key="card.label"
            :label="card.label"
            :value="card.value"
            :delta="card.delta"
            @open-glossary="glossaryOpen = true"
          />
        </div>
        <p class="mt-2 text-xs text-neutral-400">{{ overview.visitorCountCaveat }}</p>
      </template>

      <div
        class="mt-4 rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
      >
        <h2 class="font-display text-sm font-bold text-neutral-900">
          {{ t('analytics.chart.title') }}
        </h2>
        <StateMessage v-if="chartError" state="error" :message="chartError" class="mt-3" />
        <StateMessage
          v-else-if="chartLoading"
          state="loading"
          :message="t('common.loading')"
          class="mt-3"
        />
        <TrafficChart v-else-if="timeSeries" :points="timeSeries.points" class="mt-2" />
      </div>

      <nav
        class="mt-5 flex gap-1 border-b border-neutral-200"
        role="tablist"
        :aria-label="t('analytics.tabs.label')"
      >
        <RouterLink
          v-for="tab in TABS"
          :key="tab"
          :to="{ query: { ...$route.query, tab } }"
          role="tab"
          :aria-selected="activeTab === tab"
          class="rounded-t-[9px] px-3.5 py-2 text-sm font-semibold no-underline"
          :class="
            activeTab === tab
              ? 'bg-brand-50 font-bold text-brand-700'
              : 'text-neutral-600 hover:bg-neutral-50'
          "
        >
          {{ t(`analytics.tabs.${tab}`) }}
        </RouterLink>
      </nav>

      <div class="mt-4">
        <PagesTable
          v-if="activeTab === 'pages'"
          :site-id="siteId"
          :from="range.from"
          :to="range.to"
        />
        <AcquisitionTable
          v-else-if="activeTab === 'acquisition'"
          :site-id="siteId"
          :from="range.from"
          :to="range.to"
        />
        <TechnologyBreakdown
          v-else-if="activeTab === 'technology'"
          :site-id="siteId"
          :from="range.from"
          :to="range.to"
        />
        <div v-else-if="activeTab === 'geography'" class="space-y-4">
          <GeographyMap :site-id="siteId" :from="range.from" :to="range.to" />
          <GeographyTable :site-id="siteId" :from="range.from" :to="range.to" />
        </div>
        <CustomEventExplorer
          v-else-if="activeTab === 'events'"
          :site-id="siteId"
          :from="range.from"
          :to="range.to"
        />
      </div>
    </template>

    <GlossaryDrawer :open="glossaryOpen" @close="glossaryOpen = false" />
  </main>
</template>
