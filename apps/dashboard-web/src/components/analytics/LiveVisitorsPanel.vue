<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { createLiveConnection } from '@/lib/live-connection'
import type { LiveSnapshot } from '@/lib/analytics-types'

const props = defineProps<{ siteId: string }>()

const { t } = useI18n()

type ConnState = 'connecting' | 'live' | 'reconnecting' | 'offline'
const state = ref<ConnState>('connecting')
const snapshot = ref<LiveSnapshot>({ activeVisitors: 0, pages: [] })

let connection: HubConnection | null = null
let subscribedSiteId: string | null = null

async function subscribe(siteId: string) {
  if (!connection || connection.state !== HubConnectionState.Connected) return
  try {
    if (subscribedSiteId && subscribedSiteId !== siteId) {
      await connection.invoke('Unsubscribe', subscribedSiteId)
    }
    await connection.invoke('Subscribe', siteId)
    subscribedSiteId = siteId
    snapshot.value = { activeVisitors: 0, pages: [] }
  } catch {
    state.value = 'offline'
  }
}

onMounted(async () => {
  connection = createLiveConnection()
  connection.on('live', (payload: LiveSnapshot) => {
    snapshot.value = payload
    state.value = 'live'
  })
  connection.onreconnecting(() => (state.value = 'reconnecting'))
  connection.onreconnected(async () => {
    state.value = 'live'
    subscribedSiteId = null
    await subscribe(props.siteId)
  })
  connection.onclose(() => (state.value = 'offline'))

  try {
    await connection.start()
    await subscribe(props.siteId)
  } catch {
    state.value = 'offline'
  }
})

watch(
  () => props.siteId,
  (id) => subscribe(id),
)

onBeforeUnmount(async () => {
  const conn = connection
  connection = null
  if (!conn) return
  try {
    if (subscribedSiteId) await conn.invoke('Unsubscribe', subscribedSiteId)
  } catch {
    /* connection already gone — nothing to release */
  }
  await conn.stop()
})
</script>

<template>
  <div
    class="rounded-[14px] border border-neutral-200 bg-white p-5 shadow-[0_1px_2px_rgba(15,21,18,0.04),0_1px_1px_rgba(15,21,18,0.03)]"
  >
    <div class="flex items-center gap-2.5">
      <span class="relative flex h-2.5 w-2.5">
        <span
          v-if="state === 'live'"
          class="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-500 opacity-75"
        />
        <span
          class="relative inline-flex h-2.5 w-2.5 rounded-full"
          :class="state === 'live' ? 'bg-brand-500' : 'bg-neutral-400'"
        />
      </span>
      <span class="text-xs font-semibold tracking-wide text-neutral-500 uppercase">
        {{ t('analytics.live.title') }}
      </span>
      <span v-if="state === 'reconnecting' || state === 'offline'" class="text-xs text-neutral-400">
        {{ t(`analytics.live.${state}`) }}
      </span>
    </div>

    <div class="mt-3 flex flex-wrap items-end gap-x-8 gap-y-3">
      <div>
        <div class="font-display text-2xl font-bold tabular-nums text-neutral-900">
          {{ snapshot.activeVisitors.toLocaleString('en-US') }}
        </div>
        <div class="text-xs text-neutral-500">
          {{ t('analytics.live.activeVisitors', snapshot.activeVisitors) }}
        </div>
      </div>

      <ul v-if="snapshot.pages.length" class="min-w-0 flex-1 space-y-1 text-sm">
        <li
          v-for="page in snapshot.pages"
          :key="page.path"
          class="flex items-center justify-between gap-4"
        >
          <span class="truncate font-mono text-xs text-neutral-600">{{ page.path }}</span>
          <span class="tabular-nums text-neutral-500">{{ page.visitors }}</span>
        </li>
      </ul>
      <p v-else class="flex-1 text-sm text-neutral-400">
        {{ t('analytics.live.noneOnPage') }}
      </p>
    </div>
  </div>
</template>
