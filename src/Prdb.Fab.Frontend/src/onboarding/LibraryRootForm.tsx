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
   * What the field opens with. Empty in onboarding, where there is no answer
   * yet; the root in force in the settings, where there is one and a field that
   * does not name it is a field offering to replace something unnamed.
   */
  initialPath?: string
  onSaved?: () => void
}) {
  const [path, setPath] = useState(initialPath)
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
    onError: () => setFailure('The path could not be sent. The tool may have stopped; the log says.'),
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
        label="Where the library goes"
        hint={
          <>
            The path inside this container, which is whatever you mounted your
            library at. It has to be writable by the user the container runs as,
            and it is the only directory this tool writes to.
          </>
        }
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="text"
            placeholder="/library"
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
          It may not be inside <code>{connections.data.downloadDirectory}</code>,
          or contain it &mdash; that is where SABnzbd's finished downloads
          arrive, and filing moves videos out of there.
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
