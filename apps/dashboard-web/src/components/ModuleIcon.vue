<script setup lang="ts">
import type { PlatformModule } from '@/lib/modules'

/** `lit` turns the module's single accent path Signal; otherwise it stays the base colour. */
withDefaults(defineProps<{ module: PlatformModule; size?: number; lit?: boolean }>(), {
  size: 20,
  lit: false,
})
</script>

<template>
  <svg
    viewBox="0 0 24 24"
    :width="size"
    :height="size"
    fill="none"
    stroke="currentColor"
    stroke-width="1.5"
    stroke-linejoin="miter"
    stroke-linecap="butt"
    aria-hidden="true"
    class="flex-none"
  >
    <path :d="module.path" />
    <path :d="module.accent" :stroke="lit ? 'var(--color-signal)' : undefined" />
  </svg>
</template>
