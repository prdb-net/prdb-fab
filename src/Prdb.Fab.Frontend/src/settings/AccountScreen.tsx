import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'

import { changePassword, type ChangePasswordVerdict } from '../api/client.ts'
import { SignOutButton } from '../access/SignOutButton.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import formStyles from '../onboarding/Onboarding.module.css'
import styles from './Settings.module.css'

/**
 * ADR 0020's Account route: ADR 0010's password change, which requires the
 * current password and ends every other session. That is the only lever
 * somebody has who suspects a session they did not open, so what it did is said
 * plainly rather than left to be assumed.
 */
export function AccountScreen() {
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
    onError: (error) => setFailure(String(error)),
  })

  return (
    <SettingsPage
      title="Account"
      lede="One password and no user name, belonging to this installation rather than to an account somewhere."
    >
      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setFailure(null)
          setVerdict(null)
          submit.mutate()
        }}
      >
        <label className={formStyles.label} htmlFor="current-password">
          The password you use now
        </label>
        <input
          id="current-password"
          className={formStyles.field}
          type="password"
          autoComplete="current-password"
          value={current}
          onChange={(event) => setCurrent(event.target.value)}
        />
        <p className={formStyles.hint}>
          Asked for even though you are signed in: a session left open somewhere
          else must not be a way to lock you out of your own installation.
        </p>

        <label className={formStyles.label} htmlFor="new-password">
          The new one
        </label>
        <input
          id="new-password"
          className={formStyles.field}
          type="password"
          autoComplete="new-password"
          value={next}
          onChange={(event) => setNext(event.target.value)}
        />
        <p className={formStyles.hint}>
          Changing it ends every other session at once. This browser stays signed
          in.
        </p>

        <Verdict verdict={verdict} />
        {failure && <p className={formStyles.refusal}>{failure}</p>}

        <button
          className={formStyles.button}
          type="submit"
          disabled={submit.isPending || current.length === 0 || next.length === 0}
        >
          Change the password
        </button>
      </form>

      <h2 className={styles.heading}>This browser</h2>
      <p className={styles.detail}>
        Signing out ends this session now rather than at its expiry, and the
        cookie it was carried by stops working with it.
      </p>
      <SignOutButton />

      <p className={styles.note}>
        If the password is lost, it is recovered at the host rather than over the
        network: start the container once with{' '}
        <code>FAB_RESET_PASSWORD=true</code>, then remove the variable again.
      </p>
    </SettingsPage>
  )
}

function Verdict({ verdict }: { verdict: ChangePasswordVerdict | null }) {
  if (!verdict) {
    return null
  }

  if (verdict.outcome === 'Changed') {
    const ended = Number(verdict.sessionsEnded)

    return (
      <p className={formStyles.done}>
        The password has been changed.{' '}
        {ended === 0
          ? 'Nothing else was signed in.'
          : `${ended} other session${ended === 1 ? '' : 's'} ended with it.`}
      </p>
    )
  }

  return (
    <p className={formStyles.refusal}>
      {verdict.outcome === 'WrongPassword'
        ? 'That is not the password you use now, so nothing was changed.'
        : verdict.refusal}
    </p>
  )
}
