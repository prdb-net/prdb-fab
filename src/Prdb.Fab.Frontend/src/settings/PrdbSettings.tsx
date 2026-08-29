import { useQuery } from '@tanstack/react-query'

import { readConnections } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { PrdbForm } from '../onboarding/PrdbForm.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import { PageLoading } from '../shell/LoadingScreen.tsx'

/**
 * ADR 0020: the onboarding step, wrapped in *save* instead of *continue*. The
 * form is the same file — a field added later to one of two implementations is
 * a field missing from the other.
 */
export function PrdbSettings() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })

  if (connections.isPending) {
    return <PageLoading label="Loading prdb connection" />
  }

  return (
    <SettingsPage
      title="prdb"
      lede="Where the catalogue, the wanted list and the artwork come from. Saving re-checks the key against prdb, and a key it refuses is not stored."
      back="/settings/connections"
      backLabel="Connections"
    >
      <PrdbForm submitLabel="Check and save" keyIsStored={connections.data?.prdbConfigured === true} />
    </SettingsPage>
  )
}
