/**
 * localStorage is unavailable or throws in private browsing / storage-disabled contexts.
 * Every call here is wrapped so a blocked storage layer degrades to "nothing persists"
 * instead of breaking tracking entirely.
 */
export function readStorage(key: string): string | null {
  try {
    return window.localStorage.getItem(key)
  } catch {
    return null
  }
}

export function writeStorage(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value)
  } catch {
    // Storage unavailable — silently no-op, see module doc comment.
  }
}

export function removeStorage(key: string): void {
  try {
    window.localStorage.removeItem(key)
  } catch {
    // Storage unavailable — silently no-op, see module doc comment.
  }
}

export function readJson<T>(key: string): T | null {
  const raw = readStorage(key)
  if (raw === null) return null
  try {
    return JSON.parse(raw) as T
  } catch {
    return null
  }
}

export function writeJson(key: string, value: unknown): void {
  writeStorage(key, JSON.stringify(value))
}
