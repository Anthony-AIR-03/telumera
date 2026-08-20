/**
 * Mirrors services/identity-workspace/Role.cs's real ordering: Viewer(0) < Developer(1) < Admin(2) <
 * Owner(3). UI-only — the backend enforces every real authorization check independently, this just
 * lets the UI reflect the caller's actual permissions before they'd otherwise hit a 403.
 */
const ROLE_ORDER: Record<string, number> = {
  Viewer: 0,
  Developer: 1,
  Admin: 2,
  Owner: 3,
}

export function roleAtLeast(current: string | null, required: string): boolean {
  if (current === null) {
    return false
  }
  return (ROLE_ORDER[current] ?? -1) >= (ROLE_ORDER[required] ?? Infinity)
}
