import { useMutation, useQueryClient } from '@tanstack/react-query'

import type { AccountPreferenceVerdict } from '../api/client.ts'
import styles from './PreferenceButton.module.css'

export function PreferenceButton({
  active,
  activeLabel,
  inactiveLabel,
  write,
}: {
  active: boolean
  activeLabel: string
  inactiveLabel: string
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
      <button type="button" disabled={mutation.isPending} onClick={() => mutation.mutate()}>
        {mutation.isPending ? 'Saving…' : active ? activeLabel : inactiveLabel}
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
