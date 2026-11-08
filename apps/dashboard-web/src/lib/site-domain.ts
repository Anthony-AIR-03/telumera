/**
 * Turns whatever was typed into the "Canonical domain" field into a bare host name.
 * People paste addresses (`https://example.com/`, `example.com/blog`), but the site's allowed
 * origin is built as `https://${domain}` and compared exactly by the event collector, so anything
 * besides the host (a second scheme, a path) makes it reject every event from that site.
 *
 * Returns null when nothing usable is left.
 */
export function normalizeSiteDomain(input: string): string | null {
  let value = input.trim().toLowerCase()
  if (!value) {
    return null
  }

  value = value.replace(/^[a-z][a-z0-9+.-]*:\/\//, '')
  value = value.split(/[/?#]/, 1)[0] ?? ''
  value = value.replace(/\.$/, '')

  return /^[a-z0-9.-]+(:\d+)?$/.test(value) && !value.startsWith('.') ? value : null
}
