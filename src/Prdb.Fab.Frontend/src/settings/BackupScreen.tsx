import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'

import { exportBackup } from '../api/client.ts'
import { SettingsPage } from './SettingsPage.tsx'
import formStyles from '../onboarding/Onboarding.module.css'

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
      lede="One file holding everything about this installation that cannot be fetched again — and nothing that can be."
    >
      <h2 className={formStyles.heading}>What is in the file</h2>
      <p className={formStyles.hint}>
        Your settings, every indexer with its address and key, the SABnzbd
        connection and its path mapping, the prdb key, every automation rule
        including the disabled ones, the review queue, and the local record of
        what was downloaded, what was filed where, which releases are used up and
        what has already been reported to prdb.
      </p>
      <p className={formStyles.hint}>
        Not in it, because all of it can be fetched again: the indexer cache,
        cached artwork, prdb&rsquo;s own catalogue, and the video files
        themselves. The backup is a file, not an archive of your library.
      </p>

      <h2 className={formStyles.heading}>It is readable, and that is deliberate</h2>
      <p className={formStyles.warning}>
        <strong>
          The file is plain JSON and the credentials in it are readable. Anyone
          who has the file has your prdb key, your SABnzbd key and every indexer
          key.
        </strong>{' '}
        Nothing here encrypts it, so treat it exactly as you treat the data
        volume: keep it somewhere that encrypts what it holds. Whatever you back
        up with already does — restic, borg, an encrypted share — and doing it
        there means one key you manage rather than one more passphrase to lose at
        the moment you need the file most.
      </p>
      <p className={formStyles.hint}>
        Being readable is what makes it useful when a restore will not complete:
        you can open it and see what is actually in there.
      </p>

      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setSaved(null)
          write.mutate()
        }}
      >
        <label>
          <input
            type="checkbox"
            checked={understood}
            onChange={(event) => { setUnderstood(event.target.checked); setSaved(null) }}
          />{' '}
          I understand the file holds my keys in readable form
        </label>

        {write.isError && <p className={formStyles.refusal}>{String(write.error)}</p>}
        {saved && (
          <p className={formStyles.done}>
            Exported as <code>{saved}</code>. Your browser has saved it wherever it
            puts downloads &mdash; move it somewhere that encrypts it.
          </p>
        )}

        <button
          className={formStyles.button}
          type="submit"
          disabled={!understood || write.isPending}
        >
          {write.isPending ? 'Writing the backup…' : 'Export backup'}
        </button>
      </form>

      <h2 className={formStyles.heading}>Restoring it</h2>
      <p className={formStyles.hint}>
        A restore runs on an installation that holds nothing yet, before a
        password exists &mdash; the login credential is inside the file, so a
        fresh container offers it as the second way to begin setting up. It
        refuses an installation that already holds an indexer, an automation rule
        or a library entry, and names what it found. There is nothing to merge
        into an installation you are already using.
      </p>
    </SettingsPage>
  )
}
