<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TimeSeriesPoint } from '@/lib/analytics-types'

const props = defineProps<{
  points: TimeSeriesPoint[]
}>()

const { t } = useI18n()

/**
 * Palette validated with dataviz's scripts/validate_palette.js against this app's own tokens
 * (main.css's @theme) — brand-700/brand-500 pass the lightness band; brand-500 vs. neutral-500
 * lands in the CVD 6-8 "floor" band, legal only with secondary encoding, which the dashed Sessions
 * stroke plus the always-visible legend/tooltip text labels both provide. brand-500 alone also
 * WARNs on contrast-vs-surface (2.38:1) — resolved the same way (visible labels, never color-alone).
 */
const SERIES = [
  {
    key: 'views' as const,
    label: () => t('analytics.chart.views'),
    color: '#008055',
    dashed: false,
  },
  {
    key: 'visitors' as const,
    label: () => t('analytics.chart.visitors'),
    color: '#00BD7E',
    dashed: false,
  },
  {
    key: 'sessions' as const,
    label: () => t('analytics.chart.sessions'),
    color: '#838F88',
    dashed: true,
  },
]

const WIDTH = 1000
const HEIGHT = 240
const PAD_LEFT = 46
const PAD_RIGHT = 12
const PAD_TOP = 16
const PAD_BOTTOM = 28
const INNER_WIDTH = WIDTH - PAD_LEFT - PAD_RIGHT

/** Rounds up to a "nice" 1/2/5 x 10^n step so axis ticks land on clean numbers, not arbitrary values. */
function niceStep(roughStep: number): number {
  if (roughStep <= 0) return 1
  const magnitude = 10 ** Math.floor(Math.log10(roughStep))
  const normalized = roughStep / magnitude
  const step = normalized < 1.5 ? 1 : normalized < 3 ? 2 : normalized < 7 ? 5 : 10
  return step * magnitude
}

/** Catmull-Rom through every point, converted to cubic Beziers — smooth without overshooting local extrema. */
function smoothPath(points: { x: number; y: number }[]): string {
  if (points.length < 2) return ''
  let d = `M ${points[0]!.x} ${points[0]!.y}`
  for (let i = 0; i < points.length - 1; i++) {
    const p0 = points[i - 1] ?? points[i]!
    const p1 = points[i]!
    const p2 = points[i + 1]!
    const p3 = points[i + 2] ?? p2
    const c1x = p1.x + (p2.x - p0.x) / 6
    const c1y = p1.y + (p2.y - p0.y) / 6
    const c2x = p2.x - (p3.x - p1.x) / 6
    const c2y = p2.y - (p3.y - p1.y) / 6
    d += ` C ${c1x} ${c1y}, ${c2x} ${c2y}, ${p2.x} ${p2.y}`
  }
  return d
}

const chart = computed(() => {
  if (props.points.length === 0) return null

  const rawMax = Math.max(...props.points.flatMap((p) => [p.views, p.visitors, p.sessions]), 1)
  const step = niceStep(rawMax / 4)
  const niceMax = Math.ceil((rawMax * 1.08) / step) * step
  const gridSteps = niceMax / step

  const usableHeight = HEIGHT - PAD_TOP - PAD_BOTTOM
  const toY = (value: number) => PAD_TOP + usableHeight - (value / niceMax) * usableHeight
  const toX = (index: number) =>
    PAD_LEFT + (props.points.length === 1 ? 0 : (index / (props.points.length - 1)) * INNER_WIDTH)

  const series = SERIES.map((s) => {
    const pts = props.points.map((p, i) => ({ x: toX(i), y: toY(p[s.key]) }))
    return { ...s, pts, path: smoothPath(pts) }
  })

  const gridlines: { y: number; label: string }[] = []
  for (let i = 0; i <= gridSteps; i++) {
    gridlines.push({
      y: PAD_TOP + (usableHeight / gridSteps) * i,
      label: String(Math.round(niceMax - step * i)),
    })
  }

  const dateStep = Math.max(1, Math.round(props.points.length / 6))
  const xLabels = props.points
    .map((p, i) => ({ x: toX(i), label: formatShortDate(p.bucket) }))
    .filter((_, i) => i % dateStep === 0 || i === props.points.length - 1)

  return { series, gridlines, xLabels, baselineY: PAD_TOP + usableHeight }
})

function formatShortDate(bucket: string): string {
  const date = new Date(bucket)
  return Number.isNaN(date.getTime())
    ? bucket
    : date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}

const hoverIndex = ref<number | null>(null)
const svgRef = ref<SVGSVGElement | null>(null)

