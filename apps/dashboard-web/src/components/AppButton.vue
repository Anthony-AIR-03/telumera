<script setup lang="ts">
/**
 * Loading is visual-only by default (spinner, no text change) — only LoginView has a translated
 * swap-text key (login.signingIn), so `loadingLabel` is opt-in rather than synthesized here, which
 * would invent UI copy outside i18n. See docs/design/housestyle.md for the variant semantics.
 */
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'danger'
    loading?: boolean
    disabled?: boolean
    loadingLabel?: string
  }>(),
  {
    variant: 'primary',
    loading: false,
    disabled: false,
    loadingLabel: undefined,
  },
)

/** Primary is inverted fg/bg (Graphite on light, Bone on dark): Signal is a marker, never a fill. */
const variantClasses = {
  primary: 'border border-fg bg-fg text-page hover:border-fg-2 hover:bg-fg-2',
  secondary: 'border border-line bg-surface text-fg hover:border-fg-2',
  danger: 'border border-bad/40 text-bad hover:border-bad hover:bg-bad hover:text-white',
}
</script>

<template>
  <button
    :disabled="disabled || loading"
    :class="[
      'inline-flex min-h-10 items-center justify-center gap-2 rounded-[3px] px-3.5 py-2 text-sm font-semibold transition-colors pointer-coarse:min-h-11 disabled:cursor-not-allowed disabled:opacity-50',
      variantClasses[variant],
    ]"
  >
    <span
      v-if="loading"
      class="h-3 w-3 animate-spin border-[1.5px] border-current border-t-transparent"
      aria-hidden="true"
    />
    <template v-if="loading && loadingLabel">{{ loadingLabel }}</template>
    <slot v-else />
  </button>
</template>
