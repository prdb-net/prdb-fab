import { useMutation, useQueryClient } from '@tanstack/react-query'

import type { AccountPreferenceVerdict } from '../api/client.ts'
import styles from './PreferenceButton.module.css'

export function PreferenceButton({
  active,
  activeLabel,
  inactiveLabel,
  iconOnly = false,
  write,
}: {
  active: boolean
  activeLabel: string
  inactiveLabel: string
  iconOnly?: boolean
  write: (desired: boolean) => Promise<AccountPreferenceVerdict>
}) {
  const cache = useQueryClient()
  const mutation = useMutation({
    mutationFn: () => write(!active),
    onSuccess: async (verdict) => {
      if (verdict.outcome === 'Updated') {
        await cache.invalidateQueries({ queryKey: ['catalogue'] })
      }
    },
  })
  const verdict = mutation.data
  const problem = mutation.error?.message
    ?? (verdict && verdict.outcome !== 'Updated' ? verdict.detail : null)

  return (
    <span className={styles.control}>
      <button
        aria-label={active ? activeLabel : inactiveLabel}
        aria-busy={mutation.isPending}
        aria-pressed={active}
        className={iconOnly ? styles.iconButton : undefined}
        disabled={mutation.isPending}
        onClick={() => mutation.mutate()}
        title={active ? activeLabel : inactiveLabel}
        type="button"
      >
        {iconOnly ? <FavouriteIcon active={active} /> : mutation.isPending ? 'Saving…' : active ? activeLabel : inactiveLabel}
      </button>
      {problem && (
        <span className={styles.error} role="alert">
          {problem}{' '}
          <button type="button" disabled={mutation.isPending} onClick={() => mutation.mutate()}>
            Retry
          </button>
        </span>
      )}
    </span>
  )
}

function FavouriteIcon({ active }: { active: boolean }) {
  return (
    <svg aria-hidden="true" className={styles.icon} viewBox="0 0 24 24">
      <path
        d="M12 20S4 15.6 4 9.5A4.5 4.5 0 0 1 12 6.7a4.5 4.5 0 0 1 8 2.8C20 15.6 12 20 12 20Z"
        fill={active ? 'currentColor' : 'none'}
        stroke="currentColor"
        strokeLinejoin="round"
        strokeWidth="1.8"
      />
    </svg>
  )
}