function onHover(event: PointerEvent) {
  if (!svgRef.value || !chart.value || props.points.length === 0) return
  const rect = svgRef.value.getBoundingClientRect()
  const scaleX = WIDTH / rect.width
  const mouseX = (event.clientX - rect.left) * scaleX
  const ratio = (mouseX - PAD_LEFT) / INNER_WIDTH
  const idx = Math.round(ratio * (props.points.length - 1))
  hoverIndex.value = Math.max(0, Math.min(props.points.length - 1, idx))
}

const hoverPoint = computed(() =>
  hoverIndex.value !== null ? props.points[hoverIndex.value] : null,
)
const hoverX = computed(() =>
  hoverIndex.value !== null && chart.value ? chart.value.series[0]!.pts[hoverIndex.value]!.x : 0,
)
</script>

<template>
  <div>
    <div class="flex items-center gap-4 text-xs font-semibold text-neutral-600">
      <span v-for="s in SERIES" :key="s.key" class="inline-flex items-center gap-1.5">
        <span
          class="inline-block h-2.5 w-2.5 rounded-sm"
          :style="{
            background: s.color,
            ...(s.dashed
              ? {
                  backgroundImage:
                    'repeating-linear-gradient(90deg, transparent, transparent 1px, white 1px, white 2px)',
                }
              : {}),
          }"
        />
        {{ s.label() }}
      </span>
    </div>

    <div class="relative mt-2">
      <svg
        ref="svgRef"
        :viewBox="`0 0 ${WIDTH} ${HEIGHT}`"
        class="block w-full"
        style="height: 240px"
        @pointermove="onHover"
        @pointerleave="hoverIndex = null"
      >
        <template v-if="chart">
          <g>
            <line
              v-for="g in chart.gridlines"
              :key="g.y"
              :x1="PAD_LEFT"
              :y1="g.y"
              :x2="WIDTH - PAD_RIGHT"
              :y2="g.y"
              stroke="#E7E9E7"
              stroke-width="1"
            />
          </g>
          <line
            :x1="PAD_LEFT"
            :y1="chart.baselineY"
            :x2="WIDTH - PAD_RIGHT"
            :y2="chart.baselineY"
            stroke="#c3c2b7"
            stroke-width="1"
          />

          <path
            v-for="s in chart.series"
            :key="s.key"
            :d="s.path"
            fill="none"
            :stroke="s.color"
            stroke-width="2.2"
            :stroke-dasharray="s.dashed ? '5 4' : undefined"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
          <circle
            v-for="s in chart.series"
            :key="`${s.key}-end`"
            :cx="s.pts.at(-1)!.x"
            :cy="s.pts.at(-1)!.y"
            r="3.2"
            :fill="s.color"
          />

          <g v-for="g in chart.gridlines" :key="`label-${g.y}`">
            <text
              :x="PAD_LEFT - 8"
              :y="g.y + 4"
              text-anchor="end"
              font-size="10.5"
              fill="#838F88"
              font-family="JetBrains Mono, monospace"
            >
              {{ g.label }}
            </text>
          </g>
          <g v-for="xl in chart.xLabels" :key="xl.x">
            <text
              :x="xl.x"
              :y="HEIGHT - 6"
              text-anchor="middle"
              font-size="10.5"
              fill="#838F88"
              font-family="JetBrains Mono, monospace"
            >
              {{ xl.label }}
            </text>
          </g>

          <line
            v-if="hoverIndex !== null"
            :x1="hoverX"
            :y1="PAD_TOP"
            :x2="hoverX"
            :y2="chart.baselineY"
            stroke="#838F88"
            stroke-width="1"
            stroke-dasharray="3,3"
          />
          <rect
            :x="PAD_LEFT"
            :y="PAD_TOP"
            :width="INNER_WIDTH"
            :height="HEIGHT - PAD_TOP - PAD_BOTTOM"
            fill="transparent"
          />
        </template>
      </svg>

      <div
        v-if="hoverPoint"
        class="pointer-events-none absolute z-10 rounded-lg bg-neutral-900 px-2.5 py-2 text-xs text-white shadow-lg"
        :style="{ left: `${(hoverX / WIDTH) * 100}%`, top: '8px', transform: 'translateX(12px)' }"
      >
        <div class="font-semibold">{{ formatShortDate(hoverPoint.bucket) }}</div>
        <div v-for="s in SERIES" :key="s.key" class="mt-0.5 flex items-center gap-1.5">
          <span class="inline-block h-1.5 w-1.5 rounded-full" :style="{ background: s.color }" />
          {{ s.label() }}: {{ hoverPoint[s.key].toLocaleString('en-US') }}
        </div>
      </div>
    </div>
  </div>
</template>
