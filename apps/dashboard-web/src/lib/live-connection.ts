import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useAuthStore } from '@/stores/auth'

/**
 * The live-visitors hub lives on the analytics service directly, not behind the gateway — a Dapr
 * service-invocation forwarder can't carry a WebSocket. In dev that's localhost:5104; in prod it's a
 * dedicated subdomain (hubs.telumera.nl). Derived from VITE_HUBS_ORIGIN, falling back to the local
 * analytics port so `npm run dev` needs no extra config.
 */
const hubsOrigin = import.meta.env.VITE_HUBS_ORIGIN ?? 'http://localhost:5104'

export function createLiveConnection(): HubConnection {
  const auth = useAuthStore()
  return new HubConnectionBuilder()
    .withUrl(`${hubsOrigin}/hubs/live`, {
      // SignalR appends this as the ?access_token query param on the WS handshake (a browser
      // WebSocket can't send an Authorization header). The analytics service reads it back for
      // /hubs paths only — see services/analytics/Program.cs.
      accessTokenFactory: () => auth.getAccessToken(),
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
}
