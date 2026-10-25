import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { readConnections } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { SabnzbdForm } from '../onboarding/SabnzbdForm.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

export function SabnzbdSettings() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const [editing, setEditing] = useState(false)
  const [saved, setSaved] = useState(false)

  return <SettingsPage title="SABnzbd" lede="Download Releases and collect the completed files.">
    <Loaded query={connections} of="the SABnzbd connection">{held => <>
      {held.sabnzbdConfigured && !editing && <>
        <dl className={styles.connectionSummary}>
          <dt>Address</dt><dd>{held.sabnzbdUrl}</dd>
          <dt>API key</dt><dd>Stored</dd>
        </dl>
        <h2 className={styles.heading}>Download destination</h2>
        <dl className={styles.connectionSummary}>
          <dt>Category</dt><dd>{held.sabnzbdCategory}</dd>
          <dt>SABnzbd folder</dt><dd><code>{held.completedRoot}</code></dd>
          <dt>Same folder in fab</dt><dd><code>{held.downloadDirectory}</code></dd>
        </dl>
        <p className={styles.detail}>These two paths point to the same completed files.</p>
      </>}
      {held.sabnzbdConfigured && !editing && <button className={formStyles.button} type="button" onClick={() => { setSaved(false); setEditing(true) }}>Edit connection</button>}
      {(editing || !held.sabnzbdConfigured) && <>
        {!held.sabnzbdConfigured && <p className={styles.note}>Connect SABnzbd to enable Downloads.</p>}
        <h2 className={styles.heading}>Connection → Category → Path mapping</h2>
        <SabnzbdForm submitLabel="Check and save" stored={{
          url: held.sabnzbdUrl ?? null,
          category: held.sabnzbdCategory ?? null,
          downloadDirectory: held.downloadDirectory ?? null,
          keyIsStored: held.sabnzbdConfigured,
        }} onSaved={() => { setEditing(false); setSaved(true) }} />
        {held.sabnzbdConfigured && <button className={formStyles.button} type="button" onClick={() => setEditing(false)}>Cancel editing</button>}
      </>}
      {saved && <Verdict tone="done">SABnzbd connection saved.</Verdict>}
    </>}</Loaded>
  </SettingsPage>
}
