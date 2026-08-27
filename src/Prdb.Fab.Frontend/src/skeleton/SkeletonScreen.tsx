import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'

import { addItem, listItems, listRuns, runSweepNow } from '../api/client.ts'
import styles from './SkeletonScreen.module.css'

/**
 * The one route, end to end. Scaffolding, and it says so — but what it shows is
 * the part worth seeing before any feature is built: an act reaches the
 * database, a routine in a lane picks the work up on its own, and the run log
 * stays empty for every tick that had nothing to do (ADR 0032).
 */
export function SkeletonScreen() {
  // ADR 0036: anything linkable lives in the address. The page is linkable, so
  // it is a search parameter and not state.
  const [parameters, setParameters] = useSearchParams()
  const page = Number(parameters.get('page') ?? '1')

  const [label, setLabel] = useState('')
  const [refusal, setRefusal] = useState<string | null>(null)
  const queries = useQueryClient()

  const items = useQuery({
    queryKey: ['skeleton', 'items', page],
    queryFn: () => listItems(page),
    refetchInterval: 2000,
  })

  const runs = useQuery({
    queryKey: ['skeleton', 'runs'],
    queryFn: listRuns,
    refetchInterval: 2000,
  })

  const add = useMutation({
    mutationFn: addItem,
    onSuccess: async (verdict) => {
      // ADR 0040: the verdict is the body of a 200. A refusal is something the
      // caller has to read, not something to retry.
      setRefusal(verdict.refusal ?? null)

      if (verdict.added) {
        setLabel('')
        await queries.invalidateQueries({ queryKey: ['skeleton'] })
      }
    },
  })

  const runNow = useMutation({
    mutationFn: runSweepNow,
    onSuccess: () => queries.invalidateQueries({ queryKey: ['skeleton'] }),
  })

  // ASP.NET Core declares an integer as `integer | string`, because the web
  // defaults accept a number written as one. The document is right and the
  // frontend says so rather than casting the type away.
  const total = Number(items.data?.total ?? 0)
  const pageSize = Number(items.data?.pageSize ?? 20)
  const pages = Math.max(1, Math.ceil(total / pageSize))

  return (
    <main className={styles.screen}>
      <h1>prdb-fab</h1>
      <p className={styles.lede}>
        The walking skeleton. Nothing here is part of the loop — no prdb call, no
        indexer, no downloader, no filing. What it demonstrates is the shape
        underneath: an act, a schedule, and a run log that records a run and
        ignores a tick that had nothing to do.
      </p>

      <section className={styles.section}>
        <h2 className={styles.heading}>Give the sweep something to do</h2>
        <form
          className={styles.row}
          onSubmit={(event) => {
            event.preventDefault()
            add.mutate(label)
          }}
        >
          <input
            className={styles.field}
            value={label}
            onChange={(event) => setLabel(event.target.value)}
            placeholder="A label"
            aria-label="A label"
          />
          <button className={styles.button} type="submit" disabled={add.isPending}>
            Add an item
          </button>
          <button
            className={styles.button}
            type="button"
            onClick={() => runNow.mutate()}
            disabled={runNow.isPending}
          >
            Run the sweep now
          </button>
        </form>

        {refusal && <p className={styles.refusal}>{refusal}</p>}

        <p className={styles.note}>
          Running it now sets the routine&rsquo;s due time and nothing else, so the
          bulk lane takes it on its next tick — at most a second away. Left alone,
          the sweep comes round every fifteen seconds.
        </p>
      </section>

      <section className={styles.section}>
        <h2 className={styles.heading}>Items ({total})</h2>
        {items.isPending && <p className={styles.quiet}>Asking&hellip;</p>}
        {items.isError && <p className={styles.refusal}>{String(items.error)}</p>}
        {items.data && items.data.items.length === 0 && (
          <p className={styles.quiet}>Nothing yet.</p>
        )}
        {items.data && items.data.items.length > 0 && (
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Label</th>
                <th>Added</th>
                <th>Swept</th>
              </tr>
            </thead>
            <tbody>
              {items.data.items.map((item) => (
                <tr key={item.id}>
                  <td>{item.label}</td>
                  <td className={styles.quiet}>{new Date(item.addedAt).toLocaleTimeString()}</td>
                  <td className={item.sweptAt ? styles.swept : styles.pending}>
                    {item.sweptAt ? new Date(item.sweptAt).toLocaleTimeString() : 'waiting'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {pages > 1 && (
          <div className={styles.row} style={{ marginTop: 'var(--gap)' }}>
            <button
              className={styles.button}
              type="button"
              disabled={page <= 1}
              onClick={() => setParameters({ page: String(page - 1) })}
            >
              Previous
            </button>
            <span className={styles.quiet}>
              Page {page} of {pages}
            </span>
            <button
              className={styles.button}
              type="button"
              disabled={page >= pages}
              onClick={() => setParameters({ page: String(page + 1) })}
            >
              Next
            </button>
          </div>
        )}
      </section>

      <section className={styles.section}>
        <h2 className={styles.heading}>Run log</h2>
        {runs.data && runs.data.length === 0 && (
          <p className={styles.quiet}>
            No runs recorded. The lane has been ticking the whole time — an empty
            work set means the routine was never due, and a tick that did nothing
            is not a run.
          </p>
        )}
        {runs.data && runs.data.length > 0 && (
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Started</th>
                <th>Outcome</th>
                <th>Items</th>
              </tr>
            </thead>
            <tbody>
              {runs.data.map((run) => (
                <tr key={run.id}>
                  <td className={styles.quiet}>{new Date(run.startedAt).toLocaleTimeString()}</td>
                  <td>{run.outcome}</td>
                  <td>{run.itemsHandled}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  )
}
