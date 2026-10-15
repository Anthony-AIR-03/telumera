<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { addDays, resolveDateRange, todayIso } from '@/lib/date-range'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const from = computed(() => resolveDateRange(route.query).from)
const to = computed(() => resolveDateRange(route.query).to)

const activePresetDays = computed<7 | 30 | 90 | null>(() => {
  const spanDays =
    Math.round(
      (new Date(`${to.value}T00:00:00Z`).getTime() -
        new Date(`${from.value}T00:00:00Z`).getTime()) /
        86_400_000,
    ) + 1
  return spanDays === 7 || spanDays === 30 || spanDays === 90 ? spanDays : null
})

function updateQuery(patch: Record<string, string>) {
  router.replace({ query: { ...route.query, ...patch } })
}

function applyPreset(days: 7 | 30 | 90) {
  const newTo = todayIso()
  updateQuery({ from: addDays(newTo, -(days - 1)), to: newTo })
}

function onFromInput(event: Event) {
  updateQuery({ from: (event.target as HTMLInputElement).value })
}
function onToInput(event: Event) {
  updateQuery({ to: (event.target as HTMLInputElement).value })
}

const linkCopied = ref(false)
async function copyShareableLink() {
  try {
    await navigator.clipboard.writeText(window.location.href)
    linkCopied.value = true
    setTimeout(() => {
      linkCopied.value = false
    }, 1400)
  } catch {
    // Clipboard access can be denied (permissions, non-secure context) — the URL is already
    // shareable via the address bar regardless, so failing silently here is acceptable.
  }
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <div class="seg" role="group" :aria-label="t('analytics.dateRange.presetGroup')">
      <button
        v-for="days in [7, 30, 90] as const"
        :key="days"
        type="button"
        :aria-pressed="activePresetDays === days"
        class="pointer-coarse:min-h-10"
        @click="applyPreset(days)"
      >
        {{ t('analytics.dateRange.preset', { days }) }}
      </button>
    </div>

    <div class="flex items-center gap-1.5">
      <input
        type="date"
        :value="from"
        :max="to"
        :aria-label="t('analytics.dateRange.from')"
        class="field py-1.5 font-mono text-xs pointer-coarse:min-h-11"
        @change="onFromInput"
      />
      <span class="text-muted" aria-hidden="true">→</span>
      <input
        type="date"
        :value="to"
        :min="from"
        :aria-label="t('analytics.dateRange.to')"
        class="field py-1.5 font-mono text-xs pointer-coarse:min-h-11"
        @change="onToInput"
      />
    </div>

    <button
      type="button"
      class="inline-flex min-h-[34px] items-center gap-1.5 rounded-[3px] border border-line bg-surface px-2.5 text-xs font-medium pointer-coarse:min-h-11"
      :class="linkCopied ? 'text-fg' : 'text-fg-2 hover:border-fg-2'"
      @click="copyShareableLink"
    >
      <svg
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
        aria-hidden="true"
      >
        <path d="M10 14L14 10M11 6.5L13 4.5H19.5V11L17.5 13M13 17.5L11 19.5H4.5V13L6.5 11" />
      </svg>
      <span aria-live="polite">{{
        linkCopied ? t('analytics.dateRange.linkCopied') : t('analytics.dateRange.shareableLink')
      }}</span>
    </button>
  </div>
</template>
