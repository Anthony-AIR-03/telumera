<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import { fillDailyGaps, previousRange, resolveDateRange } from '@/lib/date-range'
import { useLiveSnapshot } from '@/lib/use-live-snapshot'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import AuditStrip from '@/components/analytics/AuditStrip.vue'
import ComingModulesCard from '@/components/analytics/ComingModulesCard.vue'
import LivePill from '@/components/analytics/LivePill.vue'
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
const router = useRouter()
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
const previousSeries = ref<TimeSeriesResponse | null>(null)
const chartLoading = ref(true)
const chartError = ref<string | null>(null)

const TABS = ['pages', 'acquisition', 'technology', 'geography', 'events'] as const
type Tab = (typeof TABS)[number]
const activeTab = computed<Tab>(() => {
  const requested = route.query.tab
  return TABS.includes(requested as Tab) ? (requested as Tab) : 'pages'
})

/** The KPI the chart plots, kept in the route query like the tab so a shared link reopens on it.
 *  Only the counts have a daily series; the two rates are shown but not chartable. */
const CHART_METRICS = ['visitors', 'sessions', 'views'] as const
type ChartMetric = (typeof CHART_METRICS)[number]
const chartMetric = computed<ChartMetric>(() => {
  const requested = route.query.metric
  return CHART_METRICS.includes(requested as ChartMetric) ? (requested as ChartMetric) : 'visitors'
})
function selectMetric(metric: ChartMetric) {
  router.replace({ query: { ...route.query, metric } })
}
const chartMetricLabel = computed(() => t(`analytics.overview.${chartMetric.value}`))

const glossaryOpen = ref(false)

/** One live connection for the page, shared by the header pill and the "Live now" card. */
const live = useLiveSnapshot(siteId)

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
  const prev = previousRange(range.value)
  const url = (from: string, to: string) =>
    `/sites/${siteId.value}/analytics/timeseries?from=${from}&to=${to}&interval=day`
  // The comparison line is optional: if only the previous period fails, still draw this one.
  const [current, previous] = await Promise.allSettled([
    apiClient.get<TimeSeriesResponse>(url(range.value.from, range.value.to)),
    apiClient.get<TimeSeriesResponse>(url(prev.from, prev.to)),
  ])
  if (current.status === 'fulfilled') {
    // Zero-fill quiet days, but keep a range with no traffic at all empty so the empty state shows.
    const hasData = current.value.points.length > 0
    timeSeries.value = hasData
      ? { ...current.value, points: fillDailyGaps(current.value.points, range.value) }
      : current.value
    previousSeries.value =
      previous.status === 'fulfilled'
        ? { ...previous.value, points: fillDailyGaps(previous.value.points, prev) }
        : null
  } else {
    chartError.value = t('analytics.chartError')
  }
  chartLoading.value = false
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
): { percent: string; favorable: boolean; up: boolean } | null {
  if (!hasPriorData) return null
  const change = previous === 0 ? (current === 0 ? 0 : 1) : (current - previous) / previous
  const up = change >= 0
  return {
    percent: `${Math.abs(change * 100).toFixed(1)}%`,
    favorable: up === favorableWhenUp,
    up,
  }
}

const cards = computed(() => {
  if (!overview.value) return []
  const cur = overview.value.current
  const prev = overview.value.previous
  const hasPriorData = (prev?.sessions ?? 0) > 0
  return [
    {
      metric: 'visitors' as ChartMetric | null,
      label: t('analytics.overview.visitors'),
      value: formatNumber(cur.visitors),
      delta: prev && deltaFor(cur.visitors, prev.visitors, true, hasPriorData),
    },
    {
      metric: 'sessions' as ChartMetric | null,
      label: t('analytics.overview.sessions'),
      value: formatNumber(cur.sessions),
      delta: prev && deltaFor(cur.sessions, prev.sessions, true, hasPriorData),
    },
    {
      metric: 'views' as ChartMetric | null,
      label: t('analytics.overview.views'),
      value: formatNumber(cur.views),
      delta: prev && deltaFor(cur.views, prev.views, true, hasPriorData),
    },
    {
      metric: null,
      label: t('analytics.overview.engagedRate'),
      value: formatPercent(cur.engagedSessionRate),
      delta: prev && deltaFor(cur.engagedSessionRate, prev.engagedSessionRate, true, hasPriorData),
    },
    {
      metric: null,
      label: t('analytics.overview.bounceRate'),
      value: formatPercent(cur.bounceRate),
      delta: prev && deltaFor(cur.bounceRate, prev.bounceRate, false, hasPriorData),
    },
  ]
})
</script>

