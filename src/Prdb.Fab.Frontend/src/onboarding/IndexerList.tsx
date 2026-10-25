import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation } from 'react-router'

import { listIndexers, moveIndexer } from '../api/client.ts'
import { Loaded } from '../ui/Loaded.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { formStyles } from '../ui/Form.tsx'
import { indexersKey } from './state.ts'
import styles from './Onboarding.module.css'

/**
 * The indexers that are configured, one row each — ADR 0002 identifies a
 * release by the indexer together with that indexer's own id for it, so the
 * rows matter from the first one.
 *
 * ADR 0020 gives every indexer its own route, so where there is somewhere to go
 * the row is a link to it. Onboarding passes nothing: there is no settings
 * surface to send anyone to while setting up.
 *
 * The order is ADR 0008's rank, and where the rows are reorderable this is
 * where that happens — ADR 0020 chose a list position over a typed number, so
 * the list *is* the control. Two buttons rather than a drag: a drag-only
 * reorder is unreachable from a keyboard, and this is a setting somebody may
 * only ever touch through one.
 */
export function IndexerList({ under, reorderable = false }: { under?: string; reorderable?: boolean }) {
  const indexers = useQuery({ queryKey: indexersKey, queryFn: listIndexers })
  const queries = useQueryClient()
  const { search } = useLocation()

  const move = useMutation({
    mutationFn: ({ id, up }: { id: string; up: boolean }) => moveIndexer(id, up),
    onSuccess: () => {
      void queries.invalidateQueries({ queryKey: indexersKey })
      void queries.invalidateQueries({ queryKey: ['releases'] })
    },
  })

  return <Loaded query={indexers} of="your Indexers">{rows => <>
    {rows.length === 0 && <p>No Indexers configured. Add one to find Releases.</p>}
    {move.isError && <Verdict tone="refusal">Could not reorder the Indexers. Try again.</Verdict>}
    {move.data && <Verdict tone="done">{move.data.detail}</Verdict>}
    <ul className={styles.rows}>
      {rows.map((indexer, at) => (
        <li key={indexer.id}>
          <div className={styles.row}>
            <div className={styles.rowMain}>
              {reorderable && <span aria-label={`Priority ${at + 1}`}>{at + 1}. </span>}
              {under ? <Link to={{ pathname: `${under}/${indexer.id}`, search }}>{indexer.name}</Link> : indexer.name}
              <br />
              <span className={styles.rowDetail}>
                {indexer.enabled ? 'Enabled' : 'Disabled'} &mdash; {indexer.url} &mdash;{' '}
                {indexer.categories.split(',').join(', ')}
              </span>
            </div>

            {reorderable && rows.length > 1 && (
              <div className={styles.rowActions}>
                <button
                  className={formStyles.button}
                  type="button"
                  disabled={at === 0 || move.isPending}
                  aria-label={`Move ${indexer.name} up`}
                  onClick={() => move.mutate({ id: indexer.id, up: true })}
                >
                  &uarr;
                </button>
                <button
                  className={formStyles.button}
                  type="button"
                  disabled={at === rows.length - 1 || move.isPending}
                  aria-label={`Move ${indexer.name} down`}
                  onClick={() => move.mutate({ id: indexer.id, up: false })}
                >
                  &darr;
                </button>
              </div>
            )}
          </div>
        </li>
      ))}
    </ul>
  </>}</Loaded>
}
