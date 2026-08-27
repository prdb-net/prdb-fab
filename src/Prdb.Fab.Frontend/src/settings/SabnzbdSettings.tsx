import { useQuery } from '@tanstack/react-query'

import { readConnections } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { SabnzbdForm } from '../onboarding/SabnzbdForm.tsx'
import { SettingsPage } from './SettingsPage.tsx'

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
    return null
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
    </SettingsPage>
  )
}
