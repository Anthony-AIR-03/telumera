<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  close: []
}>()

const { t } = useI18n()
const dialogRef = ref<HTMLDialogElement | null>(null)

/** Native <dialog> gives Esc-to-close, a backdrop, and focus trapping for free — no hand-rolled modal logic needed. */
watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      dialogRef.value?.showModal()
    } else {
      dialogRef.value?.close()
    }
  },
)

const terms = [
  { term: 'visitor', body: 'visitorBody' },
  { term: 'session', body: 'sessionBody' },
  { term: 'engagedSession', body: 'engagedSessionBody' },
  { term: 'bounceRate', body: 'bounceRateBody' },
  { term: 'comparisonPeriod', body: 'comparisonPeriodBody' },
  { term: 'privacyFloor', body: 'privacyFloorBody' },
] as const
</script>

<template>
  <!--
    left-auto is load-bearing, not decoration — confirmed live: without it the drawer rendered
    pinned to the LEFT edge. A modal <dialog>'s UA stylesheet sets `inset: 0` (left:0 included) on
    the top-layer element; author `right-0` alone doesn't clear that `left:0`, so it wins the
    positioning and only the explicit max-width keeps the panel from spanning the full viewport.
  -->
  <dialog
    ref="dialogRef"
    class="fixed top-0 left-auto right-0 m-0 h-screen max-h-none w-full max-w-[392px] border-0 bg-white p-0 shadow-[-8px_0_32px_rgba(15,21,18,0.16)] backdrop:bg-neutral-900/30"
    @close="emit('close')"
    @cancel="emit('close')"
  >
    <div class="flex items-center justify-between border-b border-neutral-200 px-5 py-4">
      <h2 class="font-display text-base font-bold text-neutral-900">
        {{ t('analytics.glossary.title') }}
      </h2>
      <button
        type="button"
        class="flex h-7 w-7 items-center justify-center rounded-lg text-neutral-600 hover:bg-neutral-100"
        :aria-label="t('analytics.glossary.close')"
        @click="dialogRef?.close()"
      >
        <svg width="16" height="16" viewBox="0 0 20 20" fill="none" aria-hidden="true">
          <path
            d="M5 5l10 10M15 5L5 15"
            stroke="currentColor"
            stroke-width="1.8"
            stroke-linecap="round"
          />
        </svg>
      </button>
    </div>

    <div class="flex flex-col gap-5 overflow-y-auto px-5 py-5">
      <div v-for="entry in terms" :key="entry.term">
        <div class="text-sm font-bold text-neutral-900">
          {{ t(`analytics.glossary.terms.${entry.term}`) }}
        </div>
        <p class="mt-1 text-sm leading-relaxed text-neutral-600">
          {{ t(`analytics.glossary.terms.${entry.body}`) }}
        </p>
      </div>
    </div>
  </dialog>
</template>
