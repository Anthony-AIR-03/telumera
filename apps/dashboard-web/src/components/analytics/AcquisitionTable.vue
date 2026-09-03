<script setup lang="ts">
import { ref, watch } from 'vue'
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
</script>

<template>
  <div
    class="rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
  >
    <StateMessage v-if="error" state="error" :message="error" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" />
    <StateMessage
      v-else-if="items && items.length === 0"
      state="empty"
      :message="t('analytics.acquisition.empty')"
    />

    <div v-else-if="items" class="overflow-x-auto">
      <table class="w-full border-collapse text-sm">
        <thead>
          <tr class="text-xs font-semibold text-neutral-500">
            <th scope="col" class="px-2 pb-2.5 text-left">
              {{ t('analytics.acquisition.channel') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-left">
              {{ t('analytics.acquisition.source') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-left">
              {{ t('analytics.acquisition.campaign') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-right">
              {{ t('analytics.acquisition.sessions') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-right">
              {{ t('analytics.acquisition.visitors') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-right">
              {{ t('analytics.acquisition.engagedSessions') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, i) in items" :key="i" class="border-t border-neutral-200">
            <td class="px-2 py-2.5">
              <AppBadge>{{ row.channel }}</AppBadge>
            </td>
            <td class="px-2 py-2.5 text-xs text-neutral-500">
              {{ row.referrerHost ?? '—' }}
            </td>
            <td class="px-2 py-2.5 text-xs text-neutral-500">
              {{
                [row.utmSource, row.utmMedium, row.utmCampaign].filter(Boolean).join(' / ') || '—'
              }}
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ row.sessions.toLocaleString('en-US') }}
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ row.visitors.toLocaleString('en-US') }}
              <AppBadge v-if="row.isBelowPrivacyFloor" tone="neutral" class="ml-1">{{
                t('analytics.privacyFloor')
              }}</AppBadge>
            </td>
            <td class="px-2 py-2.5 text-right tabular-nums">
              {{ row.engagedSessions.toLocaleString('en-US') }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
