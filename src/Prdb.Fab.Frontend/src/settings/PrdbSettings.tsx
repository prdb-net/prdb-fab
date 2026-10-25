import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { readConnections, savePrdbKey } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { PrdbForm } from '../onboarding/PrdbForm.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import { ConnectionSection, ConnectionStatus, StoredKey } from './ConnectionPanel.tsx'

export function PrdbSettings() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const queries = useQueryClient()
  const [editing, setEditing] = useState(false)
  const check = useMutation({
    mutationFn: () => savePrdbKey('', false),
    onSuccess: () => queries.invalidateQueries({ queryKey: connectionsKey }),
  })
  return <SettingsPage title="prdb" lede="Your connection to the Catalogue, Wanted list and artwork.">
    <Loaded query={connections} of="the prdb connection">{held => <>
      <ConnectionStatus configured={held.prdbConfigured} outcome={held.prdbLastCheckOutcome} checkedAt={held.prdbLastCheckedAt} successAt={held.prdbLastSuccessAt} checking={check.isPending} onCheck={editing ? undefined : () => check.mutate()} />
      {check.data && check.data.outcome !== 'Saved' && <Verdict tone="refusal">{check.data.detail}</Verdict>}
      {check.isError && <Verdict tone="refusal">Could not check the connection. Try again.</Verdict>}
      <ConnectionSection title="Access to prdb" description="The API key links this installation to your prdb account." action={held.prdbConfigured && <button className={formStyles.button} type="button" disabled={check.isPending} onClick={() => { check.reset(); setEditing(!editing) }}>{editing ? 'Cancel' : 'Replace key'}</button>}>
        {held.prdbConfigured && !editing && <StoredKey connection="prdb" hint={held.prdbKeyHint} />}
        {(editing || !held.prdbConfigured) && <PrdbForm submitLabel="Verify and save key" keyIsStored={held.prdbConfigured} onSaved={() => setEditing(false)} />}
      </ConnectionSection>
    </>}</Loaded>
  </SettingsPage>
}