<template>
  <div>
    <PageHeader
      :title="site?.name ?? siteId"
      :crumbs="
        site
          ? [
              { label: t('common.workspace'), to: `/workspaces/${site.workspaceId}` },
              { label: site.canonicalDomain, to: `/sites/${site.id}` },
              { label: t('nav.analytics') },
            ]
          : []
      "
    >
      <template v-if="site" #title-after>
        <SiteSwitcher
          :current-site-id="siteId"
          :current-domain="site.canonicalDomain"
          :sibling-sites="siblingSites"
        />
      </template>
      <template v-if="site" #tools>
        <LivePill :count="live.snapshot.value.activeVisitors" :state="live.state.value" />
        <AnalyticsDateRangeControl />
      </template>
    </PageHeader>

    <StateMessage v-if="siteError" state="error" :message="siteError" class="mt-6" />
    <StateMessage
      v-else-if="siteLoading"
      state="loading"
      :message="t('common.loading')"
      class="mt-6"
    />

    <template v-else>
      <AuditStrip
        :site-id="siteId"
        :from="range.from"
        :to="range.to"
        class="mt-[22px]"
        @open-glossary="glossaryOpen = true"
      />

      <StateMessage v-if="overviewError" state="error" :message="overviewError" class="mt-6" />
      <StateMessage
        v-else-if="overviewLoading"
        state="loading"
        :message="t('common.loading')"
        class="mt-6"
      />
      <template v-else-if="overview">
        <div
          class="card mt-5 grid grid-cols-2 gap-px overflow-hidden bg-line-2 sm:grid-cols-3 lg:grid-cols-5 [&>:last-child]:col-span-2 lg:[&>:last-child]:col-span-1"
          role="group"
          :aria-label="t('analytics.overview.label')"
        >
          <MetricCard
            v-for="card in cards"
            :key="card.label"
            :label="card.label"
            :value="card.value"
            :delta="card.delta"
            :selectable="card.metric !== null"
            :selected="card.metric === chartMetric"
            @select="card.metric && selectMetric(card.metric)"
            @open-glossary="glossaryOpen = true"
          />
        </div>
        <p class="mt-2 text-xs text-muted">{{ overview.visitorCountCaveat }}</p>
      </template>

      <section class="card mt-4 pb-2">
        <header class="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1 px-5 pt-4">
          <h2 class="font-display text-[17px] font-medium">
            {{ t('analytics.chart.title', { metric: chartMetricLabel }) }}
          </h2>
          <div class="flex gap-4 font-mono text-xs text-muted">
            <span class="inline-flex items-center gap-1.5">
              <i class="block h-0.5 w-3.5 bg-signal" aria-hidden="true" />
              {{ t('analytics.chart.current') }}
            </span>
            <span class="inline-flex items-center gap-1.5">
              <i class="legend-dash block h-0.5 w-3.5" aria-hidden="true" />
              {{ t('analytics.chart.previous') }}
            </span>
          </div>
        </header>
        <StateMessage v-if="chartError" state="error" :message="chartError" class="px-5 py-3" />
        <StateMessage
          v-else-if="chartLoading"
          state="loading"
          :message="t('common.loading')"
          class="px-5 py-3"
        />
        <StateMessage
          v-else-if="timeSeries && timeSeries.points.length === 0"
          state="empty"
          :message="t('analytics.chart.empty')"
          class="px-5 py-3"
        />
        <TrafficChart
          v-else-if="timeSeries"
          :points="timeSeries.points"
          :previous="previousSeries?.points ?? null"
          :metric="chartMetric"
          :metric-label="chartMetricLabel"
          class="mt-3"
        />
      </section>

      <div class="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <LiveVisitorsPanel :snapshot="live.snapshot.value" :state="live.state.value" />
        <ComingModulesCard />
      </div>

      <nav
        class="mt-8 flex gap-1 overflow-x-auto overflow-y-hidden border-b border-line"
        role="tablist"
        :aria-label="t('analytics.tabs.label')"
      >
        <RouterLink
          v-for="tab in TABS"
          :key="tab"
          :to="{ query: { ...$route.query, tab } }"
          role="tab"
          :aria-selected="activeTab === tab"
          class="-mb-px border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap"
          :class="
            activeTab === tab
              ? 'border-signal text-fg'
              : 'border-transparent text-muted hover:text-fg'
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
  </div>
</template>

<style scoped>
.legend-dash {
  background: repeating-linear-gradient(90deg, var(--color-comp) 0 4px, transparent 4px 7px);
}
</style>
