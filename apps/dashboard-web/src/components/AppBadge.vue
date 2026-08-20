<script setup lang="ts">
withDefaults(
  defineProps<{
    tone?: 'neutral' | 'danger'
  }>(),
  { tone: 'neutral' },
)

const toneClasses = {
  neutral: 'bg-neutral-100 text-neutral-600',
  danger: 'bg-red-50 text-red-700',
}
</script>

<template>
  <span
    :class="[
      'inline-flex items-center rounded-full px-[9px] py-[3px] text-xs font-bold',
      toneClasses[tone],
    ]"
  >
    <slot />
  </span>
</template>
