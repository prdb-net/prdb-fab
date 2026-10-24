import { useEffect, useRef } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readCatalogueSync, syncCatalogue } from '../api/client.ts'
import styles from './SyncControl.module.css'

type Surface = 'wanted' | 'actors' | 'sites'

/** A local status read; only the button asks the ordinary schedule for work. */
export function SyncControl({ surface }: { surface: Surface }) {
  const cache = useQueryClient()
  const status = useQuery({
    queryKey: ['catalogue-sync'],
    queryFn: readCatalogueSync,
    refetchInterval: 3000,
  })
  const sync = status.data?.find((item) => item.surface === surface)
  const previous = useRef<string | null | undefined>(undefined)
  useEffect(() => {
    if (!sync) return
    if (previous.current !== undefined && previous.current !== sync.lastSyncedAt) {
      void cache.invalidateQueries({ queryKey: ['catalogue', surface] })
    }
    previous.current = sync.lastSyncedAt
  }, [sync, cache, surface])

  const request = useMutation({
    mutationFn: () => syncCatalogue(surface),
    onSuccess: () => cache.invalidateQueries({ queryKey: ['catalogue-sync'] }),
  })
  const refused = request.data?.find((verdict) => verdict.outcome === 'Refused')
  const busy = request.isPending || sync?.outcome === 'Scheduled'
  const current = !status.isError && !request.isError && !refused && !busy && sync?.outcome === 'Current'
  const problem = request.error?.message ?? refused?.detail
  const last = sync?.lastSyncedAt
  const age = last ? `Synced ${relativeTime(last)}` : 'Not synced yet'
  const label = problem ? 'Sync unavailable'
    : status.isError ? 'Status unavailable'
    : busy ? 'Sync queued…'
    : sync?.outcome === 'Deferred' ? 'Waiting for prdb'
    : sync?.outcome === 'Failed' ? 'Sync failed'
    : sync?.outcome === 'Pending' && last ? 'Sync pending'
    : status.isPending ? 'Reading status…' : age
  const detail = [
    problem,
    sync?.detail,
    sync?.retryAt ? `Next attempt: ${new Date(sync.retryAt).toLocaleString()}` : null,
    last ? `Last fully synced: ${new Date(last).toLocaleString()}` : null,
    'Syncs through the background schedule and respects prdb limits.',
  ].filter(Boolean).join('\n')

  return (
    <div className={styles.control}>
      <span className={`${styles.status} ${current ? styles.current : ''}`} role="status" title={detail}>
        {current && <svg aria-hidden="true" viewBox="0 0 16 16"><path d="m3.5 8 3 3 6-6" /></svg>}
        <span className={styles.statusText}>
          {problem || status.isError || sync?.outcome === 'Failed' || sync?.outcome === 'Deferred'
            ? <Link to="/status" title={detail}>{label}</Link>
            : <span>{label}</span>}
          {last && label !== age && <span className={styles.last}>{age}</span>}
        </span>
      </span>
      <button
        type="button"
        className={styles.button}
        disabled={busy || status.isPending}
        aria-label={`Sync ${surface === 'wanted' ? 'Wanted' : surface === 'actors' ? 'Actors' : 'Sites'} with prdb`}
        title="Sync now with prdb"
        onClick={() => request.mutate()}
      >
        <svg aria-hidden="true" className={busy ? styles.spinning : ''} viewBox="0 0 16 16">
          <path d="M13.5 6A5.5 5.5 0 0 0 4 3.8L2 6m0-3v3h3M2.5 10a5.5 5.5 0 0 0 9.5 2.2l2-2.2m0 3v-3h-3" />
        </svg>
        Sync
      </button>
    </div>
  )
}

function relativeTime(at: string) {
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(at).getTime()) / 60_000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}
