import type { components, paths } from './schema.d.ts'

// ADR 0036 and ADR 0040: plain fetch against types generated from the committed
// OpenAPI document. No client library — the contract is the document, and a
// response shape that changed without the types being regenerated is a red
// build here and a red build in CI rather than an empty column in the UI.

type Schema = components['schemas']

export type AccessState = Schema['AccessState']
export type OnboardingStep = Schema['OnboardingStep']
export type SetPasswordVerdict = Schema['SetPasswordVerdict']
export type SignInVerdict = Schema['SignInVerdict']

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

export async function signOut(): Promise<void> {
  const response = await fetch('/api/access/sign-out', { method: 'POST' })

  if (!response.ok && response.status !== 401) {
    throw new Error(`${response.status} ${response.statusText}`)
  }
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
