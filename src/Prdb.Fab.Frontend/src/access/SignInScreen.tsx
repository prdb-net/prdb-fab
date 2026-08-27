import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import { signIn } from '../api/client.ts'
import { accessStateKey } from './state.ts'
import styles from './Access.module.css'

/**
 * One field, one secret. ADR 0010: there is no user name, because a name
 * nothing checks is a prop.
 */
export function SignInScreen() {
  const [password, setTyped] = useState('')
  const [refusal, setRefusal] = useState<string | null>(null)
  const queries = useQueryClient()

  const submit = useMutation({
    mutationFn: signIn,
    onSuccess: async (verdict) => {
      switch (verdict.outcome) {
        case 'SignedIn':
          setTyped('')
          await queries.invalidateQueries({ queryKey: accessStateKey })
          break

        case 'WrongPassword':
          setRefusal('That is not the password.')
          break

        case 'TooManyAttempts':
          // ADR 0010 rate-limits this because one password with no user name is
          // the easiest thing in the world to try repeatedly. Saying how long
          // is what keeps the owner from reading it as broken.
          setRefusal(
            `Too many attempts. Try again in ${describe(verdict.retryAfterSeconds)}.`,
          )
          break

        case 'NoPasswordYet':
          // The window is open again — someone cleared the password at the host
          // while this tab was sitting here.
          await queries.invalidateQueries({ queryKey: accessStateKey })
          break
      }
    },
    onError: (error) => setRefusal(String(error)),
  })

  return (
    <main className={styles.screen}>
      <h1>prdb-fab</h1>

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
          autoComplete="current-password"
          autoFocus
          value={password}
          onChange={(event) => setTyped(event.target.value)}
        />

        {refusal && <p className={styles.refusal}>{refusal}</p>}

        <button className={styles.button} type="submit" disabled={submit.isPending}>
          Sign in
        </button>
      </form>
    </main>
  )
}

// ASP.NET Core declares an integer as `integer | string`, because the web
// defaults accept a number written as one. The document is right and the
// frontend says so rather than casting the type away.
function describe(seconds: number | string | null | undefined): string {
  const wait = Number(seconds ?? 0)

  if (wait <= 90) {
    return `${Math.max(wait, 1)} seconds`
  }

  return `${Math.ceil(wait / 60)} minutes`
}
