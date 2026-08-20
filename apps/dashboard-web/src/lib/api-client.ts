import { useAuthStore } from '@/stores/auth'
import router from '@/router'

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api'

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const auth = useAuthStore()
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  // MSAL's silent-refresh-then-interactive-fallback (see stores/auth.ts) — async, unlike the
  // M00.2 placeholder's synchronous accessToken read.
  const token = await auth.getAccessToken()
  headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(`${baseUrl}${path}`, { ...init, headers })

  if (response.status === 401) {
    await auth.logout()
    await router.push({ name: 'login', query: { redirect: router.currentRoute.value.fullPath } })
  }

  if (!response.ok) {
    throw new ApiError(response.status, await response.text())
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

/** Typed API client. Every service sits behind the gateway at `baseUrl`, so callers never target a service URL directly. */
export const apiClient = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body ? JSON.stringify(body) : undefined }),
  patch: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PATCH', body: body ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
}
