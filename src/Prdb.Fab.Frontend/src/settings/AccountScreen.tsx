import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'

import { changePassword, type ChangePasswordVerdict } from '../api/client.ts'
import { SignOutButton } from '../access/SignOutButton.tsx'
import { Field, formStyles } from '../ui/Form.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

/**
 * ADR 0020's Account route: the one page on this surface with a security
 * consequence.
 *
 * Three parts, in the order somebody arrives at them — the password, this
 * browser's session, and getting back in when the password is gone. They were
 * one column of form fields and grey paragraphs, with the sign-out button
 * directly under the submit of the form above it: two adjacent buttons, one of
 * which ends the session and one of which changes the password.
 */
export function AccountScreen() {
  return (
    <SettingsPage
      title="Account"
      lede="Manage access to this installation."
      help="This installation uses one shared login password. No username is required."
    >
      <PasswordChange />

      <section className={styles.part}>
        <h2 className={styles.heading}>This browser</h2>
        <p className={styles.detail}>
          Signing out ends this browser&apos;s session. To end sessions on other
          devices, change the password.
        </p>
        <SignOutButton />
      </section>

      <section className={styles.part}>
        <h2 className={styles.heading}>If the password is lost</h2>
        <p className={styles.detail}>
          Reset the password on the machine running the container:
        </p>
        <ol className={styles.recovery}>
          <li>
            Start the container once with <code>FAB_RESET_PASSWORD=true</code>.
          </li>
          <li>
            This clears the password and signs out all sessions. API keys,
            Indexer settings and the Library are kept.
          </li>
          <li>
            <strong>Remove the variable and restart.</strong> Left in place, the
            next restart clears the password you are about to set.
          </li>
          <li>Open fab in your browser and set a new password.</li>
        </ol>
      </section>
    </SettingsPage>
  )
}

function PasswordChange() {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [verdict, setVerdict] = useState<ChangePasswordVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  const submit = useMutation({
    mutationFn: () => changePassword(current, next),
    onSuccess: (answer) => {
      setVerdict(answer)

      if (answer.outcome === 'Changed') {
        setCurrent('')
        setNext('')
      }
    },
    onError: () =>
      setFailure('Could not confirm the password change. Try signing in with the new password before changing it again.'),
  })

  const incomplete = current.length === 0 || next.length === 0

  return (
    <section className={styles.part}>
      <h2 className={styles.heading}>The password</h2>

      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setFailure(null)
          setVerdict(null)
          submit.mutate()
        }}
      >
        <Field
          label="Current password"
          help={
            <>
              Enter the current password to authorize this change.
            </>
          }
        >
          {(id) => (
            <input
              id={id}
              className={formStyles.field}
              type="password"
              autoComplete="current-password"
              value={current}
              onChange={(event) => setCurrent(event.target.value)}
            />
          )}
        </Field>

        <Field label="New password">
          {(id) => (
            <input
              id={id}
              className={formStyles.field}
              type="password"
              autoComplete="new-password"
              value={next}
              onChange={(event) => setNext(event.target.value)}
            />
          )}
        </Field>

        {/*
          The two consequences describe the act rather than either field, so
          they belong at the control that performs it. They used to sit under
          the second input looking like a note about what to type there.
        */}
        <p className={styles.detail}>
          Changing the password <strong>signs out all other sessions</strong>.
          This browser stays signed in.
        </p>

        <SaveBar
          label="Change the password"
          dirty={!incomplete}
          pending={submit.isPending}
          disabled={incomplete}
          blocked="Enter both your current and new passwords."
        >
          <ChangeVerdict verdict={verdict} />
          {failure && <Verdict tone="refusal">{failure}</Verdict>}
        </SaveBar>
      </form>
    </section>
  )
}

function ChangeVerdict({ verdict }: { verdict: ChangePasswordVerdict | null }) {
  if (!verdict) {
    return null
  }

  if (verdict.outcome === 'Changed') {
    const ended = Number(verdict.sessionsEnded)

    return (
      <Verdict tone="done">
        The password has been changed.{' '}
        {ended === 0
          ? 'There were no other active sessions.'
          : `${ended} other session${ended === 1 ? '' : 's'} signed out.`}
      </Verdict>
    )
  }

  if (verdict.outcome === 'TooManyAttempts') {
    const minutes = Math.max(1, Math.ceil(Number(verdict.retryAfterSeconds) / 60))

    return (
      <Verdict tone="refusal">
        Too many password attempts were made. Try again in about {minutes}{' '}
        {minutes === 1 ? 'minute' : 'minutes'}.
      </Verdict>
    )
  }

  return (
    <Verdict tone="refusal">
      {verdict.outcome === 'WrongPassword'
        ? 'The current password is incorrect. Your password was not changed.'
        : verdict.refusal}
    </Verdict>
  )
}
