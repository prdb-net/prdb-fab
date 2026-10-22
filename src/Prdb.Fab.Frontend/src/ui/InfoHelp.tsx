import { useId, useRef, useState, type ReactNode } from 'react'
import styles from './InfoHelp.module.css'

/** Optional explanations expand in place, without covering the form or submitting it. */
export function InfoHelp({ label, heading, children }: {
  label: string
  heading?: ReactNode
  children: ReactNode
}) {
  const [open, setOpen] = useState(false)
  const id = useId()
  const button = useRef<HTMLButtonElement>(null)

  return <div className={styles.help} onKeyDown={(event) => {
    if (open && event.key === 'Escape') {
      event.stopPropagation()
      setOpen(false)
      button.current?.focus()
    }
  }}>
    <div className={styles.heading}>
      {heading ?? <span className={styles.label}>{label}</span>}
      <button type="button" ref={button} className={styles.button}
        aria-label={`Info: ${label}`} aria-expanded={open} aria-controls={id}
        title={`Info: ${label}`} onClick={() => setOpen(!open)}>
        <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
          <circle cx="12" cy="12" r="9" /><path d="M12 11v6" /><path d="M12 7h.01" strokeWidth="3" />
        </svg>
      </button>
    </div>
    <div id={id} hidden={!open} className={styles.content}>{children}</div>
  </div>
}
