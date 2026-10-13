import { ref } from 'vue'

/**
 * Light/dark handling. With nothing stored the page follows the OS (`prefers-color-scheme`, see the
 * token blocks in assets/main.css); the toggle pins a choice by setting `data-theme` on <html> and
 * remembering it in localStorage. index.html applies a stored choice before first paint so a
 * pinned theme doesn't flash the other one on load.
 */
export type Theme = 'light' | 'dark'

const STORAGE_KEY = 'telumera-theme'
const darkQuery = window.matchMedia('(prefers-color-scheme: dark)')

function readStored(): Theme | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY)
    return value === 'light' || value === 'dark' ? value : null
  } catch {
    return null
  }
}

function effective(): Theme {
  return readStored() ?? (darkQuery.matches ? 'dark' : 'light')
}

/** Shared across every caller, so the rail's toggle and the mobile drawer's stay in sync. */
const current = ref<Theme>(effective())
darkQuery.addEventListener('change', () => {
  current.value = effective()
})

export function useTheme() {
  function toggle() {
    const next: Theme = current.value === 'dark' ? 'light' : 'dark'
    document.documentElement.dataset.theme = next
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // Storage blocked (private mode etc.): the choice still applies for this page view.
    }
    current.value = next
  }
  return { theme: current, toggle }
}
