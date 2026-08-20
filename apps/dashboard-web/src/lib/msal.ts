import { LogLevel, PublicClientApplication } from '@azure/msal-browser'

/**
 * Singleton MSAL client for the "Telumera Dashboard" app registration (Single-page application
 * platform, redirect URI http://localhost:5173 — distinct from the backend's "Telumera API" and
 * the CLI test client used by infrastructure/compose/scripts/get-dev-token.sh). No client secret
 * — SPA platform type is inherently a public client.
 *
 * redirectUri points at a dedicated `auth-popup.html` (project root, alongside `index.html` —
 * see `vite.config.ts`'s second rollup input) rather than the SPA root, for two stacked reasons
 * found by testing the real popup flow:
 *
 * 1. A relative `'/'` resolves to `${origin}/` (trailing slash), a different exact string than
 *    the no-slash URI registered in Entra, so the popup got rejected outright.
 * 2. Pointing redirectUri at the SPA root at all was still wrong even once the slash was fixed:
 *    Azure's redirect back to the SPA root is a real full-page navigation, so the app (router
 *    included) booted up a *second time inside the popup*, and its route guard immediately
 *    redirected the popup to `/login?redirect=/` before anything could hand the response back.
 *    This msal-browser version (5.x) doesn't poll `popup.location.href` from the opener the way
 *    older versions did — the popup itself must run `broadcastResponseToMainFrame()` (from
 *    `@azure/msal-browser/redirect-bridge`) to relay the auth response to the opener over a
 *    `BroadcastChannel`. `auth-popup.html` does exactly that and nothing else, so it's not
 *    racing the router, and it's what actually makes the opener's `loginPopup()` promise
 *    resolve instead of running until `popupBridgeTimeout`.
 *
 * Callers must `await ensureMsalInitialized()` before use — MSAL requires this before any other
 * API call.
 */
export const msalInstance = new PublicClientApplication({
  auth: {
    clientId: import.meta.env.VITE_AZURE_AD_CLIENT_ID,
    authority: `https://login.microsoftonline.com/${import.meta.env.VITE_AZURE_AD_TENANT_ID}`,
    redirectUri: `${window.location.origin}/auth-popup.html`,
  },
  cache: {
    cacheLocation: 'sessionStorage',
  },
  system: {
    loggerOptions: {
      loggerCallback: (_level, message) => console.log(`[MSAL] ${message}`),
      logLevel: LogLevel.Verbose,
      piiLoggingEnabled: false,
    },
  },
})

let initialized: Promise<void> | null = null

/**
 * Memoized so it only actually runs once. Called from `main.ts` before the app mounts, not
 * lazily on first use — `loginPopup()`'s `window.open()` call has to stay inside the same
 * synchronous tick as the click that triggered it, or Chrome silently blocks the popup instead
 * of opening it. Awaiting a real (not-yet-resolved) `initialize()` call from inside a click
 * handler introduces exactly that gap. Found by testing the real popup flow: initializing
 * eagerly here means by the time anyone clicks "Sign in," this promise is already resolved, so
 * awaiting it again is a same-tick no-op.
 */
export function ensureMsalInitialized(): Promise<void> {
  initialized ??= msalInstance.initialize()
  return initialized
}

/** The scope requested for every token acquired to call the gateway. */
export const apiScope = import.meta.env.VITE_AZURE_AD_API_SCOPE
