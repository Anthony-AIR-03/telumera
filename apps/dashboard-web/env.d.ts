/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  readonly VITE_AZURE_AD_TENANT_ID: string
  readonly VITE_AZURE_AD_CLIENT_ID: string
  readonly VITE_AZURE_AD_API_SCOPE: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
