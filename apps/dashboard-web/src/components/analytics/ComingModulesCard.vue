<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import ModuleIcon from '@/components/ModuleIcon.vue'
import { MODULES, NEXT_MODULE_IDS } from '@/lib/modules'

/** The next planned modules, shown rather than hidden so the platform's direction is visible. */
const { t } = useI18n()
const next = MODULES.filter((m) => NEXT_MODULE_IDS.includes(m.id))
</script>

<template>
  <section class="card">
    <header class="flex items-baseline justify-between gap-3 px-5 pt-4">
      <h2 class="font-display text-[17px] font-medium">{{ t('analytics.coming.title') }}</h2>
      <span class="font-mono text-xs text-muted">{{ t('analytics.coming.meta') }}</span>
    </header>
    <div class="grid gap-3 px-5 pt-3 pb-5">
      <p class="text-[13px] text-muted">{{ t('analytics.coming.blurb') }}</p>
      <ul class="grid gap-2">
        <li v-for="mod in next" :key="mod.id" class="flex items-center gap-2.5 text-fg-2">
          <ModuleIcon :module="mod" class="text-muted" />
          {{ mod.name }}
          <small class="ml-auto font-mono text-[11px] text-muted">{{ mod.id }}</small>
        </li>
      </ul>
    </div>
  </section>
</template>
