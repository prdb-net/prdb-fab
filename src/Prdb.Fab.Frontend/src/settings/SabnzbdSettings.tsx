import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { readConnections, saveSabnzbd } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { SabnzbdForm } from '../onboarding/SabnzbdForm.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import { ConnectionSection, ConnectionStatus, StoredKey } from './ConnectionPanel.tsx'
import styles from './Settings.module.css'

export function SabnzbdSettings() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const queries = useQueryClient()
  const [editing, setEditing] = useState(false)
  const check = useMutation({
    mutationFn: () => saveSabnzbd({ url: connections.data!.sabnzbdUrl!, apiKey: '', category: connections.data!.sabnzbdCategory!, downloadDirectory: connections.data!.downloadDirectory! }),
    onSuccess: () => queries.invalidateQueries({ queryKey: connectionsKey }),
  })
  return <SettingsPage title="SABnzbd" lede="Download Releases and collect the completed files.">
    <Loaded query={connections} of="the SABnzbd connection">{held => <>
      <ConnectionStatus configured={held.sabnzbdConfigured} outcome={held.sabnzbdLastCheckOutcome} checkedAt={held.sabnzbdLastCheckedAt} successAt={held.sabnzbdLastSuccessAt} checking={check.isPending} onCheck={editing ? undefined : () => check.mutate()} />
      {check.data && <Verdict tone={check.data.outcome === 'Saved' ? 'done' : 'refusal'}>{check.data.detail}</Verdict>}
      {check.isError && <Verdict tone="refusal">Could not check SABnzbd. Try again.</Verdict>}
      <ConnectionSection title="Connection details" description="The address and API key used to reach your downloader." action={held.sabnzbdConfigured && <button className={formStyles.button} type="button" disabled={check.isPending} onClick={() => { check.reset(); setEditing(!editing) }}>{editing ? 'Cancel' : 'Edit settings'}</button>}>
        {held.sabnzbdConfigured && !editing && <>
          <dl className={styles.connectionSummary}><dt>Address</dt><dd>{held.sabnzbdUrl}</dd></dl>
          <StoredKey connection="sabnzbd" hint={held.sabnzbdKeyHint} />
        </>}
        {(editing || !held.sabnzbdConfigured) && <SabnzbdForm submitLabel="Verify and save" stored={{
          url: held.sabnzbdUrl ?? null, category: held.sabnzbdCategory ?? null,
          downloadDirectory: held.downloadDirectory ?? null, keyIsStored: held.sabnzbdConfigured,
        }} onSaved={() => setEditing(false)} />}
      </ConnectionSection>
      {held.sabnzbdConfigured && !editing && <ConnectionSection title="Completed downloads" description="Where SABnzbd puts files, and where fab can reach them.">
        <dl className={styles.connectionSummary}>
          <dt>Category</dt><dd>{held.sabnzbdCategory}</dd>
          <dt>In SABnzbd</dt><dd><code>{held.completedRoot}</code></dd>
          <dt>In fab</dt><dd><code>{held.downloadDirectory}</code></dd>
        </dl>
        <p className={styles.detail}>Both paths must point to the same completed files.</p>
      </ConnectionSection>}
    </>}</Loaded>
  </SettingsPage>
}
