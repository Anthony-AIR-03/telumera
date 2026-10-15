<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppBadge from '@/components/AppBadge.vue'
import StateMessage from '@/components/StateMessage.vue'
import type { AcquisitionMetric } from '@/lib/analytics-types'

const props = defineProps<{
  siteId: string
  from: string
  to: string
}>()

const { t } = useI18n()

const items = ref<AcquisitionMetric[] | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    items.value = await apiClient.get<AcquisitionMetric[]>(
      `/sites/${props.siteId}/analytics/acquisition?from=${props.from}&to=${props.to}`,
    )
  } catch {
    error.value = t('analytics.acquisition.loadError')
  } finally {
    loading.value = false
  }
}

watch(() => [props.siteId, props.from, props.to], load, { immediate: true })

/** Largest first, so the share bars read top-down and the Signal-marked first row is the biggest source. */
const rows = computed(() => [...(items.value ?? [])].sort((a, b) => b.sessions - a.sessions))
const maxSessions = computed(() => Math.max(1, ...rows.value.map((r) => r.sessions)))
</script>

<template>
  <section class="card">
    <header class="flex items-baseline justify-between gap-3 px-5 pt-4">
      <h2 class="font-display text-[17px] font-medium">{{ t('analytics.acquisition.title') }}</h2>
      <span class="font-mono text-xs text-muted">{{ t('analytics.acquisition.meta') }}</span>
    </header>

    <StateMessage v-if="error" state="error" :message="error" class="px-5 py-4" />
    <StateMessage
      v-else-if="loading"
      state="loading"
      :message="t('common.loading')"
      class="px-5 py-4"
    />
    <StateMessage
      v-else-if="items && items.length === 0"
      state="empty"
      :message="t('analytics.acquisition.empty')"
      class="px-5 py-4"
    />

    <div v-else-if="items" class="mt-2.5 overflow-x-auto">
      <table class="data-table">
        <thead>
          <tr>
            <th scope="col" class="pl-5">{{ t('analytics.acquisition.source') }}</th>
            <th scope="col">{{ t('analytics.acquisition.channel') }}</th>
            <th scope="col">{{ t('analytics.acquisition.campaign') }}</th>
            <th scope="col" class="num">{{ t('analytics.acquisition.sessions') }}</th>
            <th scope="col" class="num">{{ t('analytics.acquisition.visitors') }}</th>
            <th scope="col" class="num pr-5">{{ t('analytics.acquisition.engagedSessions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, i) in rows" :key="i">
            <td
              class="key min-w-[180px] pl-5"
              :style="{ '--w': (row.sessions / maxSessions).toFixed(3) }"
            >
              <span>{{ row.referrerHost ?? t('analytics.acquisition.direct') }}</span>
            </td>
            <td>
              <AppBadge>{{ row.channel }}</AppBadge>
            </td>
            <td class="font-mono text-xs text-muted">
              {{
                [row.utmSource, row.utmMedium, row.utmCampaign].filter(Boolean).join(' / ') || '—'
              }}
            </td>
            <td class="num">{{ row.sessions.toLocaleString('en-US') }}</td>
            <td class="num">
              <AppBadge v-if="row.isBelowPrivacyFloor" class="mr-1.5">{{
                t('analytics.privacyFloor')
              }}</AppBadge>
              {{ row.visitors.toLocaleString('en-US') }}
            </td>
            <td class="num pr-5">{{ row.engagedSessions.toLocaleString('en-US') }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
