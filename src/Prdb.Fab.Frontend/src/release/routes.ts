export type ReleaseAddress =
  | { video: string }
  | { site: string }
  | { actor: string }

/** The one Release route; only its local Catalogue context differs. */
export function releasePath(context: ReleaseAddress, from?: string): string {
  const parameters = new URLSearchParams(context)
  if (from) parameters.set('from', from)
  return `/releases?${parameters}`
}

export function videoReleasePath(prdbId: string, from?: string): string {
  return releasePath({ video: prdbId }, from)
}
