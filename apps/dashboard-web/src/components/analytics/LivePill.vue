<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { LiveState } from '@/lib/use-live-snapshot'

/** The header's "N online now" pill. The square Signal dot blinks in steps while live (held steady
 *  under reduced motion) and goes muted when the connection drops, with the state spelled out. */
defineProps<{ count: number; state: LiveState }>()

const { t } = useI18n()
</script>

<template>
  <span
    class="inline-flex min-h-[34px] items-center gap-2 rounded-[3px] border border-line bg-surface px-3 text-[13px] font-medium"
    role="status"
  >
    <i
      class="block h-2 w-2"
      :class="state === 'live' ? 'live-blink bg-signal' : 'bg-muted'"
      aria-hidden="true"
    />
    <template v-if="state === 'live' || state === 'connecting'">
      <span class="tabular-nums">{{ state === 'live' ? count.toLocaleString('en-US') : '–' }}</span>
      {{ t('analytics.live.onlineNow') }}
    </template>
    <span v-else class="text-muted">{{ t(`analytics.live.${state}`) }}</span>
  </span>
</template>

<style scoped>
.live-blink {
  animation: blink 2s steps(2, jump-none) infinite;
}
@keyframes blink {
  50% {
    opacity: 0.25;
  }
}
@media (prefers-reduced-motion: reduce) {
  .live-blink {
    animation: none;
  }
}
</style>
