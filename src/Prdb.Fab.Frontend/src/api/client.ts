import type { components, paths } from './schema.d.ts'

// ADR 0036 and ADR 0040: plain fetch against types generated from the committed
// OpenAPI document. No client library — the contract is the document, and a
// response shape that changed without the types being regenerated is a red
// build here and a red build in CI rather than an empty column in the UI.

type Schema = components['schemas']

export type SkeletonItem = Schema['SkeletonItem']
export type ItemPage = Schema['ItemPage']
export type AddItemVerdict = Schema['AddItemVerdict']
export type RunNowVerdict = Schema['RunNowVerdict']
export type RecordedRun = Schema['RecordedRun']

type ItemsQuery = NonNullable<paths['/api/skeleton/items']['get']['parameters']['query']>

async function json<T>(response: Response): Promise<T> {
  if (!response.ok) {
    // ADR 0040 makes a verdict a 200, so anything else is genuinely a failed
    // request rather than an answer the caller did not like.
    throw new Error(`${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
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
