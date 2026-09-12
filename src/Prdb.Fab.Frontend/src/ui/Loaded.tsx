import type { ReactNode } from 'react'
import type { UseQueryResult } from '@tanstack/react-query'

import { Verdict } from './Verdict.tsx'
import styles from './Form.module.css'

/**
 * A read, and the two answers that are not the data.
 *
 * ADR 0058's rule that a control is never rendered with a default standing in
 * for an unread value is held here rather than screen by screen. Every settings
 * screen used to write `choice ?? settings.data?.beforeDownload ?? 'ThroughProbable'`,
 * which renders the default as a selected answer while the read is in flight
 * and then silently jumps when it lands — a person who answered during that
 * window answered a different question.
 *
 * So the form is not rendered at all until there is something to render it
 * from, which also means a draft can be initialised from the real value with
 * plain `useState` and compared against it for dirty state.
 *
 * The failure is a sentence of this surface's own with a retry beside it, never
 * the exception.
 */
export function Loaded<T>({
  query,
  of,
  children,
}: {
  query: UseQueryResult<T>
  /** What is being read, as it appears mid-sentence: "the identification settings". */
  of: string
  children: (data: T) => ReactNode
}) {
  if (query.isPending) {
    return <p className={styles.reading}>Reading {of}…</p>
  }

  if (query.isError || query.data === undefined) {
    return (
      <>
        <Verdict tone="refusal">
          {of.charAt(0).toUpperCase() + of.slice(1)} could not be read. The tool may
          still be starting, or it may have stopped; the log says which.
        </Verdict>
        <button
          className={styles.button}
          type="button"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          {query.isFetching ? 'Trying again…' : 'Try again'}
        </button>
      </>
    )
  }

  return <>{children(query.data)}</>
}
