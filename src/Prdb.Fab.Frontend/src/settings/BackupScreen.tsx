import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'

import { exportBackup } from '../api/client.ts'
import { Switch, formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { InfoHelp } from '../ui/InfoHelp.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

/**
 * ADR 0009's export, and ADR 0057's sentence in front of it.
 *
 * There is no passphrase. What stands where one used to is an acknowledgement:
 * the file holds every credential this installation has, in readable form, and
 * that has to be understood before the file exists rather than discovered after
 * it has been copied somewhere. One thing to understand instead of one thing to
 * remember.
 */
export function BackupScreen() {
  const [understood, setUnderstood] = useState(false)
  const [saved, setSaved] = useState<string | null>(null)
  const write = useMutation({
    mutationFn: exportBackup,
    onSuccess: (name) => setSaved(name),
  })

  return (
    <SettingsPage
      title="Backup"
      lede="Export your settings and records. Video files are not included."
    >
      <InfoHelp label="What the backup contains">
      <p className={formStyles.hint}>
        Your settings, every indexer with its address and key, the SABnzbd
        connection and its path mapping, the prdb key, every automation rule
        including the disabled ones, the review queue, and the local record of
        what was downloaded, what was filed where, which releases are used up and
        what has already been reported to prdb.
      </p>
      <p className={formStyles.hint}>
        The Indexer Cache, cached artwork, prdb Catalogue and video files are
        excluded. Back up your video files separately.
      </p>

      </InfoHelp>

      <h2 className={styles.heading}>Keep your backup private</h2>
      <Verdict tone="warning">
        <strong>
          The file is plain JSON and the credentials in it are readable. Anyone
          who has the file has your prdb key, your SABnzbd key and every indexer
          key.
        </strong>{' '}
        Store it in a private, encrypted location.
      </Verdict>

      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setSaved(null)
          write.mutate()
        }}
      >
        <Switch
          checked={understood}
          onChange={(checked) => { setUnderstood(checked); setSaved(null) }}
          label="I understand the file holds my keys in readable form"
        />

        {write.isError && (
          <Verdict tone="refusal">
            The backup export did not complete. Check your browser downloads before trying again.
          </Verdict>
        )}
        {saved && (
          <Verdict tone="done">
            Download started: <code>{saved}</code>. Once it finishes, move the file
            to a private, encrypted location.
          </Verdict>
        )}

        <button
          className={formStyles.button}
          type="submit"
          disabled={!understood || write.isPending}
        >
          {write.isPending ? 'Writing the backup…' : 'Export backup'}
        </button>
      </form>

      <InfoHelp label="How to restore a backup">
      <p className={formStyles.hint}>
        Start a fresh container and choose Restore before setting a password.
        The backup restores the login password too. Restore requires an empty
        installation: existing Indexers, Automation Rules or Library Entries
        prevent it. Backups cannot be merged into an installation in use.
      </p>
      </InfoHelp>
    </SettingsPage>
  )
}
