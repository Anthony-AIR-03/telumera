<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TimeSeriesPoint } from '@/lib/analytics-types'

/**
 * The brand chart (docs/brand/README.md §8): one series, the KPI selected in the strip above, as a
 * 2px Signal line over a 12% Signal area; the previous period of equal length as a dashed
 * comparison line; mono axis labels; square point markers. One shared linear y-axis from 0, never
 * truncated. Straight segments, not smoothed: a smoothed line implies values between days that
 * were never measured.
 *
 * Drawn in real pixels (the SVG's viewBox follows the container's measured width) rather than a
 * fixed viewBox scaled to fit, so axis labels stay 11px on a phone instead of shrinking.
 */
const props = defineProps<{
  points: TimeSeriesPoint[]
  previous: TimeSeriesPoint[] | null
  metric: 'visitors' | 'sessions' | 'views'
  metricLabel: string
}>()

const { t } = useI18n()

const hostRef = ref<HTMLElement | null>(null)
const width = ref(800)
let observer: ResizeObserver | null = null
onMounted(() => {
  if (!hostRef.value) return
  observer = new ResizeObserver(([entry]) => {
    if (entry) width.value = Math.max(280, Math.round(entry.contentRect.width))
  })
  observer.observe(hostRef.value)
})
onBeforeUnmount(() => observer?.disconnect())

const narrow = computed(() => width.value < 500)
const height = computed(() => (narrow.value ? 200 : 260))
const pad = computed(() => ({ l: narrow.value ? 36 : 48, r: 20, t: 20, b: 30 }))

/** Rounds up to a "nice" 1/2/5 x 10^n step so axis ticks land on clean numbers. */
function niceStep(roughStep: number): number {
  if (roughStep <= 0) return 1
  const magnitude = 10 ** Math.floor(Math.log10(roughStep))
  const normalized = roughStep / magnitude
  const step = normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10
  return step * magnitude
}

function formatShortDate(bucket: string): string {
  const date = new Date(bucket)
  return Number.isNaN(date.getTime())
    ? bucket
    : date.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' })
}

