import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { listIndexers } from '../api/client.ts'
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
 */
export function IndexerList({ under }: { under?: string }) {
  const indexers = useQuery({ queryKey: indexersKey, queryFn: listIndexers })

  if (!indexers.data || indexers.data.length === 0) {
    return null
  }

  return (
    <ul className={styles.rows}>
      {indexers.data.map((indexer) => (
        <li key={indexer.id}>
          {under ? <Link to={`${under}/${indexer.id}`}>{indexer.name}</Link> : indexer.name}
          <br />
          <span className={styles.rowDetail}>
            {indexer.url} &mdash; {indexer.categories.split(',').join(', ')}
          </span>
        </li>
      ))}
    </ul>
  )
}
