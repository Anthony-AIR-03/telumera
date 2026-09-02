<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { QualityDayPoint } from '@/lib/analytics-types'

const props = defineProps<{ series: QualityDayPoint[] }>()

const { t } = useI18n()

/**
 * Mutually-exclusive collector outcomes for events that arrived — so they stack honestly and sum to
 * "events that hit the collector". Palette validated with the dataviz skill (light + dark, all CVD
 * checks pass): #047857 / #dc2626 / #2563eb / #b45309.
 */
const SERIES = [
  { key: 'accepted', color: '#047857' },
  { key: 'rejected', color: '#dc2626' },
  { key: 'duplicate', color: '#2563eb' },
  { key: 'dropped_overload', color: '#b45309' },
] as const

const REJECTED_KEYS = [
  'rejected_validation',
  'rejected_unknown_token',
  'rejected_origin',
  'rejected_module_disabled',
]

const WIDTH = 1000
const HEIGHT = 240
const PAD = { top: 8, right: 8, bottom: 22, left: 8 }

interface Bar {
  date: string
  total: number
  segments: { key: string; color: string; value: number }[]
}

const bars = computed<Bar[]>(() =>
  props.series.map((point) => {
    const dims = point.dimensions
    const rejected = REJECTED_KEYS.reduce((sum, k) => sum + (dims[k] ?? 0), 0)
    const values: Record<string, number> = {
      accepted: dims.accepted ?? 0,
      rejected,
      duplicate: dims.duplicate ?? 0,
      dropped_overload: dims.dropped_overload ?? 0,
    }
    const segments = SERIES.map((s) => ({ key: s.key, color: s.color, value: values[s.key] ?? 0 }))
    return { date: point.date, total: segments.reduce((sum, s) => sum + s.value, 0), segments }
  }),
)

const maxTotal = computed(() => Math.max(1, ...bars.value.map((b) => b.total)))

const plotW = WIDTH - PAD.left - PAD.right
const plotH = HEIGHT - PAD.top - PAD.bottom
const barW = computed(() => Math.min(48, (plotW / Math.max(1, bars.value.length)) * 0.7))

function x(i: number): number {
  const step = plotW / Math.max(1, bars.value.length)
  return PAD.left + step * i + (step - barW.value) / 2
}

/** Returns [y, height] for a segment, walking up from the baseline; 2px surface gap between fills. */
function rect(bar: Bar, index: number): { y: number; h: number } {
  let below = 0
  for (let i = 0; i < index; i++) below += bar.segments[i]?.value ?? 0
  const value = bar.segments[index]?.value ?? 0
  const h = (value / maxTotal.value) * plotH
  const y = PAD.top + plotH - ((below + value) / maxTotal.value) * plotH
  return { y, h: Math.max(0, h - (h > 2 ? 2 : 0)) }
}

const hover = ref<number | null>(null)
const hoveredBar = computed(() => (hover.value === null ? null : (bars.value[hover.value] ?? null)))

function fmt(n: number): string {
  return n.toLocaleString('en-US')
}

function shortDate(iso: string): string {
  return new Date(iso + 'T00:00:00Z').toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
}
</script>

<template>
  <div>
    <div class="relative">
      <svg
        :viewBox="`0 0 ${WIDTH} ${HEIGHT}`"
        class="w-full"
        role="img"
        :aria-label="t('analytics.quality.chartTitle')"
        @mouseleave="hover = null"
      >
        <line
          :x1="PAD.left"
          :x2="WIDTH - PAD.right"
          :y1="PAD.top + plotH"
          :y2="PAD.top + plotH"
          stroke="var(--color-neutral-200)"
          stroke-width="1"
        />
        <g v-for="(bar, i) in bars" :key="bar.date">
          <rect
            v-for="(seg, si) in bar.segments"
            :key="seg.key"
            :x="x(i)"
            :y="rect(bar, si).y"
            :width="barW"
            :height="rect(bar, si).h"
            :fill="seg.color"
            rx="1.5"
            :opacity="hover === null || hover === i ? 1 : 0.35"
          />
          <rect
            :x="x(i)"
            :y="PAD.top"
            :width="barW"
            :height="plotH"
            fill="transparent"
            @mousemove="hover = i"
          />
        </g>
      </svg>

      <div
        v-if="hover !== null && hoveredBar"
        class="pointer-events-none absolute top-2 rounded-[9px] border border-neutral-200 bg-white px-3 py-2 text-xs shadow-md"
        :style="{ left: `${(100 * (x(hover) + barW / 2)) / WIDTH}%` }"
      >
        <div class="mb-1 font-semibold text-neutral-900">{{ shortDate(hoveredBar.date) }}</div>
        <div
          v-for="seg in hoveredBar.segments"
          :key="seg.key"
          class="flex items-center gap-2 tabular-nums"
        >
          <span class="inline-block h-2 w-2 rounded-full" :style="{ background: seg.color }" />
          <span class="text-neutral-600">{{ t(`analytics.quality.dim.${seg.key}`) }}</span>
          <span class="ml-auto font-medium text-neutral-900">{{ fmt(seg.value) }}</span>
        </div>
      </div>
    </div>

    <div class="mt-2 flex flex-wrap justify-between gap-1 text-[10px] text-neutral-400">
      <span v-for="bar in bars" :key="bar.date">{{ shortDate(bar.date) }}</span>
    </div>

    <ul class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs">
      <li v-for="s in SERIES" :key="s.key" class="flex items-center gap-1.5">
        <span class="inline-block h-2.5 w-2.5 rounded-sm" :style="{ background: s.color }" />
        <span class="text-neutral-600">{{ t(`analytics.quality.dim.${s.key}`) }}</span>
      </li>
    </ul>
  </div>
</template>
