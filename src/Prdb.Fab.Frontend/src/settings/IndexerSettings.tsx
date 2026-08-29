import { useQuery } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router'

import { listIndexers } from '../api/client.ts'
import { indexersKey } from '../onboarding/state.ts'
import { IndexerForm } from '../onboarding/IndexerForm.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import styles from './Settings.module.css'

/**
 * ADR 0020 gives every indexer its own route, because ADR 0018's Brakes point
 * at one row rather than at a list. The same route adds one, with no row behind
 * it.
 *
 * The row is read out of the list rather than from an endpoint of its own: it
 * is one query either way, the list is what the page before this one just
 * fetched, and there is no second shape to keep in step with the first.
 */
export function IndexerSettings() {
  const { id } = useParams<{ id: string }>()
  const indexers = useQuery({ queryKey: indexersKey, queryFn: listIndexers })
  const navigate = useNavigate()

  if (indexers.isPending) {
    return <PageLoading label="Loading Indexer" />
  }

  const indexer = id ? indexers.data?.find((candidate) => candidate.id === id) : undefined

  if (id && !indexer) {
    return (
      <SettingsPage title="That indexer is not here" back="/settings/connections" backLabel="Connections">
        <p className={styles.detail}>
          There is no indexer with that address in this installation.
        </p>
      </SettingsPage>
    )
  }

  return (
    <SettingsPage
      title={indexer ? indexer.name : 'Add an indexer'}
      lede="Checked with a real search before anything is stored, because most indexers answer a capabilities call to anybody."
      back="/settings/connections"
      backLabel="Connections"
    >
      <IndexerForm
        indexer={indexer}
        onSaved={() => {
          // Adding one lands on the list it was added to; correcting one stays
          // where it is, showing what the check just came back with.
          if (!indexer) {
            void navigate('/settings/connections')
          }
        }}
      />

      {indexer && (
        <p className={styles.note}>
          Searching: {indexer.categories.split(',').join(', ')}. Last checked{' '}
          {new Date(indexer.lastCheckedAt).toLocaleString()}.
        </p>
      )}
    </SettingsPage>
  )
}
