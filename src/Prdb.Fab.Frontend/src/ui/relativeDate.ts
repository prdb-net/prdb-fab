/** Catalogue release dates are calendar dates, so compare UTC days, not local hours. */
export function relativeDate(value: string, now = new Date()): string {
  const date = Date.parse(value)
  if (!Number.isFinite(date)) return value
  const today = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate())
  const days = Math.floor((today - date) / 86_400_000)
  if (days === 0) return 'Today'
  const absolute = Math.abs(days)
  const age = absolute >= 365 ? `${Math.floor(absolute / 365)}y`
    : absolute >= 30 ? `${Math.floor(absolute / 30)}mo`
      : `${absolute}d`
  return days < 0 ? `in ${age}` : `${age} ago`
}
