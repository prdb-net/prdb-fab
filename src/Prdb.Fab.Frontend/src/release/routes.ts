export type ReleaseAddress =
  | { video: string }
  | { site: string }
  | { actor: string }

/** The one Release route; only its local Catalogue context differs. */
export function releasePath(context: ReleaseAddress): string {
  const parameters = new URLSearchParams(context)
  return `/releases?${parameters}`
}

export function videoReleasePath(prdbId: string): string {
  return releasePath({ video: prdbId })
}
