import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import {
  addIndexer,
  editIndexer,
  type ConfiguredIndexer,
  type IndexerConnectionVerdict,
} from '../api/client.ts'
import { connectionsKey, indexersKey } from './state.ts'
import styles from './Onboarding.module.css'

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
    onError: (error) => setFailure(String(error)),
  })

  return (
    <form
      className={styles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)
        submit.mutate()
      }}
    >
      <label className={styles.label} htmlFor="indexer-url">
        The indexer's API address
      </label>
      <input
        id="indexer-url"
        className={styles.field}
        type="url"
        placeholder="https://indexer.example/api"
        autoComplete="off"
        spellCheck={false}
        value={url}
        onChange={(event) => setUrl(event.target.value)}
      />
      <p className={styles.hint}>
        Usually the site address with <code>/api</code> on the end, but not
        always &mdash; some serve it somewhere else, so it is asked for whole.
      </p>

      <label className={styles.label} htmlFor="indexer-key">
        Your API key there
      </label>
      <input
        id="indexer-key"
        className={styles.field}
        type="text"
        autoComplete="off"
        spellCheck={false}
        value={apiKey}
        onChange={(event) => setApiKey(event.target.value)}
      />
      <p className={styles.hint}>
        {indexer ? (
          <>
            A key is stored. Leave this empty to keep it &mdash; keys are never
            sent back to the browser, so there is nothing here to read.{' '}
          </>
        ) : null}
        Checked with a real search rather than with a capabilities call: most
        indexers answer that one to anybody, so it proves nothing about a key.
      </p>

      <label className={styles.label} htmlFor="indexer-name">
        What to call it
      </label>
      <input
        id="indexer-name"
        className={styles.field}
        type="text"
        placeholder="Its host name"
        autoComplete="off"
        value={name}
        onChange={(event) => setName(event.target.value)}
      />

      {verdict && (
        <p className={verdict.outcome === 'Saved' ? styles.done : styles.refusal}>
          {verdict.detail}
          {verdict.outcome === 'Saved' && verdict.categories.length > 0 && (
            <> Searching: {verdict.categories.join(', ')}.</>
          )}
        </p>
      )}
      {failure && <p className={styles.refusal}>{failure}</p>}

      <button
        className={styles.button}
        type="submit"
        disabled={
          submit.isPending || url.trim().length === 0 || (!indexer && apiKey.trim().length === 0)
        }
      >
        {submitLabel ?? (indexer ? 'Check and save' : 'Check and add')}
      </button>
    </form>
  )
}
