<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { LiveSnapshot } from '@/lib/analytics-types'
import type { LiveState } from '@/lib/use-live-snapshot'

/**
 * "Live now": the pages people are on right now, from the rolling 5-minute live projection
 * (non-authoritative, definitions doc §9). Presentational — SiteAnalyticsView owns the connection
 * (useLiveSnapshot) and shares it with the header pill. A path that newly appears flashes
 * Signal-soft once, so changes are visible without animating the whole list.
 */
defineProps<{ snapshot: LiveSnapshot; state: LiveState }>()

const { t } = useI18n()
</script>

<template>
  <section class="card">
    <header class="flex items-baseline justify-between gap-3 px-5 pt-4">
      <h2 class="font-display text-[17px] font-medium">{{ t('analytics.live.title') }}</h2>
      <span class="font-mono text-xs text-muted">
        {{
          state === 'reconnecting' || state === 'offline'
            ? t(`analytics.live.${state}`)
            : t('analytics.live.window')
        }}
      </span>
    </header>

    <ul v-if="snapshot.pages.length" class="mt-2 px-5 pb-3.5">
      <li
        v-for="page in snapshot.pages"
        :key="page.path"
        class="live-row grid grid-cols-[1fr_auto] gap-3 border-b border-line-2 py-2.5 font-mono text-[12.5px] last:border-b-0"
      >
        <span class="truncate">{{ page.path }}</span>
        <span class="text-muted tabular-nums">{{
          t('analytics.live.pageVisitors', page.visitors)
        }}</span>
      </li>
    </ul>
    <p v-else class="px-5 pt-2 pb-5 text-sm text-muted">
      {{ t('analytics.live.noneOnPage') }}
    </p>
  </section>
</template>

<style scoped>
.live-row {
  animation: flash 0.6s ease-out;
}
@keyframes flash {
  from {
    background: var(--color-signal-soft);
  }
  to {
    background: transparent;
  }
}
@media (prefers-reduced-motion: reduce) {
  .live-row {
    animation: none;
  }
}
</style>
