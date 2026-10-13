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

/**
 * Sqrt scale (the choropleth convention: linear over-weights a few huge values) on a neutral ramp
 * from the share-bar tone to Graphite 600, mixed in CSS so it follows the theme. Signal is a marker,
 * not a fill: only the single top country takes it, the same way the tables mark their top row.
 */
function fillFor(iso: string): string {
  const row = byIso.value.get(iso)
  if (!row || row.visitors <= 0 || maxVisitors.value === 0) return 'var(--color-line-2)'
  if (row.visitors === maxVisitors.value) return 'var(--color-signal)'
  const pct = Math.round(15 + 85 * (Math.sqrt(row.visitors) / Math.sqrt(maxVisitors.value)))
  return `color-mix(in srgb, var(--color-fg-2) ${pct}%, var(--color-bar))`
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
  <section class="card">
    <header class="flex items-baseline justify-between gap-3 px-5 pt-4">
      <h2 class="font-display text-[17px] font-medium">{{ t('analytics.geography.mapTitle') }}</h2>
      <span v-if="hasRealData" class="font-mono text-xs text-muted">
        {{ t('analytics.geography.mapLegend') }}
      </span>
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

    <div v-else-if="rows" class="relative px-5 pt-3 pb-5">
      <svg
        viewBox="0 0 1000 500"
        class="w-full"
        role="img"
        :aria-label="t('analytics.geography.mapTitle')"
        @mouseleave="hovered = null"
      >
        <!-- Fill set via style, not the attribute: color-mix() isn't valid presentation-attribute syntax everywhere. -->
        <path
          v-for="iso in isoList"
          :key="iso"
          :d="paths[iso]"
          :style="{ fill: fillFor(iso) }"
          stroke="var(--color-raised)"
          stroke-width="0.6"
          class="transition-[fill] duration-150"
          @mousemove="onMove(iso, $event)"
        >
          <title>{{ countryName(iso) }}</title>
        </path>
      </svg>

      <div
        v-if="hovered && hoveredRow"
        class="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full rounded-[3px] bg-fg px-2.5 py-1.5 font-mono text-xs whitespace-nowrap text-page"
        :style="{ left: `${hovered.x}px`, top: `${hovered.y - 8}px` }"
      >
        <div class="font-sans font-semibold">{{ countryName(hovered.iso) }}</div>
        <div class="tabular-nums">
          {{ hoveredRow.visitors.toLocaleString('en-US') }}
          {{ t('analytics.geography.visitors').toLowerCase() }} ·
          {{ hoveredRow.sessions.toLocaleString('en-US') }}
          {{ t('analytics.geography.sessions').toLowerCase() }}
        </div>
        <div v-if="hoveredRow.isBelowPrivacyFloor" class="mt-0.5 opacity-70">
          {{ t('analytics.geography.belowFloorHint') }}
        </div>
      </div>

      <ul
        v-if="ranked.length"
        class="mt-3 flex flex-wrap gap-x-5 gap-y-1 font-mono text-xs text-muted"
      >
        <li
          v-for="r in ranked"
          :key="r.country ?? ''"
          class="flex items-center gap-1.5 tabular-nums"
        >
          <i
            class="block h-2 w-2"
            :style="{ background: fillFor((r.country ?? '').toUpperCase()) }"
            aria-hidden="true"
          />
          <span class="font-sans font-medium text-fg">{{ countryName(r.country ?? '') }}</span>
          {{ r.visitors.toLocaleString('en-US') }}
        </li>
      </ul>
    </div>
  </section>
</template>
