import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import { savePrdbKey, type PrdbConnectionVerdict } from '../api/client.ts'
import { Field, formStyles } from '../ui/Form.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { connectionsKey } from './state.ts'

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
    onError: () => setFailure('Could not confirm the prdb connection was saved. Check your connection to fab and try again.'),
  })

  const asking = verdict?.outcome === 'AnotherAccount'

  // A stored key with an empty field is a submit that re-checks what is there,
  // which is a real act — so it counts as something to do even untouched.
  const dirty = apiKey.trim().length > 0 || keyIsStored

  return (
    <form
      className={formStyles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)
        // A confirmation covers the key that was shown, so re-submitting the
        // same field is what carries it back.
        submit.mutate({ confirm: asking })
      }}
    >
      <Field
        label="Your prdb API key"
        hint={keyIsStored ? 'A key is stored. Leave empty to keep it.' : 'Find the key on your prdb account page.'}
        help="The key is checked against prdb before it is stored. Saving with an empty field re-checks the stored key."
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
              setVerdict(null)
            }}
          />
        )}
      </Field>

      <SaveBar
        label={asking ? 'Yes, use this account' : submitLabel}
        dirty={dirty}
        pending={submit.isPending}
        disabled={!dirty}
        blocked="Enter your prdb API key."
      >
        <PrdbVerdict verdict={verdict} />
        {failure && <Verdict tone="refusal">{failure}</Verdict>}
      </SaveBar>
    </form>
  )
}

function PrdbVerdict({ verdict }: { verdict: PrdbConnectionVerdict | null }) {
  if (!verdict) {
    return null
  }

  if (verdict.outcome === 'Saved') {
    return <Verdict tone="done">{verdict.detail}</Verdict>
  }

  if (verdict.outcome === 'AnotherAccount') {
    return <Verdict tone="confirmed">{verdict.detail}</Verdict>
  }

  return (
    <Verdict tone="refusal">
      {verdict.detail}
      {verdict.retryAfterSeconds != null && ` Try again in ${verdict.retryAfterSeconds} seconds.`}
    </Verdict>
  )
}
