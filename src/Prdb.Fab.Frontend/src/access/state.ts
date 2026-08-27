import { QueryCache, QueryClient, MutationCache } from '@tanstack/react-query'

import { NotSignedIn } from '../api/client.ts'

/** The one thing the page decides from. */
export const accessStateKey = ['access', 'state'] as const

/**
 * ADR 0040: a verdict is HTTP 200 with a typed body, so there is nothing here
 * for TanStack Query to retry. Retries are off deliberately rather than by
 * omission: a failed request is a failed request, and ADR 0041 already decided
 * that nothing retries inside one.
 *
 * The two caches exist for one reason beyond that. A session can end while a
 * page is open — it expired, or the password was changed somewhere else — and
 * ADR 0010 says what that looks like: a 401, never a redirect. Rather than every
 * screen handling that, anything that comes back `NotSignedIn` re-asks the state
 * endpoint, and the one page decides again. Which is what makes it the one page
 * rather than the first of several.
 */
export function createQueryClient(): QueryClient {
  const reconsider = (error: unknown) => {
    if (error instanceof NotSignedIn) {
      void queries.invalidateQueries({ queryKey: accessStateKey })
    }
  }

  const queries = new QueryClient({
    queryCache: new QueryCache({ onError: reconsider }),
    mutationCache: new MutationCache({ onError: reconsider }),
    defaultOptions: {
      queries: { retry: false, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  })

  return queries
}
