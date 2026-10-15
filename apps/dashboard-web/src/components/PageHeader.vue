<script setup lang="ts">
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import { useI18n } from 'vue-i18n'

/**
 * The page header every screen uses: a mono breadcrumb (`workspace / site / Module`), a Chakra Petch
 * title, and an optional tools area on the right (`#tools` slot). `#title-after` sits inline with
 * the title, e.g. the site switcher.
 */
defineProps<{
  title: string
  crumbs?: { label: string; to?: RouteLocationRaw }[]
}>()

const { t } = useI18n()
</script>

<template>
  <header class="flex flex-wrap items-end justify-between gap-x-5 gap-y-4">
    <div class="min-w-0">
      <nav
        v-if="crumbs?.length"
        class="mb-1.5 flex flex-wrap items-center gap-x-1.5 font-mono text-xs font-medium tracking-[0.04em] text-muted"
        :aria-label="t('nav.breadcrumb')"
      >
        <template v-for="(crumb, i) in crumbs" :key="i">
          <span v-if="i > 0" aria-hidden="true">/</span>
          <RouterLink v-if="crumb.to" :to="crumb.to" class="hover:text-fg hover:underline">
            {{ crumb.label }}
          </RouterLink>
          <span v-else>{{ crumb.label }}</span>
        </template>
      </nav>
      <div class="flex flex-wrap items-center gap-3">
        <h1
          class="font-display text-[28px] leading-[1.05] font-medium tracking-[-0.005em] break-words sm:text-[34px]"
        >
          {{ title }}
        </h1>
        <slot name="title-after" />
      </div>
    </div>
    <div v-if="$slots.tools" class="flex flex-wrap items-center gap-2.5">
      <slot name="tools" />
    </div>
  </header>
</template>
