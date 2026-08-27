import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { readConnections, saveLibraryRoot, type LibraryRootVerdict } from '../api/client.ts'
import { connectionsKey } from './state.ts'
import styles from './Onboarding.module.css'

/**
 * ADR 0010's second mandatory step: one path, and three checks on it. Two of
 * them refuse; the third warns and continues, because some NAS layouts
 * genuinely put the library and the downloads on different filesystems and
 * refusing them would be refusing a working installation.
 */
export function LibraryRootForm({
  submitLabel = 'Check and continue',
  onSaved,
}: {
  submitLabel?: string
  onSaved?: () => void
}) {
  const [path, setPath] = useState('')
  const [verdict, setVerdict] = useState<LibraryRootVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const queries = useQueryClient()

  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })

  const save = useMutation({
    mutationFn: () => saveLibraryRoot(path),
    onSuccess: async (answer) => {
      setVerdict(answer)

      if (answer.outcome === 'Saved' || answer.outcome === 'SavedWithWarning') {
        await queries.invalidateQueries({ queryKey: connectionsKey })
      }

      // The warning is a sentence somebody has to read, and continuing would
      // take the page it is written on away. ADR 0010 warns without refusing,
      // so the path waits for the step around this form instead.
      if (answer.outcome === 'Saved') {
        onSaved?.()
      }
    },
    onError: (error) => setFailure(String(error)),
  })

  const stored = verdict?.outcome === 'Saved' || verdict?.outcome === 'SavedWithWarning'

  return (
    <form
      className={styles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)
        save.mutate()
      }}
    >
      <label className={styles.label} htmlFor="library-root">
        Where the library goes
      </label>
      <input
        id="library-root"
        className={styles.field}
        type="text"
        placeholder="/library"
        autoComplete="off"
        spellCheck={false}
        value={path}
        onChange={(event) => setPath(event.target.value)}
      />
      <p className={styles.hint}>
        The path inside this container, which is whatever you mounted your
        library at. It has to be writable by the user the container runs as, and
        it is the only directory this tool writes to.
      </p>

      {connections.data?.downloadDirectory && (
        <p className={styles.hint}>
          It may not be inside <code>{connections.data.downloadDirectory}</code>,
          or contain it &mdash; that is where SABnzbd's finished downloads
          arrive, and filing moves videos out of there.
        </p>
      )}

      {verdict && (
        <p
          className={
            verdict.outcome === 'Saved'
              ? styles.done
              : verdict.outcome === 'SavedWithWarning'
                ? styles.warning
                : styles.refusal
          }
        >
          {verdict.detail}
        </p>
      )}
      {failure && <p className={styles.refusal}>{failure}</p>}

      <button
        className={styles.button}
        type="submit"
        disabled={save.isPending || path.trim().length === 0}
      >
        {stored ? 'Stored' : submitLabel}
      </button>
    </form>
  )
}
