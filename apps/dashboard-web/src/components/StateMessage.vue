<script setup lang="ts">
/** `message` is always an already-translated i18n string — this component holds no copy of its own. */
defineProps<{
  state: 'loading' | 'error' | 'empty'
  message: string
}>()

const stateClasses = {
  loading: 'text-neutral-500',
  error: 'text-red-700',
  empty: 'text-neutral-500',
}
</script>

<template>
  <p :class="['text-sm', stateClasses[state]]" :role="state === 'error' ? 'alert' : undefined">
    {{ message }}
  </p>
</template>
