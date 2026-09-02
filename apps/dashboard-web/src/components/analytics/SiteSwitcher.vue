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
      class="inline-flex items-center gap-1.5 rounded-[9px] border border-neutral-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-neutral-600 hover:bg-neutral-50"
      :aria-expanded="open"
      @click="open = !open"
    >
      {{ t('analytics.siteSwitcher.trigger') }}
      <svg
        width="14"
        height="14"
        viewBox="0 0 20 20"
        fill="none"
        aria-hidden="true"
        :style="{ transform: open ? 'rotate(180deg)' : 'rotate(0deg)' }"
      >
        <path
          d="M5 7.5L10 12.5L15 7.5"
          stroke="currentColor"
          stroke-width="1.6"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </button>

    <div
      v-if="open"
      class="absolute top-[calc(100%+6px)] left-0 z-10 w-56 overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-[0_8px_24px_rgba(15,21,18,0.12),0_2px_6px_rgba(15,21,18,0.06)]"
    >
      <div
        class="flex items-center justify-between px-3 py-2.5 text-sm font-semibold text-brand-700 bg-brand-50"
      >
        {{ currentDomain }}
        <svg width="14" height="14" viewBox="0 0 20 20" fill="none" aria-hidden="true">
          <path
            d="M4 10.5L8 14.5L16 6"
            stroke="currentColor"
            stroke-width="1.8"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </div>
      <button
        v-for="site in siblingSites"
        :key="site.id"
        type="button"
        class="block w-full px-3 py-2.5 text-left text-sm text-neutral-900 hover:bg-neutral-50"
        @click="switchTo(site.id)"
      >
        {{ site.canonicalDomain }}
      </button>
      <StateMessage
        v-if="siblingSites.length === 0"
        state="empty"
        :message="t('analytics.siteSwitcher.empty')"
        class="px-3 py-2.5"
      />
    </div>
  </div>
</template>
