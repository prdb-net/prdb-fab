import { useEffect, useId, useRef, type ReactNode } from 'react'

import styles from './Form.module.css'

/**
 * The modal every destructive act on this surface goes through.
 *
 * Moved here from `filing/` by ADR 0058, unchanged, because it was already the
 * application's answer to this question and the settings surface was reaching
 * for `window.confirm` instead — which cannot carry the sentence a preview
 * endpoint returns, cannot be styled, and reads as the browser rather than as
 * the tool at the moment somebody is deciding whether to delete something.
 *
 * A native `<dialog>`: the backdrop, the focus trap and Escape are the
 * platform's, and Escape and a backdrop click are both cancels — except while
 * the act is running, when neither is, since the act is not cancellable.
 */
export function ConfirmationDialog({
  title,
  confirmLabel,
  busy = false,
  danger = false,
  onCancel,
  onConfirm,
  children,
}: {
  title: string
  confirmLabel: string
  busy?: boolean
  danger?: boolean
  onCancel: () => void
  onConfirm: () => void
  children: ReactNode
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  return (
    <dialog
      ref={dialog}
      className={styles.dialog}
      aria-labelledby={titleId}
      onCancel={(event) => {
        event.preventDefault()
        if (!busy) onCancel()
      }}
      onClick={(event) => {
        if (event.currentTarget === event.target && !busy) onCancel()
      }}
    >
      <h2 id={titleId}>{title}</h2>
      <div className={styles.dialogBody}>{children}</div>
      <div className={styles.dialogActions}>
        <button type="button" className={styles.button} disabled={busy} onClick={onCancel}>
          Cancel
        </button>
        <button
          type="button"
          className={`${styles.button} ${danger ? styles.dangerButton : styles.primaryButton}`}
          disabled={busy}
          onClick={onConfirm}
        >
          {confirmLabel}
        </button>
      </div>
    </dialog>
  )
}
