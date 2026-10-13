import { onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'
import { HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { createLiveConnection } from '@/lib/live-connection'
import type { LiveSnapshot } from '@/lib/analytics-types'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

/**
 * One SignalR subscription to a site's live-visitor projection, shared by everything on the page
 * that shows it (the header's "online now" pill and the "Live now" card), so the page holds a single
 * connection. Connects on mount, follows `siteId` changes, and releases the subscription on unmount.
 */
export function useLiveSnapshot(siteId: Ref<string>) {
  const state = ref<LiveState>('connecting')
  const snapshot = ref<LiveSnapshot>({ activeVisitors: 0, pages: [] })

  let connection: HubConnection | null = null
  let subscribedSiteId: string | null = null

  async function subscribe(id: string) {
    if (!connection || connection.state !== HubConnectionState.Connected) return
    try {
      if (subscribedSiteId && subscribedSiteId !== id) {
        await connection.invoke('Unsubscribe', subscribedSiteId)
      }
      await connection.invoke('Subscribe', id)
      subscribedSiteId = id
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
      await subscribe(siteId.value)
    })
    connection.onclose(() => (state.value = 'offline'))

    try {
      await connection.start()
      await subscribe(siteId.value)
    } catch {
      state.value = 'offline'
    }
  })

  watch(siteId, (id) => subscribe(id))

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

  return { state, snapshot }
}
