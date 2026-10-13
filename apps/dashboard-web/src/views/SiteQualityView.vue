<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import { resolveDateRange } from '@/lib/date-range'
import PageHeader from '@/components/PageHeader.vue'
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
  <div>
    <PageHeader
      :title="t('analytics.quality.title')"
      :crumbs="[
        { label: site?.name ?? '…', to: `/sites/${siteId}` },
        {
          label: t('nav.analytics'),
          to: { name: 'site-analytics', params: { id: siteId }, query: route.query },
        },
        { label: t('analytics.quality.link') },
      ]"
    >
      <template #tools>
        <AnalyticsDateRangeControl />
      </template>
    </PageHeader>
    <p class="mt-3 max-w-2xl text-sm text-fg-2">{{ t('analytics.quality.blurb') }}</p>

    <StateMessage v-if="error" state="error" :message="error" class="mt-6" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" class="mt-6" />

    <template v-else-if="quality">
      <QualitySummaryCards
        :quality="quality"
        class="mt-5"
        @open-glossary="definitionsOpen = true"
      />

      <section class="card mt-4">
        <header class="px-5 pt-4">
          <h2 class="font-display text-[17px] font-medium">
            {{ t('analytics.quality.chartTitle') }}
          </h2>
        </header>
        <p v-if="!quality.series.length" class="px-5 pt-3 pb-5 text-sm text-muted">
          {{ t('analytics.quality.empty') }}
        </p>
        <QualityChart v-else :series="quality.series" class="px-5 pt-3 pb-5" />
      </section>

      <details class="card group mt-4" :open="definitionsOpen">
        <summary
          class="flex cursor-pointer list-none items-center justify-between px-5 py-4 font-display text-[17px] font-medium"
        >
          {{ t('analytics.quality.definitionsTitle') }}
          <svg
            width="12"
            height="12"
            viewBox="0 0 12 12"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
            class="text-muted transition-transform group-open:rotate-180"
            aria-hidden="true"
          >
            <path d="M3 4.5L6 7.5L9 4.5" />
          </svg>
        </summary>
        <dl class="grid gap-3 border-t border-line-2 px-5 py-4 text-sm">
          <div v-for="[key, text] in definitionEntries" :key="key">
            <dt class="font-mono text-xs font-medium">{{ key }}</dt>
            <dd class="mt-0.5 text-fg-2">{{ text }}</dd>
          </div>
        </dl>
      </details>
    </template>
  </div>
</template>
