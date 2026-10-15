<script setup lang="ts">
import { useI18n } from 'vue-i18n'

/**
 * One KPI tile, meant to sit in a strip (`card grid gap-px bg-line-2`, which draws the hairlines
 * between tiles at any wrap). Purely presentational — formatting and favorable/unfavorable direction
 * are decided by the caller, which has the actual OverviewMetrics numbers.
 *
 * `selectable` tiles drive the chart: a full-tile button (not the tile itself, so the glossary
 * button inside stays a separate, valid control) and a 2px Signal top bar when `selected`.
 */
withDefaults(
  defineProps<{
    label: string
    value: string
    delta?: { percent: string; favorable: boolean; up: boolean } | null
    selectable?: boolean
    selected?: boolean
  }>(),
  { delta: null, selectable: false, selected: false },
)

defineEmits<{
  'open-glossary': []
  select: []
}>()

const { t } = useI18n()
</script>

<template>
  <div class="relative grid content-start gap-2 bg-raised px-5 pt-[18px] pb-4">
    <span v-if="selected" class="absolute inset-x-0 top-0 h-0.5 bg-signal" aria-hidden="true" />
    <button
      v-if="selectable"
      type="button"
      class="absolute inset-0 hover:bg-surface"
      :aria-pressed="selected"
      :aria-label="t('analytics.overview.showInChart', { label })"
      @click="$emit('select')"
    />
    <div class="pointer-events-none relative flex items-center justify-between gap-2">
      <span class="label-mono">{{ label }}</span>
      <button
        type="button"
        class="pointer-events-auto -m-1.5 p-1.5 text-muted hover:text-fg pointer-coarse:-m-[13px] pointer-coarse:p-[13px]"
        :aria-label="t('analytics.glossary.openFor', { label })"
        @click="$emit('open-glossary')"
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
          aria-hidden="true"
        >
          <path d="M4 4H20V20H4Z" />
          <path d="M12 11V17M12 7V9" />
        </svg>
      </button>
    </div>
    <div
      class="pointer-events-none relative font-display text-[26px] leading-none font-medium tabular-nums sm:text-[32px]"
    >
      {{ value }}
    </div>
    <div
      v-if="delta"
      class="pointer-events-none relative font-mono text-[11px] font-medium text-muted"
    >
      <span :class="delta.favorable ? 'text-ok' : 'text-bad'"
        ><span aria-hidden="true" class="mr-0.5">{{ delta.up ? '▲' : '▼' }}</span
        >{{ delta.percent }}</span
      >
      {{ t('analytics.overview.vsPrior') }}
    </div>
  </div>
</template>
