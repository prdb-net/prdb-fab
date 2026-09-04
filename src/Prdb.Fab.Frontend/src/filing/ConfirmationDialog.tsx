import { useEffect, useId, useRef, type ReactNode } from 'react'

import styles from './Filing.module.css'

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

  return <dialog
    ref={dialog}
    className={styles.dialog}
    aria-labelledby={titleId}
    onCancel={(event) => { event.preventDefault(); if (!busy) onCancel() }}
    onClick={(event) => { if (event.currentTarget === event.target && !busy) onCancel() }}
  >
    <h2 id={titleId}>{title}</h2>
    <div className={styles.dialogBody}>{children}</div>
    <div className={styles.dialogActions}>
      <button type="button" className={styles.button} disabled={busy} onClick={onCancel}>Cancel</button>
      <button type="button" className={`${styles.button} ${danger ? styles.dangerButton : styles.primaryButton}`} disabled={busy} onClick={onConfirm}>{confirmLabel}</button>
    </div>
  </dialog>
}
