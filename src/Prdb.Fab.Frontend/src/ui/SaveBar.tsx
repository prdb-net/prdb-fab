import type { ReactNode } from 'react'

import styles from './Form.module.css'

/**
 * The submit control, what it is waiting for, and its verdict.
 *
 * Three things it settles that were settled differently on every screen. Save
 * is not offered when nothing has been edited, so pressing it always means
 * something. It says *which* of its reasons it is disabled for rather than
 * being inert. And it is sticky: ADR 0036 put this application in library mode
 * with `<BrowserRouter>`, so there is no `useBlocker` to warn on leaving a
 * dirty form — this bar staying visible for as long as there is something
 * unsaved is the answer instead.
 */
export function SaveBar({
  label,
  dirty,
  pending,
  disabled = false,
  blocked,
  children,
}: {
  label: string
  /** Whether anything has been edited since the last save or read. */
  dirty: boolean
  pending: boolean
  /** A reason of the form's own — an empty required field, say. */
  disabled?: boolean
  /** What to say when `disabled` is true and it is not obvious. */
  blocked?: ReactNode
  /** The verdict, where there is one. */
  children?: ReactNode
}) {
  return (
    <div className={styles.saveBar}>
      <button
        className={styles.button}
        type="submit"
        disabled={pending || disabled || !dirty}
      >
        {pending ? 'Saving…' : label}
      </button>

      <p className={styles.saveState}>
        {pending
          ? 'Saving.'
          : disabled
            ? blocked ?? 'Something above is not answered yet.'
            : dirty
              ? 'There are unsaved changes.'
              : 'Nothing has changed.'}
      </p>

      {children && <div className={styles.saveVerdict}>{children}</div>}
    </div>
  )
}
