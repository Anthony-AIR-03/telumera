<script setup lang="ts">
withDefaults(
  defineProps<{
    tone?: 'neutral' | 'danger'
  }>(),
  { tone: 'neutral' },
)

const toneClasses = {
  neutral: 'border-line text-fg-2',
  danger: 'border-bad/40 text-bad',
}
</script>

<template>
  <span
    :class="[
      'inline-flex items-center rounded-[3px] border px-1.5 py-px font-mono text-[11px] font-medium whitespace-nowrap',
      toneClasses[tone],
    ]"
  >
    <slot />
  </span>
</template>
