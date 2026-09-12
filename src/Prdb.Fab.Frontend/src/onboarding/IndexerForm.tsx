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
    onError: () => setFailure('The indexer could not be sent. The tool may have stopped; the log says.'),
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
        label="The indexer's API address"
        hint={
          <>
            Usually the site address with <code>/api</code> on the end, but not
            always &mdash; some serve it somewhere else, so it is asked for whole.
          </>
        }
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
        label="Your API key there"
        hint={
          <>
            {indexer ? (
              <>
                A key is stored. Leave this empty to keep it &mdash; keys are never
                sent back to the browser, so there is nothing here to read.{' '}
              </>
            ) : null}
            Checked with a real search rather than with a capabilities call: most
            indexers answer that one to anybody, so it proves nothing about a key.
          </>
        }
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

      <Field label="What to call it">
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="text"
            placeholder="Its host name"
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
            ? 'The address is needed before anything can be checked.'
            : 'A key is needed to add an indexer, because the check is a real search.'
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
