<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useTheme } from '@/lib/theme'

const { t } = useI18n()
const { theme, toggle } = useTheme()
</script>

<template>
  <button
    type="button"
    class="grid h-[34px] w-[34px] flex-none place-items-center rounded-[3px]"
    :aria-label="theme === 'dark' ? t('nav.themeLight') : t('nav.themeDark')"
    @click="toggle"
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
      <path d="M12 3a9 9 0 0 1 0 18a9 9 0 0 1 0-18Z" />
      <path d="M12 3a9 9 0 0 1 0 18Z" fill="currentColor" />
    </svg>
  </button>
</template>
