import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readAccessState } from '../api/client.ts'
import { accessStateKey } from '../access/state.ts'
import styles from './Chrome.module.css'

/**
 * The way between the surfaces there are, and a way to the settings from
 * wherever the user is.
 *
 * It appears only once setting up is finished. While the wizard is running it
 * is the whole page — ADR 0010 has each step commit and the path not come back,
 * and a bar offering the settings beside it would be a second way to answer the
 * same questions in a different order.
 */
export function Chrome() {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })

  if (state.data?.nextStep !== 'Complete') {
    return null
  }

  return (
    <nav className={styles.bar}>
      <Link className={styles.name} to="/">
        prdb-fab
      </Link>
      <Link to="/">What&rsquo;s new</Link>
      <Link to="/sites">Sites</Link>
      <Link to="/actors">Actors</Link>
      <Link to="/wanted">Wanted</Link>
      <Link to="/settings">Settings</Link>
    </nav>
  )
}
