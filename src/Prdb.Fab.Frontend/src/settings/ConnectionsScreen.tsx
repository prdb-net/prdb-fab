import { Link, Navigate, useLocation } from 'react-router'
import { IndexerList } from '../onboarding/IndexerList.tsx'
import { formStyles } from '../ui/Form.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

/** Keep old Status links working; each connection now has its own navigation entry. */
export function ConnectionsScreen({ redirect = false }: { redirect?: boolean }) {
  const { search } = useLocation()
  if (redirect) return <Navigate to={{ pathname: '/settings/connections/indexers', search }} replace />

  return <SettingsPage title="Indexers" lede="Find Releases and choose which Indexers are preferred.">
    <Link className={formStyles.button} to={{ pathname: '/settings/connections/indexers/new', search }}>Add an indexer</Link>
    <IndexerList under="/settings/connections/indexers" reorderable />
    <p className={styles.detail}>
      When Indexers offer the same Release, the one higher in this list is preferred.
      Reordering saves immediately. Each Indexer has its own connection, categories and daily query budget.
    </p>
  </SettingsPage>
}
