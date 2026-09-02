<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import MetricCard from '@/components/analytics/MetricCard.vue'
import type { QualityResponse } from '@/lib/analytics-types'

const props = defineProps<{ quality: QualityResponse }>()

const emit = defineEmits<{ 'open-glossary': [] }>()

const { t } = useI18n()

function num(key: string): number {
  return props.quality.totals[key] ?? 0
}

const rejectedTotal = computed(
  () =>
    num('rejected_validation') +
    num('rejected_unknown_token') +
    num('rejected_origin') +
    num('rejected_module_disabled'),
)

/** Everything that reached the collector: accepted + rejected + duplicate + dropped. */
const arrived = computed(
  () => num('accepted') + rejectedTotal.value + num('duplicate') + num('dropped_overload'),
)

function pct(n: number, d: number): string {
  return d === 0 ? '—' : `${((100 * n) / d).toFixed(1)}%`
}

const cards = computed(() => [
  { label: t('analytics.quality.card.acceptanceRate'), value: pct(num('accepted'), arrived.value) },
  {
    label: t('analytics.quality.card.rejectionRate'),
    value: pct(rejectedTotal.value, arrived.value),
  },
  {
    label: t('analytics.quality.card.duplicateRate'),
    value: pct(num('duplicate'), arrived.value),
  },
  {
    label: t('analytics.quality.card.botShare'),
    value: pct(num('bot'), num('accepted')),
  },
  {
    label: t('analytics.quality.card.delayed'),
    value: num('delayed').toLocaleString('en-US'),
  },
  {
    label: t('analytics.quality.card.deadLetter'),
    value: props.quality.deadLetterQueueDepth.toLocaleString('en-US'),
  },
])
</script>

<template>
  <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
    <MetricCard
      v-for="card in cards"
      :key="card.label"
      :label="card.label"
      :value="card.value"
      @open-glossary="emit('open-glossary')"
    />
  </div>
</template>
