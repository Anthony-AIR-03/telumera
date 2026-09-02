<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import StateMessage from '@/components/StateMessage.vue'
import countryPaths from '@/assets/world-countries.json'
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

const paths = countryPaths as Record<string, string>
const isoList = Object.keys(paths)

const regionNames =
  typeof Intl !== 'undefined' && 'DisplayNames' in Intl
    ? new Intl.DisplayNames(['en'], { type: 'region' })
    : null

function countryName(iso: string): string {
  try {
    return regionNames?.of(iso) ?? iso
  } catch {
    return iso
  }
}

/** No GeoIP provider configured yet, or a site with no located events — every country is null. */
const hasRealData = computed(
  () => rows.value !== null && rows.value.some((r) => r.country !== null),
)

const byIso = computed(() => {
  const map = new Map<string, GeographyMetric>()
  for (const r of rows.value ?? []) {
    if (r.country) map.set(r.country.toUpperCase(), r)
  }
  return map
})

const maxVisitors = computed(() => {
  let max = 0
  for (const r of byIso.value.values()) if (r.visitors > max) max = r.visitors
  return max
})

/** Sqrt scale — the choropleth convention: linear over-weights a few huge values. 0 → lightest. */
function fillFor(iso: string): string {
  const row = byIso.value.get(iso)
  if (!row || row.visitors <= 0 || maxVisitors.value === 0) return 'var(--color-neutral-100)'
  const tRatio = Math.sqrt(row.visitors) / Math.sqrt(maxVisitors.value)
  return mixBrand(tRatio)
}

/** Interpolate brand-50 → brand-800 in sRGB. Kept inline — no color lib, matching TrafficChart. */
const LO = [230, 251, 243] as const
const HI = [0, 102, 66] as const
function mixBrand(ratio: number): string {
  const r = Math.round(LO[0] + (HI[0] - LO[0]) * ratio)
  const g = Math.round(LO[1] + (HI[1] - LO[1]) * ratio)
  const b = Math.round(LO[2] + (HI[2] - LO[2]) * ratio)
  return `rgb(${r} ${g} ${b})`
}

const hovered = ref<{ iso: string; x: number; y: number } | null>(null)

function onMove(iso: string, ev: MouseEvent) {
  const host = (ev.currentTarget as SVGElement).ownerSVGElement?.parentElement
  const rect = host?.getBoundingClientRect()
  hovered.value = {
    iso,
    x: ev.clientX - (rect?.left ?? 0),
    y: ev.clientY - (rect?.top ?? 0),
  }
}

const hoveredRow = computed(() =>
  hovered.value ? (byIso.value.get(hovered.value.iso) ?? null) : null,
)

const ranked = computed(() =>
  [...byIso.value.values()].sort((a, b) => b.visitors - a.visitors).slice(0, 5),
)
</script>

<template>
  <div
    class="rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
  >
    <div class="mb-3 flex items-baseline justify-between">
      <h3 class="font-display text-sm font-bold text-neutral-900">
        {{ t('analytics.geography.mapTitle') }}
      </h3>
      <span v-if="hasRealData" class="text-xs text-neutral-500">
        {{ t('analytics.geography.mapLegend') }}
      </span>
    </div>

    <StateMessage v-if="error" state="error" :message="error" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" />

    <div
      v-else-if="rows && !hasRealData"
      class="flex flex-col items-center gap-1.5 px-5 py-11 text-center text-neutral-400"
    >
      <div class="text-sm font-bold text-neutral-600">
        {{ t('analytics.geography.notAvailable') }}
      </div>
      <p class="max-w-sm text-xs">{{ t('analytics.geography.notAvailableDetail') }}</p>
    </div>

    <div v-else-if="rows" class="relative">
      <svg
        viewBox="0 0 1000 500"
        class="w-full"
        role="img"
        :aria-label="t('analytics.geography.mapTitle')"
        @mouseleave="hovered = null"
      >
        <rect x="0" y="0" width="1000" height="500" fill="var(--color-neutral-50)" />
        <path
          v-for="iso in isoList"
          :key="iso"
          :d="paths[iso]"
          :fill="fillFor(iso)"
          stroke="var(--color-neutral-200)"
          stroke-width="0.5"
          class="transition-[fill] duration-150"
          @mousemove="onMove(iso, $event)"
        >
          <title>{{ countryName(iso) }}</title>
        </path>
      </svg>

      <div
        v-if="hovered && hoveredRow"
        class="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full rounded-[9px] border border-neutral-200 bg-white px-2.5 py-1.5 text-xs shadow-md"
        :style="{ left: `${hovered.x}px`, top: `${hovered.y - 8}px` }"
      >
        <div class="font-semibold text-neutral-900">{{ countryName(hovered.iso) }}</div>
        <div class="tabular-nums text-neutral-600">
          {{ hoveredRow.visitors.toLocaleString('en-US') }}
          {{ t('analytics.geography.visitors').toLowerCase() }} ·
          {{ hoveredRow.sessions.toLocaleString('en-US') }}
          {{ t('analytics.geography.sessions').toLowerCase() }}
        </div>
        <div v-if="hoveredRow.isBelowPrivacyFloor" class="mt-0.5 text-neutral-400">
          {{ t('analytics.geography.belowFloorHint') }}
        </div>
      </div>

      <ul v-if="ranked.length" class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-600">
        <li v-for="r in ranked" :key="r.country ?? ''" class="tabular-nums">
          <span class="font-semibold text-neutral-900">{{ countryName(r.country ?? '') }}</span>
          {{ r.visitors.toLocaleString('en-US') }}
        </li>
      </ul>
    </div>
  </div>
</template>
