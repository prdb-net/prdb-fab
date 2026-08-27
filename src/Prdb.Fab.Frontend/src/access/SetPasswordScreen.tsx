import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import { setPassword } from '../api/client.ts'
import { accessStateKey } from './state.ts'
import styles from './Access.module.css'

/**
 * ADR 0010's window, seen from the browser: the one write anyone may make
 * without being signed in, and the act that closes it for good.
 */
export function SetPasswordScreen() {
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
          'This installation already has a password. Sign in with it, or clear it at the host.',
      )
    },
    onError: (error) => setRefusal(String(error)),
  })

  // Checked here and nowhere else, because it is not a rule about the password
  // — the server has no second field to compare. It is a guard against a typo
  // in the one secret that cannot be recovered over the network.
  const mismatched = repeated.length > 0 && repeated !== password

  return (
    <main className={styles.screen}>
      <h1>prdb-fab</h1>
      <p className={styles.lede}>
        Choose the password this installation is reached by. There is no user
        name and no second account — one password, and it is set here once.
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
          A password
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
          The same password again
        </label>
        <input
          id="repeat"
          className={styles.field}
          type="password"
          autoComplete="new-password"
          value={repeated}
          onChange={(event) => setRepeated(event.target.value)}
        />

        {mismatched && <p className={styles.refusal}>The two do not match.</p>}
        {refusal && <p className={styles.refusal}>{refusal}</p>}

        <button
          className={styles.button}
          type="submit"
          disabled={submit.isPending || mismatched || repeated.length === 0}
        >
          Set it
        </button>
      </form>

      <p className={styles.note}>
        Losing it is recovered at the host rather than over the network: start
        the container once with <code>FAB_RESET_PASSWORD=true</code>, which
        clears the password and every session and leaves everything else — the
        prdb key, the indexers, the library — exactly as it was.
      </p>
    </main>
  )
}
