/**
 * The query keys for the browse surfaces. The page is part of the key, so
 * moving back to one already read shows it out of the cache rather than
 * re-asking for it.
 */
export const whatsNewKey = (page: number) => ['catalogue', 'whats-new', page] as const
