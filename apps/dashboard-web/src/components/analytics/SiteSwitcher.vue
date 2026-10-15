<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import StateMessage from '@/components/StateMessage.vue'

interface SiteSummary {
  id: string
  name: string
  canonicalDomain: string
}

defineProps<{
  currentSiteId: string
  currentDomain: string
  siblingSites: SiteSummary[]
}>()

const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const open = ref(false)
const rootRef = ref<HTMLElement | null>(null)

/** Confirmed live: without an outside-click handler the menu stayed open after clicking a date-range preset elsewhere on the page. */
function onDocumentClick(event: MouseEvent) {
  if (open.value && rootRef.value && !rootRef.value.contains(event.target as Node)) {
    open.value = false
  }
}
onMounted(() => document.addEventListener('click', onDocumentClick))
onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))

function switchTo(siteId: string) {
  open.value = false
  // Preserve from/to/tab — switching sites shouldn't reset the range or the active breakdown tab.
  router.push({ name: 'site-analytics', params: { id: siteId }, query: route.query })
}
</script>

<template>
  <div ref="rootRef" class="relative">
    <button
      type="button"
      class="inline-flex min-h-[34px] items-center gap-2 rounded-[3px] border border-line bg-surface px-2.5 font-mono text-xs font-medium text-fg-2 hover:border-fg-2 pointer-coarse:min-h-11"
      :aria-expanded="open"
      aria-haspopup="true"
      :aria-label="t('analytics.siteSwitcher.trigger')"
      @click="open = !open"
    >
      {{ currentDomain }}
      <svg
        width="12"
        height="12"
        viewBox="0 0 12 12"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
        aria-hidden="true"
        :style="{ transform: open ? 'rotate(180deg)' : 'rotate(0deg)' }"
      >
        <path d="M3 4.5L6 7.5L9 4.5" />
      </svg>
    </button>

    <div
      v-if="open"
      class="absolute top-[calc(100%+6px)] left-0 z-20 w-60 overflow-hidden rounded-[4px] border border-line bg-raised"
    >
      <div class="label-mono border-b border-line-2 px-3 pt-2.5 pb-2">
        {{ t('analytics.siteSwitcher.heading') }}
      </div>
      <div
        class="relative flex items-center justify-between bg-surface px-3 py-2.5 font-mono text-[13px]"
        aria-current="true"
      >
        <span class="absolute top-2 bottom-2 left-0 w-0.5 bg-signal" aria-hidden="true" />
        {{ currentDomain }}
      </div>
      <button
        v-for="site in siblingSites"
        :key="site.id"
        type="button"
        class="block w-full px-3 py-2.5 text-left font-mono text-[13px] hover:bg-surface pointer-coarse:min-h-11"
        @click="switchTo(site.id)"
      >
        {{ site.canonicalDomain }}
      </button>
      <StateMessage
        v-if="siblingSites.length === 0"
        state="empty"
        :message="t('analytics.siteSwitcher.empty')"
        class="border-t border-line-2 px-3 py-2.5 text-xs"
      />
    </div>
  </div>
</template>
