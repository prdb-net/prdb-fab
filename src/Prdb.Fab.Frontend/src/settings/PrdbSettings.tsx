import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { readConnections, savePrdbKey } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { PrdbForm } from '../onboarding/PrdbForm.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

export function PrdbSettings() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const queries = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [saved, setSaved] = useState(false)
  const check = useMutation({
    mutationFn: () => savePrdbKey('', false),
    onSuccess: () => queries.invalidateQueries({ queryKey: connectionsKey }),
  })

  return <SettingsPage title="prdb" lede="Your Catalogue, Wanted list and artwork.">
    <Loaded query={connections} of="the prdb connection">{held => <>
      {held.prdbConfigured && <>
        <dl className={styles.connectionSummary}>
          <dt>API key</dt><dd>Stored</dd>
        </dl>
        <p className={styles.detail}>The stored key is never displayed. Check the connection or replace the key below.</p>
      </>}
      {held.prdbConfigured && !editing && <div className={styles.connectionActions}>
        <button className={formStyles.button} type="button" disabled={check.isPending} onClick={() => { check.reset(); setSaved(false); setEditing(true) }}>Replace API key</button>
        <button className={formStyles.button} type="button" disabled={check.isPending} onClick={() => { setSaved(false); check.mutate() }}>{check.isPending ? 'Checking…' : 'Check connection'}</button>
      </div>}
      {(editing || !held.prdbConfigured) && <>
        <PrdbForm submitLabel="Check and save" keyIsStored={held.prdbConfigured} onSaved={() => { setEditing(false); setSaved(true) }} />
        {held.prdbConfigured && <button className={formStyles.button} type="button" onClick={() => setEditing(false)}>Cancel editing</button>}
      </>}
      {saved && <Verdict tone="done">prdb connection saved.</Verdict>}
      {check.data && <Verdict tone={check.data.outcome === 'Saved' ? 'done' : 'refusal'}>{check.data.detail}</Verdict>}
      {check.isError && <Verdict tone="refusal">Could not check the connection. Try again.</Verdict>}
    </>}</Loaded>
  </SettingsPage>
}
