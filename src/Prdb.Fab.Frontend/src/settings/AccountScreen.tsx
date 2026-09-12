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
      lede="One password and no user name, belonging to this installation rather than to an account somewhere."
    >
      <PasswordChange />

      <section className={styles.part}>
        <h2 className={styles.heading}>This browser</h2>
        <p className={styles.detail}>
          Signing out ends <strong>this</strong> session now rather than at its
          expiry, and the cookie it was carried by stops working with it. Every
          other session is untouched &mdash; ending those is what changing the
          password above does.
        </p>
        <SignOutButton />
      </section>

      <section className={styles.part}>
        <h2 className={styles.heading}>If the password is lost</h2>
        <p className={styles.detail}>
          It is recovered at the host rather than over the network, because a
          second way in over the network is a second way to configure wrongly.
        </p>
        <ol className={styles.recovery}>
          <li>
            Start the container once with <code>FAB_RESET_PASSWORD=true</code>.
          </li>
          <li>
            It clears the password and every session, and says so loudly in the
            log. Everything else &mdash; the prdb key, the indexers, the library
            &mdash; is exactly as it was.
          </li>
          <li>
            <strong>Remove the variable and restart.</strong> Left in place, the
            next restart clears the password you are about to set.
          </li>
          <li>Open the tool and set a new password, as at first run.</li>
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
      setFailure('The change could not be sent. The tool may have stopped; the log says.'),
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
          label="The password you use now"
          hint={
            <>
              Asked for even though you are signed in (ADR 0010): a session left
              open somewhere else must not be a way to lock you out of your own
              installation.
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

        <Field label="The new one">
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
          Changing it <strong>ends every other session at once</strong>, which is
          the only lever there is against a session somebody else opened. This
          browser stays signed in.
        </p>

        <SaveBar
          label="Change the password"
          dirty={!incomplete}
          pending={submit.isPending}
          disabled={incomplete}
          blocked="Both the password you use now and the new one are needed."
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
          ? 'Nothing else was signed in.'
          : `${ended} other session${ended === 1 ? '' : 's'} ended with it.`}
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
        ? 'That is not the password you use now, so nothing was changed.'
        : verdict.refusal}
    </Verdict>
  )
}
