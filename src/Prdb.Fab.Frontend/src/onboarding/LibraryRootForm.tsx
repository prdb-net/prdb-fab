import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { readConnections, saveLibraryRoot, type LibraryRootVerdict } from '../api/client.ts'
import { Field, formStyles } from '../ui/Form.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { connectionsKey } from './state.ts'

/**
 * ADR 0010's second mandatory step: one path, and three checks on it. Two of
 * them refuse; the third warns and continues, because some NAS layouts
 * genuinely put the library and the downloads on different filesystems and
 * refusing them would be refusing a working installation.
 */
export function LibraryRootForm({
  submitLabel = 'Check and continue',
  initialPath = '',
  onSaved,
}: {
  submitLabel?: string
  /**
   * The configured root in settings. Without one, suggest the separate Library
   * directory from the Docker guide; the suggestion still needs verification
   * and saving, so initialPath remains the baseline for dirty tracking.
   */
  initialPath?: string
  onSaved?: () => void
}) {
  const [path, setPath] = useState(initialPath || '/media/library')
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
    onError: () => setFailure('Could not confirm the Library root was saved. Check your connection to fab and try again.'),
  })

  const trimmed = path.trim()
  const dirty = trimmed.length > 0 && trimmed !== initialPath.trim()

  return (
    <form
      className={formStyles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)
        save.mutate()
      }}
    >
      <Field
        label="Library root"
        hint="The Docker setup uses /media/library. Change it if your Library is mounted elsewhere."
        help="The directory must be writable by the user the container runs as. Use the mount path for your Library."
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="text"
            placeholder="/media/library"
            autoComplete="off"
            spellCheck={false}
            value={path}
            onChange={(event) => {
              setPath(event.target.value)

              // A verdict is about the path that was submitted. Editing the
              // field makes it stale.
              setVerdict(null)
            }}
          />
        )}
      </Field>

      {connections.data?.downloadDirectory && (
        <p className={formStyles.hint}>
          Choose a directory separate from <code>{connections.data.downloadDirectory}</code>.
          The Library and SABnzbd download directory must not be the same
          directory or contain one another.
        </p>
      )}

      <SaveBar
        label={submitLabel}
        dirty={dirty}
        pending={save.isPending}
        disabled={trimmed.length === 0}
        blocked="Type the path the library is mounted at in this container."
      >
        {verdict && (
          <Verdict
            tone={
              verdict.outcome === 'Saved'
                ? 'done'
                : verdict.outcome === 'SavedWithWarning'
                  ? 'warning'
                  : 'refusal'
            }
          >
            {verdict.detail}
          </Verdict>
        )}
        {failure && <Verdict tone="refusal">{failure}</Verdict>}
      </SaveBar>
    </form>
  )
}
