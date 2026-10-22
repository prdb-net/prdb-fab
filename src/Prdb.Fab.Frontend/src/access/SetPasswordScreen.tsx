import { InfoHelp } from '../ui/InfoHelp.tsx'
import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import { setPassword } from '../api/client.ts'
import { accessStateKey } from './state.ts'
import { RestoreScreen } from './RestoreScreen.tsx'
import styles from './Access.module.css'

/**
 * ADR 0010's window, seen from the browser: the one write anyone may make
 * without being signed in, and the act that closes it for good.
 */
export function SetPasswordScreen() {
  // ADR 0010's step 1 is a fork rather than a question, and it lives here
  // because both sides of it are gated on the same condition — no password
  // exists yet — and that is exactly the condition this screen is shown under.
  const [restoring, setRestoring] = useState(false)
  const [password, setChosen] = useState('')
  const [repeated, setRepeated] = useState('')
  const [refusal, setRefusal] = useState<string | null>(null)
  const queries = useQueryClient()

  const submit = useMutation({
    mutationFn: setPassword,
    onSuccess: async (verdict) => {
      if (verdict.outcome === 'Set') {
        // Setting it signed us in, so the page decides again and lands on
        // whatever onboarding step is next.
        await queries.invalidateQueries({ queryKey: accessStateKey })

        return
      }

      setRefusal(
        verdict.refusal ??
          'This installation already has a password. Sign in or reset it on the machine running the container.',
      )
    },
    onError: (error) => setRefusal(String(error)),
  })

  // Checked here and nowhere else, because it is not a rule about the password
  // — the server has no second field to compare. It is a guard against a typo
  // in the one secret that cannot be recovered over the network.
  const mismatched = repeated.length > 0 && repeated !== password

  if (restoring) {
    return <RestoreScreen onBack={() => setRestoring(false)} />
  }

  return (
    <main className={styles.screen}>
      <h1>prdb-fab</h1>
      <p className={styles.lede}>
        Set the password used to sign in to this installation. No username is
        required. You can change the password later in Settings → Account.
      </p>

      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setRefusal(null)
          submit.mutate(password)
        }}
      >
        <label className={styles.label} htmlFor="password">
          Password
        </label>
        <input
          id="password"
          className={styles.field}
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(event) => setChosen(event.target.value)}
        />

        <label className={styles.label} htmlFor="repeat">
          Confirm password
        </label>
        <input
          id="repeat"
          className={styles.field}
          type="password"
          autoComplete="new-password"
          value={repeated}
          onChange={(event) => setRepeated(event.target.value)}
        />

        {mismatched && <p className={styles.refusal}>The passwords do not match.</p>}
        {refusal && <p className={styles.refusal}>{refusal}</p>}

        <button
          className={styles.button}
          type="submit"
          disabled={submit.isPending || mismatched || repeated.length === 0}
        >
          Set password
        </button>
      </form>

      <p className={styles.fork}>
        Already have a backup?{' '}
        <button
          className={styles.forkButton}
          type="button"
          onClick={() => setRestoring(true)}
        >
          Restore a backup
        </button>{' '}
        to recover your settings and login password.
      </p>

      <div className={styles.note}><InfoHelp label="If you forget your password">
        If you forget the password, start the container once with{' '}
        <code>FAB_RESET_PASSWORD=true</code>. This clears the password and all
        sessions while keeping your keys, settings and Library. Remove the
        variable and restart before setting a new password.
      </InfoHelp></div>
    </main>
  )
}
