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
      lede="Each of these is checked against the service it names before anything is stored, and a check that fails changes nothing."
    >
      <ul className={styles.groups}>
        <li>
          <Link to="/settings/connections/prdb">prdb</Link>
          <br />
          <span className={styles.detail}>
            {held?.prdbConfigured ? 'A key is stored.' : 'Not configured.'}
          </span>
        </li>
        <li>
          <Link to="/settings/connections/sabnzbd">SABnzbd</Link>
          <br />
          <span className={styles.detail}>
            {held?.sabnzbdConfigured
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
          ? 'Each has its own route, because what points at one of them points at the row rather than at the list.'
          : held?.indexersSkipped
            ? 'None. This step was skipped during setting up, so nothing is searched for.'
            : 'None, so nothing is searched for.'}
      </p>

      <IndexerList under="/settings/connections/indexers" />

      <p className={styles.note}>
        <Link to="/settings/connections/indexers/new">Add an indexer</Link>
      </p>
    </SettingsPage>
  )
}
