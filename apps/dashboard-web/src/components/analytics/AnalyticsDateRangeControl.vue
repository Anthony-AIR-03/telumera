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
  <div class="flex flex-wrap items-center gap-2.5">
    <div
      role="group"
      :aria-label="t('analytics.dateRange.presetGroup')"
      class="flex items-center gap-0.5 rounded-[9px] border border-neutral-300 bg-white p-1"
    >
      <button
        v-for="days in [7, 30, 90] as const"
        :key="days"
        type="button"
        :aria-pressed="activePresetDays === days"
        class="rounded-[7px] px-3 py-1.5 text-sm font-semibold"
        :class="
          activePresetDays === days
            ? 'bg-brand-50 font-bold text-brand-700'
            : 'text-neutral-600 hover:bg-neutral-50'
        "
        @click="applyPreset(days)"
      >
        {{ t('analytics.dateRange.preset', { days }) }}
      </button>
    </div>

    <input
      type="date"
      :value="from"
      :max="to"
      :aria-label="t('analytics.dateRange.from')"
      class="rounded-[9px] border border-neutral-300 px-3 py-1.5 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
      @change="onFromInput"
    />
    <span class="text-sm text-neutral-400" aria-hidden="true">&rarr;</span>
    <input
      type="date"
      :value="to"
      :min="from"
      :aria-label="t('analytics.dateRange.to')"
      class="rounded-[9px] border border-neutral-300 px-3 py-1.5 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
      @change="onToInput"
    />

    <button
      type="button"
      class="inline-flex items-center gap-1.5 rounded-[9px] border border-neutral-200 bg-white px-2.5 py-1.5 text-xs font-semibold"
      :class="linkCopied ? 'text-brand-700' : 'text-neutral-600 hover:bg-neutral-50'"
      @click="copyShareableLink"
    >
      <svg width="14" height="14" viewBox="0 0 20 20" fill="none" aria-hidden="true">
        <path
          d="M8.5 11.5L11.5 8.5"
          stroke="currentColor"
          stroke-width="1.6"
          stroke-linecap="round"
        />
        <path
          d="M9.8 6.3L11 5.1a2.6 2.6 0 013.7 3.7l-1.2 1.2M10.2 13.7L9 14.9a2.6 2.6 0 01-3.7-3.7l1.2-1.2"
          stroke="currentColor"
          stroke-width="1.6"
          stroke-linecap="round"
        />
      </svg>
      {{
        linkCopied ? t('analytics.dateRange.linkCopied') : t('analytics.dateRange.shareableLink')
      }}
    </button>
  </div>
</template>
