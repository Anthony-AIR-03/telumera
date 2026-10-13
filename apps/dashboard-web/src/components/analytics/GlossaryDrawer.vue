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
    class="fixed top-0 right-0 left-auto m-0 h-screen max-h-none w-full max-w-[392px] border-0 border-l border-line bg-raised p-0 text-fg backdrop:bg-graphite/50"
    @close="emit('close')"
    @cancel="emit('close')"
  >
    <div class="flex items-center justify-between border-b border-line px-5 py-4">
      <h2 class="font-display text-xl font-medium">
        {{ t('analytics.glossary.title') }}
      </h2>
      <button
        type="button"
        class="grid h-8 w-8 place-items-center rounded-[3px] text-muted hover:bg-surface hover:text-fg pointer-coarse:h-11 pointer-coarse:w-11"
        :aria-label="t('analytics.glossary.close')"
        @click="dialogRef?.close()"
      >
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
          aria-hidden="true"
        >
          <path d="M5 5L19 19M19 5L5 19" />
        </svg>
      </button>
    </div>

    <dl class="flex flex-col gap-5 overflow-y-auto px-5 py-5">
      <div v-for="entry in terms" :key="entry.term">
        <dt class="label-mono text-fg">
          {{ t(`analytics.glossary.terms.${entry.term}`) }}
        </dt>
        <dd class="mt-1.5 text-sm leading-relaxed text-fg-2">
          {{ t(`analytics.glossary.terms.${entry.body}`) }}
        </dd>
      </div>
    </dl>
  </dialog>
</template>
