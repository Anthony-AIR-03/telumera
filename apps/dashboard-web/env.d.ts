/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  /** Origin of the analytics SignalR hub (live visitors). Dev default: http://localhost:5104. */
  readonly VITE_HUBS_ORIGIN?: string
  /** Origin the SDK install snippet points at (collector). Dev default: http://localhost:5103. */
  readonly VITE_COLLECTOR_ORIGIN?: string
  readonly VITE_AZURE_AD_TENANT_ID: string
  readonly VITE_AZURE_AD_CLIENT_ID: string
  readonly VITE_AZURE_AD_API_SCOPE: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
