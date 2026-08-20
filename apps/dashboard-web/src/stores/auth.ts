import { ref, computed } from 'vue'
import { defineStore } from 'pinia'
import { InteractionRequiredAuthError, type AccountInfo } from '@azure/msal-browser'
import { msalInstance, apiScope, ensureMsalInitialized } from '@/lib/msal'

/**
 * Real Entra ID auth boundary (replaces the M00.2 placeholder that just held a hand-fed token).
 * MSAL itself is initialized once at app boot (`main.ts`, before mount) — see
 * `ensureMsalInitialized`'s doc comment in `lib/msal.ts` for why that has to happen eagerly
 * rather than lazily on first use. `restore()` is still called from the router guard on every
 * navigation, so a page refresh correctly picks the session back up from MSAL's own
 * session-storage cache.
 */
export const useAuthStore = defineStore('auth', () => {
  const account = ref<AccountInfo | null>(null)

  const isAuthenticated = computed(() => account.value !== null)

  async function restore() {
    await ensureMsalInitialized()
    if (account.value) {
      return
    }

    const [existing] = msalInstance.getAllAccounts()
    if (existing) {
      account.value = existing
      msalInstance.setActiveAccount(existing)
    }
  }

  async function login() {
    await ensureMsalInitialized()
    const result = await msalInstance.loginPopup({ scopes: [apiScope] })
    account.value = result.account
    msalInstance.setActiveAccount(result.account)
  }

  /**
   * Local-only sign-out: clears this app's own MSAL cache without a network round-trip to
   * Azure AD's end_session_endpoint. `logoutPopup()` performs a full front-channel logout — it
   * opens a real Microsoft popup asking the user to confirm ending their whole Entra SSO session,
   * which also affects any other app sharing it. That's more than a "Log out" button on an
   * internal dashboard should do, and the popup reads as unexpected/confusing (found by testing
   * the real flow — it looks like an unrelated sign-in prompt). A future re-sign-in to Telumera
   * will pick the still-live Microsoft session back up silently, which is the expected trade-off
   * for a local-only logout.
   */
  async function logout() {
    await ensureMsalInitialized()
    await msalInstance.clearCache({ account: account.value })
    account.value = null
  }

  /** Silent refresh first, falling back to an interactive popup only when actually required. */
  async function getAccessToken(): Promise<string> {
    await ensureMsalInitialized()
    if (!account.value) {
      throw new Error('Not authenticated')
    }

    try {
      const result = await msalInstance.acquireTokenSilent({ scopes: [apiScope], account: account.value })
      return result.accessToken
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        const result = await msalInstance.acquireTokenPopup({ scopes: [apiScope] })
        return result.accessToken
      }
      throw error
    }
  }

  return { account, isAuthenticated, restore, login, logout, getAccessToken }
})
