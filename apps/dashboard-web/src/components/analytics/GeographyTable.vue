<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import AppBadge from '@/components/AppBadge.vue'
import StateMessage from '@/components/StateMessage.vue'
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

/** No GeoIP provider exists yet (M01.8) — every row's country is null until then, so a table would be one meaningless "Unknown: 100%" row. Showing that plainly instead of a near-empty table. */
const hasRealData = computed(
  () => rows.value !== null && rows.value.some((r) => r.country !== null),
)
</script>

<template>
  <div
    class="rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
  >
    <StateMessage v-if="error" state="error" :message="error" />
    <StateMessage v-else-if="loading" state="loading" :message="t('common.loading')" />

    <div
      v-else-if="rows && !hasRealData"
      class="flex flex-col items-center gap-1.5 px-5 py-11 text-center text-neutral-400"
    >
      <svg
        width="28"
        height="28"
        viewBox="0 0 20 20"
        fill="none"
        aria-hidden="true"
        class="opacity-50"
      >
        <circle cx="10" cy="10" r="7.5" stroke="currentColor" stroke-width="1.4" />
        <path
          d="M2.5 10h15M10 2.5c2.2 2 3.4 5 3.4 7.5s-1.2 5.5-3.4 7.5c-2.2-2-3.4-5-3.4-7.5S7.8 4.5 10 2.5z"
          stroke="currentColor"
          stroke-width="1.3"
        />
      </svg>
      <div class="text-sm font-bold text-neutral-600">
        {{ t('analytics.geography.notAvailable') }}
      </div>
      <p class="max-w-sm text-xs">{{ t('analytics.geography.notAvailableDetail') }}</p>
    </div>

    <div v-else-if="rows" class="overflow-x-auto">
      <table class="w-full border-collapse text-sm">
        <thead>
          <tr class="text-xs font-semibold text-neutral-500">
            <th scope="col" class="px-2 pb-2.5 text-left">
              {{ t('analytics.geography.country') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-right">
              {{ t('analytics.geography.sessions') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-right">
              {{ t('analytics.geography.visitors') }}
            </th>
            <th scope="col" class="px-2 pb-2.5 text-right">{{ t('analytics.geography.views') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, i) in rows" :key="i" class="border-t border-neutral-200">
            <td class="px-2 py-2.5">{{ row.country ?? t('analytics.geography.unknown') }}</td>
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
              {{ row.views.toLocaleString('en-US') }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
