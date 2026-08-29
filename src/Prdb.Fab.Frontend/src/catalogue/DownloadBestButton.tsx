import { useMutation, useQueryClient } from '@tanstack/react-query'

import { downloadBest } from '../api/client.ts'
import styles from './PreferenceButton.module.css'

export function DownloadBestButton({ prdbId }: { prdbId: string }) {
  const cache = useQueryClient()
  const mutation = useMutation({
    mutationFn: () => downloadBest(prdbId),
    onSuccess: async () => {
      await Promise.all([
        cache.invalidateQueries({ queryKey: ['catalogue'] }),
        cache.invalidateQueries({ queryKey: ['downloads'] }),
        cache.invalidateQueries({ queryKey: ['releases'] }),
      ])
    },
  })
  const verdict = mutation.data
  const failed = verdict && !['Submitted', 'Pending', 'SubmissionUnknown'].includes(verdict.outcome)
  const problem = mutation.error?.message ?? (failed ? verdict.detail : null)

  return (
    <span className={styles.control}>
      <button type="button" disabled={mutation.isPending} onClick={() => mutation.mutate()}>
        {mutation.isPending ? 'Starting…' : 'Download best Release'}
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
