import { ref, computed } from 'vue'
import { defineStore } from 'pinia'

const ACCESS_TOKEN_STORAGE_KEY = 'telumera.accessToken'

/**
 * Placeholder auth boundary: holds the access token and exposes login/logout so the router guard
 * and API client have a real seam to code against. Replaced with real sign-in against the
 * identity-workspace service once M00's control plane ships — until then `login` just accepts a
 * token rather than performing a real credential exchange.
 */
export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem(ACCESS_TOKEN_STORAGE_KEY))

  const isAuthenticated = computed(() => accessToken.value !== null)

  function login(token: string) {
    accessToken.value = token
    localStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, token)
  }

  function logout() {
    accessToken.value = null
    localStorage.removeItem(ACCESS_TOKEN_STORAGE_KEY)
  }

  return { accessToken, isAuthenticated, login, logout }
})
