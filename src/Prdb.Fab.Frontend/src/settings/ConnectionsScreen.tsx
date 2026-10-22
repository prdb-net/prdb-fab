import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readConnections } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { IndexerList } from '../onboarding/IndexerList.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

/**
 * ADR 0020's Connections group. It is a set of routes rather than one page,
 * because ADR 0018 has every Gap carrying a route to the form that fills it —
 * and a Gap saying SABnzbd is missing has to land on that form rather than
 * scroll a wall of everything else into view.
 */
export function ConnectionsScreen() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const held = connections.data

  return (
    <SettingsPage
      title="Connections"
      lede="Connect the services that find and download your Videos. Each connection is checked before saving."
    >
      <ul className={styles.groups}>
        <li>
          <Link to="/settings/connections/prdb">prdb</Link>
          <br />
          <span className={styles.detail}>
            {!held ? 'Loading…' : held.prdbConfigured ? 'A key is stored.' : 'Not configured.'}
          </span>
        </li>
        <li>
          <Link to="/settings/connections/sabnzbd">SABnzbd</Link>
          <br />
          <span className={styles.detail}>
            {!held ? 'Loading…' : held.sabnzbdConfigured
              ? `${held.sabnzbdUrl}, category ${held.sabnzbdCategory}.`
              : held?.sabnzbdSkipped
                ? 'Skipped during setting up, so nothing is downloaded.'
                : 'Not configured.'}
          </span>
        </li>
      </ul>

      <h2 className={styles.heading}>Indexers</h2>
      <p className={styles.detail}>
        {Number(held?.indexerCount ?? 0) > 0
          ? 'Manage your Indexers and their priority below.'
          : held?.indexersSkipped
            ? 'None. This step was skipped during setting up, so nothing is searched for.'
            : 'None, so nothing is searched for.'}
      </p>

      <IndexerList under="/settings/connections/indexers" reorderable />

      <p className={styles.detail}>
        When Indexers offer the same Release, the one higher in this list is preferred.
        Reordering saves immediately.
      </p>

      <p className={styles.note}>
        <Link to="/settings/connections/indexers/new">Add an indexer</Link>
      </p>
    </SettingsPage>
  )
}
