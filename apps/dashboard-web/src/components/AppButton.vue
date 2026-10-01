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

const variantClasses = {
  primary: 'bg-brand-700 text-white hover:bg-brand-800 focus-visible:outline-brand-700',
  secondary:
    'border border-neutral-200 text-neutral-900 hover:bg-neutral-100 focus-visible:outline-neutral-400',
  danger: 'bg-red-50 text-red-700 hover:bg-red-700 hover:text-white focus-visible:outline-red-700',
}
</script>

<template>
  <button
    :disabled="disabled || loading"
    :class="[
      'inline-flex min-h-11 items-center justify-center gap-2 rounded-[9px] px-[15px] py-[9px] text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-60',
      variantClasses[variant],
    ]"
  >
    <span
      v-if="loading"
      class="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
      aria-hidden="true"
    />
    <template v-if="loading && loadingLabel">{{ loadingLabel }}</template>
    <slot v-else />
  </button>
</template>
