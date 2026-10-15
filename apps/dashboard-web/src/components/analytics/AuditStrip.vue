<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiClient } from '@/lib/api-client'
import type { QualityResponse } from '@/lib/analytics-types'

/**
 * The audit strip: the visible form of the Accuracy Principle, shown before any number (brand
 * README §8). It states what was collected for this range, using the same quality endpoint as the
 * Data quality screen. "No sampling" is a statement of fact: nothing in the pipeline samples. If
 * the counts can't be loaded the strip says so rather than disappearing.
 */
const props = defineProps<{ siteId: string; from: string; to: string }>()
const emit = defineEmits<{ 'open-glossary': [] }>()

const route = useRoute()
const { t } = useI18n()

const quality = ref<QualityResponse | null>(null)
const failed = ref(false)

async function load() {
  failed.value = false
  try {
    quality.value = await apiClient.get<QualityResponse>(
      `/sites/${props.siteId}/analytics/quality?from=${props.from}&to=${props.to}`,
    )
  } catch {
    quality.value = null
    failed.value = true
  }
}
watch(() => [props.siteId, props.from, props.to], load, { immediate: true })

const counts = computed(() => {
  const totals = quality.value?.totals ?? {}
  const n = (k: string) => totals[k] ?? 0
  const rejected =
    n('rejected_validation') +
    n('rejected_unknown_token') +
    n('rejected_origin') +
    n('rejected_module_disabled')
  return [
    { key: 'accepted', value: n('accepted') },
    { key: 'duplicates', value: n('duplicate') },
    { key: 'bots', value: n('bot') },
    { key: 'rejected', value: rejected },
  ]
})
</script>

<template>
  <div
    class="flex flex-wrap items-center gap-x-[22px] gap-y-1.5 rounded-[4px] border border-line bg-surface px-4 py-3 font-mono text-[13px] text-fg-2"
    role="note"
    :aria-label="t('analytics.audit.label')"
  >
    <template v-if="quality">
      <span v-for="c in counts" :key="c.key">
        <b class="font-medium text-fg tabular-nums">{{ c.value.toLocaleString('en-US') }}</b>
        {{ t(`analytics.audit.${c.key}`) }}
      </span>
    </template>
    <span v-else-if="failed" class="text-muted">{{ t('analytics.audit.unavailable') }}</span>
    <span v-else class="text-muted">{{ t('common.loading') }}</span>

    <span
      ><span class="text-ok" aria-hidden="true">■ </span>{{ t('analytics.audit.noSampling') }}</span
    >
    <span>{{ t('analytics.audit.cookieFree') }}</span>

    <span class="flex flex-wrap gap-x-4 font-sans font-medium sm:ml-auto">
      <button type="button" class="text-signal-text hover:underline" @click="emit('open-glossary')">
        {{ t('analytics.glossary.link') }}
      </button>
      <RouterLink
        :to="{ name: 'site-analytics-quality', params: { id: siteId }, query: route.query }"
        class="text-signal-text hover:underline"
      >
        {{ t('analytics.audit.howCounted') }} <span aria-hidden="true">→</span>
      </RouterLink>
    </span>
  </div>
</template>
