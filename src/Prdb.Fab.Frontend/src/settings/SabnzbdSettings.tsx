import { useQuery } from '@tanstack/react-query'

import { readConnections, type ConnectionsState } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { SabnzbdForm } from '../onboarding/SabnzbdForm.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import styles from './Settings.module.css'

/**
 * ADR 0020: the same form as the onboarding step, and the same order within it
 * — the category is answered before the mapping, because it decides which of
 * SABnzbd's folders is being mapped.
 *
 * This is also the route a Gap lands on when SABnzbd was skipped during setting
 * up, which is why it is a route of its own rather than a section of the
 * Connections page.
 */
export function SabnzbdSettings() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })

  if (connections.isPending) {
    return <PageLoading label="Loading SABnzbd connection" />
  }

  const held = connections.data

  return (
    <SettingsPage
      title="SABnzbd"
      lede="What downloads, and where its finished folder is in this container. The mapping is verified rather than collected, so a wrong answer is refused here instead of turning up as a download that hangs."
      back="/settings/connections"
      backLabel="Connections"
    >
      <SabnzbdForm
        submitLabel="Check and save"
        stored={{
          url: held?.sabnzbdUrl ?? null,
          category: held?.sabnzbdCategory ?? null,
          downloadDirectory: held?.downloadDirectory ?? null,
          keyIsStored: held?.sabnzbdConfigured === true,
        }}
      />

      <Stored connections={held} />
    </SettingsPage>
  )
}

/**
 * What is stored right now, which the form above cannot say for itself: the
 * category is chosen from a list that is only fetched once the address and the
 * key have been checked, so until then the page shows two filled fields and
 * nothing about the two answers behind them.
 *
 * It is read from the same query the form writes to, so a save moves it — which
 * is what tells the difference between a check that passed and a check that was
 * stored.
 */
function Stored({ connections }: { connections: ConnectionsState | undefined }) {
  if (connections?.sabnzbdConfigured !== true) {
    return (
      <p className={styles.note}>
        {connections?.sabnzbdSkipped === true
          ? 'Nothing is stored: this step was skipped during setting up, so nothing is downloaded.'
          : 'Nothing is stored yet, so nothing is downloaded.'}
      </p>
    )
  }

  return (
    <p className={styles.note}>
      Stored: {connections.sabnzbdUrl}, category {connections.sabnzbdCategory}. SABnzbd
      finishes that category in {connections.completedRoot}, which is{' '}
      {connections.downloadDirectory} here.
    </p>
  )
}