const chart = computed(() => {
  const cur = props.points.map((p) => p[props.metric])
  if (cur.length === 0) return null
  const prev = (props.previous ?? []).slice(0, cur.length).map((p) => p[props.metric])

  const rawMax = Math.max(1, ...cur, ...prev)
  const step = niceStep(rawMax / 4)
  const max = Math.ceil(rawMax / step) * step
  const { l, r, t: top, b } = pad.value
  const w = width.value
  const h = height.value
  const plotW = w - l - r
  const x = (i: number) => l + (cur.length === 1 ? plotW / 2 : (i * plotW) / (cur.length - 1))
  const y = (v: number) => top + (1 - v / max) * (h - top - b)
  const line = (values: number[]) =>
    values.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)} ${y(v).toFixed(1)}`).join('')

  const ticks: { y: number; label: string }[] = []
  for (let v = 0; v <= max; v += step) ticks.push({ y: y(v), label: v.toLocaleString('en-US') })

  // Every Nth day plus the last one, skipping a regular label that would crowd the last.
  const last = cur.length - 1
  const labelEvery = Math.max(1, Math.ceil(cur.length / (narrow.value ? 4 : 8)))
  const xLabels = props.points
    .map((p, i) => ({ x: x(i), label: formatShortDate(p.bucket), i }))
    .filter(({ i }) => i === last || (i % labelEvery === 0 && last - i >= labelEvery / 2))

  const curLine = line(cur)
  return {
    cur,
    prev,
    x,
    y,
    ticks,
    xLabels,
    baseline: y(0),
    plotLeft: l,
    plotRight: w - r,
    top,
    curLine,
    area: `${curLine}L${x(cur.length - 1).toFixed(1)} ${y(0)}L${x(0).toFixed(1)} ${y(0)}Z`,
    prevLine: prev.length > 1 ? line(prev) : null,
  }
})

const hoverIndex = ref<number | null>(null)
function onHover(event: PointerEvent) {
  const c = chart.value
  if (!c || !hostRef.value) return
  const rect = hostRef.value.getBoundingClientRect()
  const ratio = (event.clientX - rect.left - c.plotLeft) / (c.plotRight - c.plotLeft)
  const idx = Math.round(ratio * (c.cur.length - 1))
  hoverIndex.value = Math.max(0, Math.min(c.cur.length - 1, idx))
}

const hover = computed(() => {
  const c = chart.value
  const i = hoverIndex.value
  if (!c || i === null) return null
  return {
    x: c.x(i),
    y: c.y(c.cur[i]!),
    date: formatShortDate(props.points[i]!.bucket),
    value: c.cur[i]!,
    prevDate: props.previous?.[i] ? formatShortDate(props.previous[i]!.bucket) : null,
    prevValue: c.prev[i] ?? null,
    flip: c.x(i) > width.value * 0.65,
  }
})
</script>

<template>
  <div ref="hostRef" class="relative">
    <svg
      v-if="chart"
      :viewBox="`0 0 ${width} ${height}`"
      :width="width"
      :height="height"
      class="block w-full"
      role="img"
      :aria-label="t('analytics.chart.aria', { metric: metricLabel, points: points.length })"
      @pointermove="onHover"
      @pointerleave="hoverIndex = null"
    >
      <g font-family="Red Hat Mono, monospace" font-size="11" fill="var(--color-muted)">
        <template v-for="tick in chart.ticks" :key="tick.y">
          <line
            :x1="chart.plotLeft"
            :x2="chart.plotRight"
            :y1="tick.y"
            :y2="tick.y"
            stroke="var(--color-line-2)"
          />
          <text :x="chart.plotLeft - 8" :y="tick.y + 4" text-anchor="end">{{ tick.label }}</text>
        </template>
        <text
          v-for="xl in chart.xLabels"
          :key="xl.i"
          :x="xl.x"
          :y="height - 8"
          :text-anchor="xl.i === chart.cur.length - 1 && chart.cur.length > 1 ? 'end' : 'middle'"
        >
          {{ xl.label }}
        </text>
      </g>

      <path :d="chart.area" fill="var(--color-signal-soft)" />
      <path
        v-if="chart.prevLine"
        :d="chart.prevLine"
        fill="none"
        stroke="var(--color-comp)"
        stroke-width="1.5"
        stroke-dasharray="4 3"
      />
      <path
        :d="chart.curLine"
        fill="none"
        stroke="var(--color-signal)"
        stroke-width="2"
        stroke-linejoin="round"
      />
      <rect
        :x="chart.x(chart.cur.length - 1) - 4"
        :y="chart.y(chart.cur.at(-1)!) - 4"
        width="8"
        height="8"
        fill="var(--color-signal)"
      />

      <g v-if="hover">
        <line
          :x1="hover.x"
          :x2="hover.x"
          :y1="chart.top"
          :y2="chart.baseline"
          stroke="var(--color-fg-2)"
          stroke-dasharray="2 3"
        />
        <rect
          :x="hover.x - 4"
          :y="hover.y - 4"
          width="8"
          height="8"
          fill="var(--color-raised)"
          stroke="var(--color-signal)"
          stroke-width="2"
        />
      </g>
    </svg>

    <div
      v-if="hover"
      class="pointer-events-none absolute top-2 z-10 rounded-[3px] bg-fg px-2.5 py-2 font-mono text-xs whitespace-nowrap text-page"
      :style="{
        left: `${hover.x}px`,
        transform: hover.flip ? 'translateX(calc(-100% - 12px))' : 'translateX(12px)',
      }"
    >
      <div>
        <span class="mr-1.5 inline-block h-2 w-2 bg-signal" aria-hidden="true" />{{ hover.date }} ·
        <b class="font-medium">{{ hover.value.toLocaleString('en-US') }}</b>
      </div>
      <div v-if="hover.prevValue !== null && hover.prevDate" class="mt-0.5 opacity-70">
        <span class="mr-1.5 inline-block h-2 w-2 border border-current" aria-hidden="true" />{{
          hover.prevDate
        }}
        · {{ hover.prevValue.toLocaleString('en-US') }}
      </div>
    </div>
  </div>
</template>
