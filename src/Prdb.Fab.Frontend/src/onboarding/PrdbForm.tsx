import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import { savePrdbKey, type PrdbConnectionVerdict } from '../api/client.ts'
import { connectionsKey } from './state.ts'
import styles from './Onboarding.module.css'

/**
 * ADR 0010's mandatory step. Written once here: onboarding puts *continue*
 * around it and the settings route puts *save* around it, and neither writes
 * the form a second time.
 *
 * There is no way past a failure, so there is no "continue anyway" button to
 * look for — the submit is the check, and a refusal leaves the field where it
 * is with the reason under it.
 */
export function PrdbForm({
  submitLabel = 'Check and continue',
  keyIsStored = false,
  onSaved,
}: {
  submitLabel?: string
  /**
   * ADR 0020: keys are write-only. Nothing is ever sent back to the browser, so
   * this is all the form can say about the one that is stored — and leaving the
   * field empty keeps it.
   */
  keyIsStored?: boolean
  /** What the step around this form does once the key is stored. Nothing, in the settings. */
  onSaved?: () => void
}) {
  const [apiKey, setApiKey] = useState('')
  const [verdict, setVerdict] = useState<PrdbConnectionVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const queries = useQueryClient()

  const submit = useMutation({
    mutationFn: ({ confirm }: { confirm: boolean }) => savePrdbKey(apiKey, confirm),
    onSuccess: async (answer) => {
      setVerdict(answer)

      if (answer.outcome === 'Saved') {
        await queries.invalidateQueries({ queryKey: connectionsKey })
        onSaved?.()
      }
    },
    onError: (error) => setFailure(String(error)),
  })

  const asking = verdict?.outcome === 'AnotherAccount'

  return (
    <form
      className={styles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)
        // A confirmation covers the key that was shown, so re-submitting the
        // same field is what carries it back.
        submit.mutate({ confirm: asking })
      }}
    >
      <label className={styles.label} htmlFor="prdb-key">
        Your prdb API key
      </label>
      <input
        id="prdb-key"
        className={styles.field}
        type="text"
        autoComplete="off"
        spellCheck={false}
        value={apiKey}
        onChange={(event) => {
          setApiKey(event.target.value)
          setVerdict(null)
        }}
      />
      <p className={styles.hint}>
        {keyIsStored ? (
          <>
            A key is stored. Leave this empty to keep it &mdash; saving re-checks
            it against prdb either way.{' '}
          </>
        ) : null}
        It is on your prdb account page. This installation checks it against prdb
        before storing it, so a key that is wrong is a wrong key now rather than
        a library that quietly never fills.
      </p>

      <Verdict verdict={verdict} />
      {failure && <p className={styles.refusal}>{failure}</p>}

      <button
        className={styles.button}
        type="submit"
        disabled={submit.isPending || (apiKey.trim().length === 0 && !keyIsStored)}
      >
        {asking ? 'Yes, use this account' : submitLabel}
      </button>
    </form>
  )
}

function Verdict({ verdict }: { verdict: PrdbConnectionVerdict | null }) {
  if (!verdict) {
    return null
  }

  if (verdict.outcome === 'Saved') {
    return <p className={styles.done}>{verdict.detail}</p>
  }

  if (verdict.outcome === 'AnotherAccount') {
    return <p className={styles.confirmed}>{verdict.detail}</p>
  }

  return (
    <p className={styles.refusal}>
      {verdict.detail}
      {verdict.retryAfterSeconds != null && ` prdb asks for ${verdict.retryAfterSeconds} seconds.`}
    </p>
  )
}
