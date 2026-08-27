import type { components, paths } from './schema.d.ts'

// ADR 0036 and ADR 0040: plain fetch against types generated from the committed
// OpenAPI document. No client library — the contract is the document, and a
// response shape that changed without the types being regenerated is a red
// build here and a red build in CI rather than an empty column in the UI.

type Schema = components['schemas']

export type AccessState = Schema['AccessState']
export type OnboardingStep = Schema['OnboardingStep']
export type ChangePasswordVerdict = Schema['ChangePasswordVerdict']
export type SetPasswordVerdict = Schema['SetPasswordVerdict']
export type SignInVerdict = Schema['SignInVerdict']

export type OnboardingOutcome = Schema['OnboardingOutcome']
export type OnboardingVerdict = Schema['OnboardingVerdict']

export type ConnectionsState = Schema['ConnectionsState']
export type PrdbConnectionVerdict = Schema['PrdbConnectionVerdict']
export type SabnzbdCategory = Schema['SabnzbdCategory']
export type SabnzbdCategoriesVerdict = Schema['SabnzbdCategoriesVerdict']
export type SabnzbdConnectionVerdict = Schema['SabnzbdConnectionVerdict']
export type ConfiguredIndexer = Schema['ConfiguredIndexer']
export type IndexerConnectionVerdict = Schema['IndexerConnectionVerdict']
export type LibraryRootVerdict = Schema['LibraryRootVerdict']

export type VideoCard = Schema['VideoCard']
export type VideoPage = Schema['VideoPage']

export type SkeletonItem = Schema['SkeletonItem']
export type ItemPage = Schema['ItemPage']
export type AddItemVerdict = Schema['AddItemVerdict']
export type RunNowVerdict = Schema['RunNowVerdict']
export type RecordedRun = Schema['RecordedRun']

type ItemsQuery = NonNullable<paths['/api/skeleton/items']['get']['parameters']['query']>

/**
 * ADR 0010: an unauthenticated request gets 401 and never a redirect, so this
 * is what the end of a session looks like from here. Its own type, because the
 * one page has to tell it apart from a request that genuinely failed: one sends
 * the viewer back to the sign-in form, the other is an error to show.
 */
export class NotSignedIn extends Error {
  constructor() {
    super('Not signed in.')
    this.name = 'NotSignedIn'
  }
}

async function json<T>(response: Response): Promise<T> {
  if (response.status === 401) {
    throw new NotSignedIn()
  }

  if (!response.ok) {
    // ADR 0040 makes a verdict a 200, so anything else is genuinely a failed
    // request rather than an answer the caller did not like.
    throw new Error(`${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
}

async function post<T>(path: string, body?: unknown): Promise<T> {
  return json<T>(
    await fetch(path, {
      method: 'POST',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  )
}

export async function readAccessState(): Promise<AccessState> {
  return json<AccessState>(await fetch('/api/access/state'))
}

export async function setPassword(password: string): Promise<SetPasswordVerdict> {
  return post<SetPasswordVerdict>('/api/access/password', { password })
}

export async function signIn(password: string): Promise<SignInVerdict> {
  return post<SignInVerdict>('/api/access/sign-in', { password })
}

/**
 * ADR 0010: the current password is asked for again, and every other session
 * ends. ADR 0020 puts the act on the Account route.
 */
export async function changePassword(
  current: string,
  next: string,
): Promise<ChangePasswordVerdict> {
  return post<ChangePasswordVerdict>('/api/access/change-password', { current, next })
}

export async function signOut(): Promise<void> {
  const response = await fetch('/api/access/sign-out', { method: 'POST' })

  if (!response.ok && response.status !== 401) {
    throw new Error(`${response.status} ${response.statusText}`)
  }
}

/**
 * ADR 0010's path: the step is answered, so the marker moves past it. What
 * keeps the two mandatory steps mandatory is the backend reading what is
 * stored, not this call being withheld.
 */
export async function takeOnboardingStep(step: OnboardingStep): Promise<OnboardingVerdict> {
  return post<OnboardingVerdict>('/api/onboarding/take', { step })
}

/** The step is passed by deliberately, and what is left behind is a Gap. */
export async function skipOnboardingStep(step: OnboardingStep): Promise<OnboardingVerdict> {
  return post<OnboardingVerdict>('/api/onboarding/skip', { step })
}

export async function readConnections(): Promise<ConnectionsState> {
  return json<ConnectionsState>(await fetch('/api/connections'))
}

export async function savePrdbKey(
  apiKey: string,
  confirmAnotherAccount: boolean,
): Promise<PrdbConnectionVerdict> {
  return post<PrdbConnectionVerdict>('/api/connections/prdb', { apiKey, confirmAnotherAccount })
}

/**
 * A read that carries a credential, which is why it is a POST: a key has no
 * business in an address bar or in anybody's access log.
 */
export async function readSabnzbdCategories(
  url: string,
  apiKey: string,
): Promise<SabnzbdCategoriesVerdict> {
  return post<SabnzbdCategoriesVerdict>('/api/connections/sabnzbd/categories', { url, apiKey })
}

export async function saveSabnzbd(connection: {
  url: string
  apiKey: string
  category: string
  downloadDirectory: string
}): Promise<SabnzbdConnectionVerdict> {
  return post<SabnzbdConnectionVerdict>('/api/connections/sabnzbd', connection)
}

export async function listIndexers(): Promise<ConfiguredIndexer[]> {
  return json<ConfiguredIndexer[]>(await fetch('/api/connections/indexers'))
}

export async function addIndexer(indexer: {
  name: string
  url: string
  apiKey: string
}): Promise<IndexerConnectionVerdict> {
  return post<IndexerConnectionVerdict>('/api/connections/indexers', indexer)
}

/** ADR 0020's indexer route: the same check, run again over a row that is there. */
export async function editIndexer(
  id: string,
  indexer: { name: string; url: string; apiKey: string },
): Promise<IndexerConnectionVerdict> {
  return post<IndexerConnectionVerdict>(`/api/connections/indexers/${id}`, indexer)
}

export async function saveLibraryRoot(path: string): Promise<LibraryRootVerdict> {
  return post<LibraryRootVerdict>('/api/connections/library-root', { path })
}

/**
 * ADR 0013's What's New, read out of the catalogue. Nothing here reaches prdb:
 * the page is a query over what the sync routines have already written, which
 * is why a reload spends no request (ADR 0018).
 */
export async function listWhatsNew(page: number): Promise<VideoPage> {
  return json<VideoPage>(await fetch(`/api/catalogue/whats-new?page=${page}`))
}

export async function listItems(page: ItemsQuery['page']): Promise<ItemPage> {
  return json<ItemPage>(await fetch(`/api/skeleton/items?page=${page ?? 1}`))
}

export async function addItem(label: string): Promise<AddItemVerdict> {
  return json<AddItemVerdict>(
    await fetch('/api/skeleton/items', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ label }),
    }),
  )
}

export async function runSweepNow(): Promise<RunNowVerdict> {
  return json<RunNowVerdict>(await fetch('/api/skeleton/sweep/run-now', { method: 'POST' }))
}

export async function listRuns(): Promise<RecordedRun[]> {
  return json<RecordedRun[]>(await fetch('/api/skeleton/runs'))
}
