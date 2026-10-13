<script setup lang="ts">
/** `message` is always an already-translated i18n string — this component holds no copy of its own. */
defineProps<{
  state: 'loading' | 'error' | 'empty'
  message: string
}>()

const stateClasses = {
  loading: 'text-muted',
  error: 'text-bad',
  empty: 'text-muted',
}
</script>

<template>
  <p :class="['text-sm', stateClasses[state]]" :role="state === 'error' ? 'alert' : undefined">
    {{ message }}
  </p>
</template>
