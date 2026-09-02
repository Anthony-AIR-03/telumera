<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import { resolveDateRange } from '@/lib/date-range'
import StateMessage from '@/components/StateMessage.vue'
import AnalyticsDateRangeControl from '@/components/analytics/AnalyticsDateRangeControl.vue'
import QualityChart from '@/components/analytics/QualityChart.vue'
import QualitySummaryCards from '@/components/analytics/QualitySummaryCards.vue'
import type { QualityResponse } from '@/lib/analytics-types'

interface SiteSummary {
  id: string
  name: string
  workspaceId: string
}

const route = useRoute()
const { t } = useI18n()
const siteId = computed(() => route.params.id as string)
const range = computed(() => resolveDateRange(route.query))

const site = ref<SiteSummary | null>(null)
const quality = ref<QualityResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const definitionsOpen = ref(false)

async function load() {
  loading.value = true
  error.value = null
  try {
    site.value = await apiClient.get<SiteSummary>(`/sites/${siteId.value}`)
    quality.value = await apiClient.get<QualityResponse>(
      `/sites/${siteId.value}/analytics/quality?from=${range.value.from}&to=${range.value.to}`,
    )
  } catch {
    error.value = t('analytics.quality.loadError')
  } finally {
    loading.value = false
  }
}

watch([siteId, range], load, { immediate: true })

const definitionEntries = computed(() => Object.entries(quality.value?.definitions ?? {}))
</script>

<template>
  <main>
    <RouterLink
      :to="{ name: 'site-analytics', params: { id: siteId }, query: route.query }"
      class="text-xs font-semibold text-neutral-400 hover:text-neutral-700"
    >
      {{ t('analytics.quality.backToAnalytics') }}
    </RouterLink>
    <h1 class="font-display mt-2 text-xl font-bold text-neutral-900">
      {{ t('analytics.quality.heading', { site: site?.name ?? siteId }) }}
    </h1>
    <p class="mt-1 max-w-2xl text-sm text-neutral-500">{{ t('analytics.quality.blurb') }}</p>

    <div class="mt-4">
      <AnalyticsDateRangeControl />
    </div>

    <StateMessage v-if="error" state="error" :message="error" class="mt-6" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-6" />

    <template v-else-if="quality">
      <QualitySummaryCards
        :quality="quality"
        class="mt-5"
        @open-glossary="definitionsOpen = true"
      />

      <div
        class="mt-4 rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
      >
        <h2 class="font-display text-sm font-bold text-neutral-900">
          {{ t('analytics.quality.chartTitle') }}
        </h2>
        <p v-if="!quality.series.length" class="mt-3 text-sm text-neutral-400">
          {{ t('analytics.quality.empty') }}
        </p>
        <QualityChart v-else :series="quality.series" class="mt-3" />
      </div>

      <details
        class="mt-4 rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
        :open="definitionsOpen"
      >
        <summary class="cursor-pointer text-sm font-bold text-neutral-900">
          {{ t('analytics.quality.definitionsTitle') }}
        </summary>
        <dl class="mt-3 space-y-2 text-sm">
          <div v-for="[key, text] in definitionEntries" :key="key">
            <dt class="font-mono text-xs font-semibold text-neutral-700">{{ key }}</dt>
            <dd class="text-neutral-600">{{ text }}</dd>
          </div>
        </dl>
      </details>
    </template>
  </main>
</template>
