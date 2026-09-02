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

/** Validated with dataviz's scripts/validate_palette.js — the same categorical set the approved dashboard-concept mockup already used for this exact purpose. */
const PALETTE = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#4a3aa7']

type Segment = { label: string; value: number; percent: number; color: string }

function aggregate(dimension: 'deviceCategory' | 'browserCategory' | 'osCategory'): Segment[] {
  if (!rows.value) return []
  const totals = new Map<string, number>()
  for (const row of rows.value) {
    totals.set(row[dimension], (totals.get(row[dimension]) ?? 0) + row.sessions)
  }
  const sum = [...totals.values()].reduce((a, b) => a + b, 0) || 1
  return [...totals.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([label, value], i) => ({
      label,
      value,
      percent: Math.round((value / sum) * 100),
      color: PALETTE[i % PALETTE.length]!,
    }))
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

const R = 40
const CX = 55
const CY = 55
const STROKE_WIDTH = 16
const CIRCUMFERENCE = 2 * Math.PI * R

function donutArcs(segments: Segment[]) {
  const total = segments.reduce((sum, s) => sum + s.value, 0) || 1
  let offset = 0
  return segments.map((s) => {
    const len = (s.value / total) * CIRCUMFERENCE
    const arc = {
      color: s.color,
      dasharray: `${Math.max(len - 2, 0)} ${CIRCUMFERENCE - len + 2}`,
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

    <div v-else class="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <div
        v-for="b in breakdowns"
        :key="b.key"
        class="rounded-[14px] border border-neutral-200 bg-white p-4 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
      >
        <div class="text-sm font-bold text-neutral-900">{{ b.title }}</div>
        <div class="mt-2 flex items-center gap-3.5">
          <svg width="110" height="110" viewBox="0 0 110 110" aria-hidden="true">
            <circle
              v-for="(arc, i) in donutArcs(b.segments)"
              :key="i"
              :cx="CX"
              :cy="CY"
              :r="R"
              fill="none"
              :stroke="arc.color"
              :stroke-width="STROKE_WIDTH"
              :stroke-dasharray="arc.dasharray"
              :stroke-dashoffset="arc.dashoffset"
              stroke-linecap="round"
              :transform="`rotate(-90 ${CX} ${CY})`"
            />
          </svg>
          <div class="flex flex-col items-start gap-1.5">
            <div
              v-for="s in b.segments"
              :key="s.label"
              class="flex items-center gap-1.5 text-xs text-neutral-600"
            >
              <span
                class="inline-block h-2 w-2 flex-shrink-0 rounded-sm"
                :style="{ background: s.color }"
              />
              {{ s.label }} &middot; <span class="tabular-nums">{{ s.percent }}%</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
