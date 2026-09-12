import type { ReactNode } from 'react'

import styles from './Form.module.css'

/**
 * The four shapes a verdict comes in. ADR 0040 makes a refusal a success with a
 * name in it, so all four are sentences the tool decided to say rather than
 * failures to report.
 */
export type VerdictTone = 'done' | 'warning' | 'confirmed' | 'refusal'

/**
 * One place a verdict is rendered.
 *
 * Before ADR 0058 there were eleven: every screen reached into
 * `Onboarding.module.css` for `done` or `refusal` and wrote its own paragraph,
 * and several of them put `String(error)` in one — so what a person read when a
 * read failed was `Error: Failed to fetch`.
 *
 * A refusal is announced as an alert because it is the answer to something the
 * person just did and there may be nothing else on screen that changed; the
 * other three are polite, since they follow an act whose result is visible.
 */
export function Verdict({
  tone,
  className,
  children,
}: {
  tone: VerdictTone
  className?: string
  children: ReactNode
}) {
  return (
    <p
      className={className ? `${styles[tone]} ${className}` : styles[tone]}
      role={tone === 'refusal' ? 'alert' : 'status'}
    >
      {children}
    </p>
  )
}
