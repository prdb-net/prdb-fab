import { useId, type ReactNode } from 'react'

import { InfoHelp } from './InfoHelp.tsx'
import styles from './Form.module.css'

export { styles as formStyles }

/**
 * One question: its label, its control, and the hint under it.
 *
 * The control is a render prop taking the id rather than a child that has to
 * carry a hand-written one. Every `htmlFor`/`id` pair on this surface was two
 * string literals somebody kept in step by reading them, and `useId` is what
 * removes the chance of a label pointing at nothing — which is a label that
 * does not focus its field and is not announced with it.
 */
export function Field({
  label,
  hint,
  help,
  children,
}: {
  label: ReactNode
  hint?: ReactNode
  help?: ReactNode
  children: (id: string) => ReactNode
}) {
  const id = useId()

  const caption = <label className={styles.label} htmlFor={id}>{label}</label>
  return (
    <div className={styles.fieldGroup}>
      {help ? <InfoHelp label={typeof label === 'string' ? label : 'This field'} heading={caption}>{help}</InfoHelp> : caption}
      {children(id)}
      {hint && <p className={styles.hint}>{hint}</p>}
    </div>
  )
}

/**
 * A real group, which is what every radio set and every switch group on this
 * surface has to be.
 *
 * Before ADR 0058 a group heading was a `<label className={label}>` bound to no
 * control at all, so *Allow an automatic Download after* was announced as a
 * stray sentence and its three radios as unrelated to each other and to it.
 */
export function Fieldset({
  legend,
  hint,
  help,
  children,
}: {
  legend: ReactNode
  hint?: ReactNode
  help?: ReactNode
  children: ReactNode
}) {
  return (
    <fieldset className={styles.fieldset}>
      <legend className={styles.legend}>{legend}</legend>
      {help ? (
        <InfoHelp label={typeof legend === 'string' ? legend : 'This group'} heading={<span className={styles.hint}>{hint ?? 'More information'}</span>}>{help}</InfoHelp>
      ) : hint && <p className={styles.hint}>{hint}</p>}
      {children}
    </fieldset>
  )
}

/**
 * One radio inside a {@link Fieldset}, with its own hint.
 *
 * The hint belongs to the member rather than to the group because on this
 * surface every named answer has a consequence of its own — ADR 0006's gates
 * are set membership, and what each set admits is the thing a person is
 * choosing between.
 */
export function Choice<T extends string>({
  name,
  value,
  selected,
  onSelect,
  label,
  hint,
  disabled = false,
}: {
  name: string
  value: T
  selected: T
  onSelect: (value: T) => void
  label: ReactNode
  hint?: ReactNode
  disabled?: boolean
}) {
  const id = useId()

  return (
    <div className={styles.member}>
      <input
        id={id}
        type="radio"
        name={name}
        value={value}
        checked={selected === value}
        disabled={disabled}
        onChange={() => onSelect(value)}
      />
      <label htmlFor={id}>{label}</label>
      {hint && <p className={styles.memberHint}>{hint}</p>}
    </div>
  )
}

/**
 * What a channel sends: available, and out of the way.
 *
 * It was a wall of prose at exactly the moment somebody is deciding, which is
 * the moment they will not read it. Folded, the sentence that matters is the
 * one at the switch. Part of ADR 0058's kit rather than of one screen, because
 * the third Reporting channel is explained in onboarding as well as in the
 * settings and both say the same thing.
 */
export function Sends({ summary, children }: { summary: string; children: ReactNode }) {
  return (
    <details className={styles.sends}>
      <summary>{summary}</summary>
      <ul>{children}</ul>
    </details>
  )
}

/** One checkbox with its label and its hint. */
export function Switch({
  checked,
  onChange,
  label,
  hint,
  help,
  disabled = false,
}: {
  checked: boolean
  onChange: (checked: boolean) => void
  label: ReactNode
  hint?: ReactNode
  help?: ReactNode
  disabled?: boolean
}) {
  const id = useId()

  return (
    <div className={styles.member}>
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
      />
      {help ? <InfoHelp label={typeof label === 'string' ? label : 'This option'} heading={<label htmlFor={id}>{label}</label>}>{help}</InfoHelp> : <label htmlFor={id}>{label}</label>}
      {hint && <p className={styles.memberHint}>{hint}</p>}
    </div>
  )
}
