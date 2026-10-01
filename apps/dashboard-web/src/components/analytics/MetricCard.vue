<script setup lang="ts">
import { useI18n } from 'vue-i18n'

/** Purely presentational — formatting and favorable/unfavorable direction are decided by the caller, which has the actual OverviewMetrics numbers. */
defineProps<{
  label: string
  value: string
  delta?: { text: string; favorable: boolean } | null
}>()

defineEmits<{
  'open-glossary': []
}>()

const { t } = useI18n()
</script>

<template>
  <div
    class="rounded-[14px] border border-neutral-200 bg-white p-4 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
  >
    <div class="flex items-center justify-between">
      <span class="text-xs font-semibold text-neutral-600">{{ label }}</span>
      <button
        type="button"
        class="pointer-coarse:-m-[15px] pointer-coarse:p-[15px] text-neutral-400 hover:text-neutral-600"
        :aria-label="t('analytics.glossary.openFor', { label })"
        @click="$emit('open-glossary')"
      >
        <svg width="14" height="14" viewBox="0 0 20 20" fill="none" aria-hidden="true">
          <circle cx="10" cy="10" r="7.3" stroke="currentColor" stroke-width="1.4" />
          <path d="M10 9.2v4.4" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" />
          <circle cx="10" cy="6.7" r="0.9" fill="currentColor" />
        </svg>
      </button>
    </div>
    <div class="font-display mt-2 text-2xl font-bold text-neutral-900">{{ value }}</div>
    <div
      v-if="delta"
      class="mt-1 flex items-center gap-1 text-xs font-semibold"
      :class="delta.favorable ? 'text-brand-700' : 'text-neutral-500'"
    >
      <svg
        v-if="delta.favorable"
        width="11"
        height="11"
        viewBox="0 0 20 20"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M10 15V5M5 9l5-5 5 5"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
      <svg v-else width="11" height="11" viewBox="0 0 20 20" fill="none" aria-hidden="true">
        <path
          d="M10 5v10M5 11l5 5 5-5"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
      {{ delta.text }}
    </div>
  </div>
</template>
