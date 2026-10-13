<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import ModuleIcon from '@/components/ModuleIcon.vue'
import { MODULES } from '@/lib/modules'

/** Emitted on every link click so a mobile drawer can close itself; the desktop rail ignores it. */
defineEmits<{ navigate: [] }>()

const { t } = useI18n()
const route = useRoute()

/** Which rail item owns the current page. Site pages sit under Workspaces (that's how you reach them). */
const ANALYTICS_ROUTES = ['analytics-sites', 'site-analytics', 'site-analytics-quality']
const activeModuleRoute = computed(() =>
  ANALYTICS_ROUTES.includes(String(route.name)) ? '/analytics' : null,
)
const workspacesActive = computed(() =>
  ['dashboard', 'workspace-detail', 'site-detail'].includes(String(route.name)),
)

const itemClass =
  'relative flex whitespace-nowrap items-center gap-3 rounded-[3px] px-2.5 py-2 text-sm text-rail-fg hover:bg-rail-hover'
</script>

<template>
  <div class="flex flex-col gap-[22px]">
    <nav class="grid gap-0.5" :aria-label="t('nav.modules')">
      <div class="label-mono mb-1.5 px-2.5 text-rail-muted" aria-hidden="true">
        {{ t('nav.modules') }}
      </div>
      <template v-for="mod in MODULES" :key="mod.id">
        <RouterLink
          v-if="mod.route"
          :to="mod.route"
          :class="[itemClass, activeModuleRoute === mod.route && 'bg-rail-hover']"
          :aria-current="activeModuleRoute === mod.route ? 'page' : undefined"
          @click="$emit('navigate')"
        >
          <span
            v-if="activeModuleRoute === mod.route"
            class="absolute top-2 bottom-2 -left-3.5 w-0.5 bg-signal"
            aria-hidden="true"
          />
          <ModuleIcon
            :module="mod"
            :lit="activeModuleRoute === mod.route"
            :class="activeModuleRoute === mod.route ? 'text-rail-fg' : 'text-rail-muted'"
          />
          {{ mod.name }}
        </RouterLink>
        <div v-else :class="[itemClass, 'text-rail-muted hover:bg-transparent']">
          <ModuleIcon :module="mod" class="opacity-70" />
          <span class="min-w-0 truncate">{{ mod.name }}</span>
          <span
            class="ml-auto flex-none rounded-[2px] border border-rail-line px-1 font-mono text-[9.5px] tracking-[0.06em] uppercase"
          >
            {{ t('nav.planned') }}
          </span>
        </div>
      </template>
    </nav>

    <nav class="grid gap-0.5" :aria-label="t('nav.workspace')">
      <div class="label-mono mb-1.5 px-2.5 text-rail-muted" aria-hidden="true">
        {{ t('nav.workspace') }}
      </div>
      <RouterLink
        to="/"
        :class="[itemClass, workspacesActive && 'bg-rail-hover']"
        :aria-current="workspacesActive ? 'page' : undefined"
        @click="$emit('navigate')"
      >
        <span
          v-if="workspacesActive"
          class="absolute top-2 bottom-2 -left-3.5 w-0.5 bg-signal"
          aria-hidden="true"
        />
        <svg
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
          aria-hidden="true"
          class="flex-none"
          :class="workspacesActive ? 'text-rail-fg' : 'text-rail-muted'"
        >
          <path d="M4 4H20V10H4ZM4 14H20V20H4Z" />
        </svg>
        {{ t('nav.workspaces') }}
      </RouterLink>
    </nav>
  </div>
</template>
