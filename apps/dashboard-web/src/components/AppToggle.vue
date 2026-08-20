<script setup lang="ts">
defineProps<{
  modelValue: boolean
  disabled?: boolean
}>()

defineEmits<{
  'update:modelValue': [value: boolean]
}>()
</script>

<template>
  <label
    class="inline-flex cursor-pointer items-center gap-2"
    :class="{ 'cursor-not-allowed': disabled }"
  >
    <span class="relative inline-flex h-5 w-8.5 items-center">
      <input
        type="checkbox"
        class="peer sr-only"
        :checked="modelValue"
        :disabled="disabled"
        @change="$emit('update:modelValue', ($event.target as HTMLInputElement).checked)"
      />
      <span
        class="pointer-events-none absolute inset-0 rounded-full bg-neutral-300 transition-colors peer-checked:bg-brand-700 peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-brand-700 peer-disabled:opacity-40"
      />
      <span
        class="pointer-events-none absolute left-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform peer-checked:translate-x-3.5"
      />
    </span>
    <slot />
  </label>
</template>
