import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { addIndexer, listIndexers, type IndexerConnectionVerdict } from '../api/client.ts'
import { connectionsKey, indexersKey } from './state.ts'
import styles from './Onboarding.module.css'

/**
 * ADR 0010's search step. Several indexers, added one at a time, each its own
 * row — ADR 0002 identifies a release by the indexer together with that
 * indexer's own id for it, so the rows matter from the first one.
 */
export function IndexerForm() {
  const [name, setName] = useState('')
  const [url, setUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [verdict, setVerdict] = useState<IndexerConnectionVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const queries = useQueryClient()

  const indexers = useQuery({ queryKey: indexersKey, queryFn: listIndexers })

  const add = useMutation({
    mutationFn: () => addIndexer({ name, url, apiKey }),
    onSuccess: async (answer) => {
      setVerdict(answer)

      if (answer.outcome !== 'Saved') {
        return
      }

      setName('')
      setUrl('')
      setApiKey('')

      await Promise.all([
        queries.invalidateQueries({ queryKey: indexersKey }),
        queries.invalidateQueries({ queryKey: connectionsKey }),
      ])
    },
    onError: (error) => setFailure(String(error)),
  })

  return (
    <>
      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setFailure(null)
          add.mutate()
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
          disabled={add.isPending || url.trim().length === 0 || apiKey.trim().length === 0}
        >
          Check and add
        </button>
      </form>

      {indexers.data && indexers.data.length > 0 && (
        <ul className={styles.rows}>
          {indexers.data.map((indexer) => (
            <li key={indexer.id}>
              {indexer.name}
              <br />
              <span className={styles.rowDetail}>
                {indexer.url} &mdash; {indexer.categories.split(',').join(', ')}
              </span>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
