<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'

/** Emitted on every link click so a mobile drawer can close itself; the desktop sidebar ignores it. */
defineEmits<{ navigate: [] }>()

const { t } = useI18n()
</script>

<template>
  <nav class="flex flex-col gap-0.5">
    <RouterLink
      to="/"
      class="flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-semibold text-neutral-600 hover:bg-neutral-50 hover:text-neutral-900"
      active-class="bg-brand-50 text-brand-700 hover:bg-brand-50 hover:text-brand-700"
      @click="$emit('navigate')"
    >
      <svg class="h-[18px] w-[18px] flex-shrink-0" viewBox="0 0 20 20" fill="none">
        <rect x="3" y="3" width="6" height="6" rx="1.5" stroke="currentColor" stroke-width="1.6" />
        <rect
          x="11"
          y="3"
          width="6"
          height="6"
          rx="1.5"
          stroke="currentColor"
          stroke-width="1.6"
        />
        <rect
          x="3"
          y="11"
          width="6"
          height="6"
          rx="1.5"
          stroke="currentColor"
          stroke-width="1.6"
        />
        <rect
          x="11"
          y="11"
          width="6"
          height="6"
          rx="1.5"
          stroke="currentColor"
          stroke-width="1.6"
        />
      </svg>
      {{ t('nav.dashboard') }}
    </RouterLink>
    <RouterLink
      to="/analytics"
      class="flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-semibold text-neutral-600 hover:bg-neutral-50 hover:text-neutral-900"
      active-class="bg-brand-50 text-brand-700 hover:bg-brand-50 hover:text-brand-700"
      @click="$emit('navigate')"
    >
      <svg class="h-[18px] w-[18px] flex-shrink-0" viewBox="0 0 20 20" fill="none">
        <path
          d="M3 15.5V9m5.5 6.5V4.5M14 15.5v-4M19 15.5H1"
          stroke="currentColor"
          stroke-width="1.6"
          stroke-linecap="round"
        />
      </svg>
      {{ t('nav.analytics') }}
    </RouterLink>
  </nav>
</template>
