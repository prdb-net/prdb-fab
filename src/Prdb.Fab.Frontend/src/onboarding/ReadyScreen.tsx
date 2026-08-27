import { useQuery } from '@tanstack/react-query'
import { Link, Navigate } from 'react-router'

import { readAccessState, readConnections } from '../api/client.ts'
import { accessStateKey } from '../access/state.ts'
import { connectionsKey } from './state.ts'
import styles from './Onboarding.module.css'

/**
 * Where setting up ends, and a placeholder.
 *
 * ADR 0010 ends the path on the wanted list, with the first sync visibly
 * running, because `VISION.md` measures onboarding by whether it leads to a
 * first download. There is no wanted list yet and no sync to run, so this page
 * says what the installation holds and names what is not built. The prdb sync
 * slice replaces it with the wanted list, and this file and the one line that
 * routes to it are the whole of that change.
 */
export function ReadyScreen() {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })

  if (state.isPending) {
    return null
  }

  // Setting up is not finished, so this is not the page to be on. The wizard
  // decides which one is; this never guesses at it.
  if (state.data?.nextStep !== 'Complete') {
    return <Navigate to="/" replace />
  }

  const held = connections.data

  return (
    <main className={styles.screen}>
      <h1>This installation is ready</h1>

      <p className={styles.lede}>
        The password is set, prdb answered to your key, and there is somewhere to
        put a library. Setting up is finished and does not come back.
      </p>

      {held && (
        <ul className={styles.rows}>
          <li>
            prdb
            <br />
            <span className={styles.rowDetail}>Connected.</span>
          </li>
          <li>
            SABnzbd
            <br />
            <span className={styles.rowDetail}>
              {held.sabnzbdConfigured
                ? `${held.sabnzbdUrl}, category ${held.sabnzbdCategory}.`
                : 'Not configured, so nothing will be downloaded.'}
            </span>
          </li>
          <li>
            Indexers
            <br />
            <span className={styles.rowDetail}>
              {Number(held.indexerCount) > 0
                ? `${held.indexerCount} configured.`
                : 'None, so nothing will be searched for.'}
            </span>
          </li>
          <li>
            The library
            <br />
            <span className={styles.rowDetail}>{held.libraryRoot}</span>
          </li>
        </ul>
      )}

      <h2 className={styles.heading}>What is not built yet</h2>
      <p>
        Nothing is synced from prdb, nothing is searched for, nothing is
        downloaded and nothing is filed. There is no wanted list to show you and
        no status page to check, and neither of the connections above is used by
        anything yet. This page is where the wanted list will be.
      </p>

      <p className={styles.note}>
        What does run is <Link to="/skeleton">the walking skeleton</Link>: one
        scheduled routine, turning.
      </p>
    </main>
  )
}
