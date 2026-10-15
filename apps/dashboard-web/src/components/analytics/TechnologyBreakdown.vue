<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import StateMessage from '@/components/StateMessage.vue'
import type { TechnologyMetric } from '@/lib/analytics-types'

const props = defineProps<{
  siteId: string
  from: string
  to: string
}>()

const { t } = useI18n()

const rows = ref<TechnologyMetric[] | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    rows.value = await apiClient.get<TechnologyMetric[]>(
      `/sites/${props.siteId}/analytics/technology?from=${props.from}&to=${props.to}`,
    )
  } catch {
    error.value = t('analytics.technology.loadError')
  } finally {
    loading.value = false
  }
}

watch(() => [props.siteId, props.from, props.to], load, { immediate: true })

/**
 * Brand data-series order (docs/brand/README.md §4): Signal, Graphite 600, Cyan, Iris. Anything past
 * four categories folds into one "Other" segment in the comparison grey, so the ring never needs a
 * fifth hue. Every segment is paired with a text legend and its share, never colour alone.
 */
const SERIES = [
  'var(--color-signal)',
  'var(--color-fg-2)',
  'var(--color-series-3)',
  'var(--color-series-4)',
]
const OTHER = 'var(--color-comp)'

type Segment = { label: string; value: number; percent: number; color: string }

function aggregate(dimension: 'deviceCategory' | 'browserCategory' | 'osCategory'): Segment[] {
  if (!rows.value) return []
  const totals = new Map<string, number>()
  for (const row of rows.value) {
    totals.set(row[dimension], (totals.get(row[dimension]) ?? 0) + row.sessions)
  }
  const sorted = [...totals.entries()].sort((a, b) => b[1] - a[1])
  const sum = sorted.reduce((a, [, v]) => a + v, 0) || 1
  const head = sorted.length > SERIES.length ? sorted.slice(0, SERIES.length - 1) : sorted
  const tail = sorted.slice(head.length)
  const segments = head.map(([label, value], i) => ({ label, value, color: SERIES[i]! }))
  if (tail.length) {
    segments.push({
      label: t('analytics.technology.other'),
      value: tail.reduce((a, [, v]) => a + v, 0),
      color: OTHER,
    })
  }
  return segments.map((s) => ({ ...s, percent: Math.round((s.value / sum) * 100) }))
}

const breakdowns = computed(() => [
  { key: 'device', title: t('analytics.technology.device'), segments: aggregate('deviceCategory') },
  {
    key: 'browser',
    title: t('analytics.technology.browser'),
    segments: aggregate('browserCategory'),
  },
  { key: 'os', title: t('analytics.technology.os'), segments: aggregate('osCategory') },
])

/**
 * Ring geometry. Square (butt) ends and a 2px surface gap between segments keep the ring in the
 * brand's square vocabulary; it starts at 12 o'clock and runs clockwise in legend order.
 */
const R = 40
const C = 50
const STROKE = 12
const CIRC = 2 * Math.PI * R
const GAP = 2

function arcs(segments: Segment[]) {
  const total = segments.reduce((a, s) => a + s.value, 0) || 1
  const gap = segments.length > 1 ? GAP : 0
  let offset = 0
  return segments.map((s) => {
    const len = (s.value / total) * CIRC
    const arc = {
      ...s,
      dasharray: `${Math.max(len - gap, 0)} ${CIRC - len + gap}`,
      dashoffset: -offset,
    }
    offset += len
    return arc
  })
}
</script>

<template>
  <div>
    <StateMessage v-if="error" state="error" :message="error" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" />
    <StateMessage
      v-else-if="rows && rows.length === 0"
      state="empty"
      :message="t('analytics.technology.empty')"
    />

    <div v-else class="grid grid-cols-1 gap-4 xl:grid-cols-3">
      <section v-for="b in breakdowns" :key="b.key" class="card">
        <header class="flex items-baseline justify-between gap-3 px-5 pt-4">
          <h2 class="font-display text-[17px] font-medium">{{ b.title }}</h2>
          <span class="font-mono text-xs text-muted">{{ t('analytics.technology.meta') }}</span>
        </header>
        <div class="flex items-center gap-5 px-5 pt-3.5 pb-[18px]">
          <div class="relative size-[104px] flex-none">
            <svg viewBox="0 0 100 100" class="size-full -rotate-90" aria-hidden="true">
              <circle
                v-for="arc in arcs(b.segments)"
                :key="arc.label"
                :cx="C"
                :cy="C"
                :r="R"
                fill="none"
                :stroke-width="STROKE"
                :stroke-dasharray="arc.dasharray"
                :stroke-dashoffset="arc.dashoffset"
                :style="{ stroke: arc.color }"
              >
                <title>{{ arc.label }} · {{ arc.percent }}%</title>
              </circle>
            </svg>
            <div
              v-if="b.segments.length"
              class="absolute inset-0 grid place-content-center text-center"
            >
              <b class="font-mono text-lg leading-none font-medium tabular-nums"
                >{{ b.segments[0]!.percent }}%</b
              >
              <span class="mt-1 max-w-[64px] truncate text-[11px] text-muted">{{
                b.segments[0]!.label
              }}</span>
            </div>
          </div>
          <ul class="grid max-w-[320px] min-w-0 flex-1 gap-2 text-[13px]">
            <li
              v-for="s in b.segments"
              :key="s.label"
              class="flex items-center justify-between gap-3"
            >
              <span class="flex min-w-0 items-center gap-2">
                <i
                  class="block h-2 w-2 flex-none"
                  :style="{ background: s.color }"
                  aria-hidden="true"
                />
                <span class="truncate">{{ s.label }}</span>
              </span>
              <b class="font-mono font-medium tabular-nums">{{ s.percent }}%</b>
            </li>
          </ul>
        </div>
      </section>
    </div>
  </div>
</template>
