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
        class="pointer-events-none absolute inset-0 rounded-[3px] border border-line bg-surface transition-colors peer-checked:border-fg peer-checked:bg-fg peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-signal peer-disabled:opacity-40"
      />
      <span
        class="pointer-events-none absolute left-[3px] h-3.5 w-3.5 rounded-[2px] bg-muted transition-transform peer-checked:translate-x-3.5 peer-checked:bg-page"
      />
    </span>
    <slot />
  </label>
</template>
