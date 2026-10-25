import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import {
  addIndexer,
  editIndexer,
  type ConfiguredIndexer,
  type IndexerConnectionVerdict,
} from '../api/client.ts'
import { Field, formStyles } from '../ui/Form.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { connectionsKey, indexersKey } from './state.ts'

/**
 * ADR 0010's search step, and ADR 0020's indexer route: one form, adding a row
 * or correcting one that is there.
 *
 * Editing re-runs the same check with the same verdicts, because it is the same
 * code — which is the whole reason ADR 0020 refused to have this written twice.
 */
export function IndexerForm({
  indexer,
  submitLabel,
  onSaved,
}: {
  /** The row being corrected. Absent when one is being added. */
  indexer?: ConfiguredIndexer
  submitLabel?: string
  onSaved?: () => void
}) {
  const [name, setName] = useState(indexer?.name ?? '')
  const [url, setUrl] = useState(indexer?.url ?? '')
  const [apiKey, setApiKey] = useState('')
  const [verdict, setVerdict] = useState<IndexerConnectionVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const queries = useQueryClient()

  /**
   * A verdict is about what was submitted, so editing any field makes it stale.
   * A green sentence left standing under a form that has been changed since
   * reads as saved when nothing was.
   */
  const forgetVerdict = () => setVerdict(null)

  const submit = useMutation({
    mutationFn: () =>
      indexer
        ? editIndexer(indexer.id, { name, url, apiKey })
        : addIndexer({ name, url, apiKey }),
    onSuccess: async (answer) => {
      setVerdict(answer)

      if (answer.outcome !== 'Saved') {
        await queries.invalidateQueries({ queryKey: indexersKey })
        return
      }

      // A row that was added leaves an empty form behind, because the next
      // thing anyone does with it is add another. A row that was corrected
      // stays on screen as what it now is.
      if (!indexer) {
        setName('')
        setUrl('')
      }

      setApiKey('')

      await Promise.all([
        queries.invalidateQueries({ queryKey: indexersKey }),
        queries.invalidateQueries({ queryKey: connectionsKey }),
      ])

      onSaved?.()
    },
    onError: () => setFailure('Could not confirm the Indexer was saved. Check your connection to fab and try again.'),
  })

  const incomplete =
    url.trim().length === 0 || (!indexer && apiKey.trim().length === 0)
  const dirty =
    apiKey.trim().length > 0
    || url !== (indexer?.url ?? '')
    || name !== (indexer?.name ?? '')

  return (
    <form
      className={formStyles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)
        submit.mutate()
      }}
    >
      <Field
        label="Indexer API URL"
        hint="Enter the complete API URL, usually ending in /api."
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="url"
            placeholder="https://indexer.example/api"
            autoComplete="off"
            spellCheck={false}
            value={url}
            onChange={(event) => {
              setUrl(event.target.value)
              forgetVerdict()
            }}
          />
        )}
      </Field>

      <Field
        label="Indexer API key"
        hint={indexer ? 'A key is stored. Leave empty to keep it.' : 'Find the API key in your Indexer account.'}
        help="fab tests this key with a search before saving, using one request from your Indexer account."
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="text"
            autoComplete="off"
            spellCheck={false}
            value={apiKey}
            onChange={(event) => {
              setApiKey(event.target.value)
              forgetVerdict()
            }}
          />
        )}
      </Field>

      <Field label="Display name">
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="text"
            placeholder="Defaults to the hostname"
            autoComplete="off"
            value={name}
            onChange={(event) => {
              setName(event.target.value)
              forgetVerdict()
            }}
          />
        )}
      </Field>

      <SaveBar
        label={submitLabel ?? (indexer ? 'Check and save' : 'Check and add')}
        dirty={dirty}
        pending={submit.isPending}
        disabled={incomplete}
        blocked={
          url.trim().length === 0
            ? 'Enter the Indexer API URL.'
            : 'Enter an API key to connect this Indexer.'
        }
      >
        {verdict && (
          <Verdict tone={verdict.outcome === 'Saved' ? 'done' : 'refusal'}>
            {verdict.detail}
            {verdict.outcome === 'Saved' && verdict.categories.length > 0 && (
              <> Searching: {verdict.categories.join(', ')}.</>
            )}
          </Verdict>
        )}
        {failure && <Verdict tone="refusal">{failure}</Verdict>}
      </SaveBar>
    </form>
  )
}
