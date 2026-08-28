/**
 * The query keys for the browse surfaces. The page is part of the key, so
 * moving back to one already read shows it out of the cache rather than
 * re-asking for it.
 */
export const whatsNewKey = (page: number) => ['catalogue', 'whats-new', page] as const

/** ADR 0007's list, read. Its own key, because it is a different question. */
export const wantedKey = (page: number) => ['catalogue', 'wanted', page] as const

export const sitesKey = (selected: string | undefined, search: string, page: number) =>
  ['catalogue', 'sites', selected, search, page] as const

export const actorsKey = (selected: string | undefined, search: string, page: number) =>
  ['catalogue', 'actors', selected, search, page] as const
