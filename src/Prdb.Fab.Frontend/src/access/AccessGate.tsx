import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'

import { readAccessState } from '../api/client.ts'
import { accessStateKey } from './state.ts'
import { SetPasswordScreen } from './SetPasswordScreen.tsx'
import { SignInScreen } from './SignInScreen.tsx'
import styles from './Access.module.css'

/**
 * ADR 0010: the browser side is **one page that decides for itself what to
 * show** — sign-in, an onboarding step, or the workspace — and one anonymous
 * state endpoint answers what it needs to decide.
 *
 * So this is that decision and the only place it is made. Nothing below it
 * checks whether anybody is signed in; a 401 from anywhere brings the question
 * back here (see `state.ts`).
 */
export function AccessGate({ children }: { children: ReactNode }) {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })

  if (state.isPending) {
    // Deliberately not a spinner. This answers in milliseconds off a table with
    // one row in it, and a flash of something is worse than a blank moment.
    return null
  }

  if (state.isError) {
    return (
      <main className={styles.screen}>
        <h1>prdb-fab</h1>
        <p className={styles.refusal}>
          The tool did not answer. It may still be starting; the log says what
          happened.
        </p>
      </main>
    )
  }

  if (!state.data.passwordSet) {
    return <SetPasswordScreen />
  }

  if (!state.data.signedIn) {
    return <SignInScreen />
  }

  return children
}
