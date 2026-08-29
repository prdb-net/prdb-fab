import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'

import {
  listDownloads,
  previewStopFollowing,
  stopFollowing,
  type DownloadPage,
  type DownloadState,
} from '../api/client.ts'
import styles from './DownloadsScreen.module.css'

const states: readonly DownloadState[] = ['Outstanding', 'Completed', 'Collected', 'Failed']

export function DownloadsScreen() {
  const [parameters, setParameters] = useSearchParams()
  const stateValue = parameters.get('state')
  const state = states.find((value) => value === stateValue)
  const indexer = parameters.get('indexer') ?? undefined
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const downloads = useQuery({
    queryKey: ['downloads', parameters.toString()],
    queryFn: () => listDownloads({ state, indexer, page }),
  })

  if (downloads.isPending) return null
  if (downloads.isError) return <main className={styles.screen}>Downloads could not be read.</main>

  return (
    <DownloadTable
      answer={downloads.data}
      state={state}
      indexer={indexer}
      setFilter={(name, value) => {
        const next = new URLSearchParams(parameters)
        if (value) next.set(name, value)
        else next.delete(name)
        next.delete('page')
        setParameters(next)
      }}
      goTo={(wanted) => {
        const next = new URLSearchParams(parameters)
        if (wanted === 1) next.delete('page')
        else next.set('page', String(wanted))
        setParameters(next)
        window.scrollTo({ top: 0 })
      }}
    />
  )
}

function DownloadTable({
  answer,
  state,
  indexer,
  setFilter,
  goTo,
}: {
  answer: DownloadPage
  state: DownloadState | undefined
  indexer: string | undefined
  setFilter: (name: 'state' | 'indexer', value: string) => void
  goTo: (page: number) => void
}) {
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const queryClient = useQueryClient()
  const action = useMutation({
    mutationFn: async () => {
      const ids = [...selected]
      const preview = await previewStopFollowing(ids)
      if (preview.outcome !== 'Ready') return preview
      const names = preview.downloads.map((download) => `• ${download.submittedName}`).join('\n')
      if (!window.confirm(
        `Stop following ${preview.downloads.length} Download(s)?\n\n${names}\n\nSABnzbd will be left untouched. Failed Downloads still spend this Video's retry budget, and the next ranked Release may be submitted automatically.`,
      )) return null
      return stopFollowing(preview.downloads.map((download) => download.id))
    },
    onSuccess: () => {
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['downloads'] })
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
    },
  })

  const total = Number(answer.total)
  const current = Number(answer.page)
  const pages = Math.max(1, Math.ceil(total / Number(answer.pageSize)))

  return (
    <main className={styles.screen}>
      <div className={styles.heading}>
        <div>
          <h1>Downloads</h1>
          <p>Local history of what prdb-fab submitted and still follows in SABnzbd.</p>
        </div>
        <span>{total} Downloads</span>
      </div>

      <div className={styles.boundary}>
        prdb-fab reads SABnzbd but never retries or deletes a SABnzbd job. Stop following changes
        only this local record.
      </div>

      <div className={styles.filters}>
        <label>
          State
          <select value={state ?? ''} onChange={(event) => setFilter('state', event.target.value)}>
            <option value="">All states</option>
            {states.map((value) => <option value={value} key={value}>{value}</option>)}
          </select>
        </label>
        <label>
          Indexer
          <select value={indexer ?? ''} onChange={(event) => setFilter('indexer', event.target.value)}>
            <option value="">All Indexers</option>
            {answer.indexers.map((entry) => <option value={entry.id} key={entry.id}>{entry.name}</option>)}
          </select>
        </label>
      </div>

      <div className={styles.selection}>
        <button type="button" disabled={selected.size === 0 || action.isPending} onClick={() => action.mutate()}>
          {action.isPending ? 'Checking…' : `Stop following${selected.size ? ` (${selected.size})` : ''}`}
        </button>
        {action.data?.detail && <span>{action.data.detail}</span>}
        {action.isError && <span>The selection could not be checked.</span>}
      </div>

      {answer.downloads.length === 0 ? (
        <p className={styles.empty}>No local Downloads match this filter.</p>
      ) : (
        <div className={styles.tableFrame}>
          <table>
            <thead><tr>
              <th>Select</th><th>Video / Release</th><th>State</th><th>SABnzbd</th>
              <th>Indexer</th><th>Size</th><th>Origin</th><th>Outstanding since</th>
            </tr></thead>
            <tbody>{answer.downloads.map((download) => (
              <tr key={download.id}>
                <td>
                  <input
                    type="checkbox"
                    aria-label={`Select ${download.submittedName}`}
                    disabled={download.state !== 'Outstanding'}
                    checked={selected.has(download.id)}
                    onChange={(event) => setSelected((held) => {
                      const next = new Set(held)
                      if (event.target.checked) next.add(download.id)
                      else next.delete(download.id)
                      return next
                    })}
                  />
                </td>
                <td className={styles.release}>
                  <Link to={`/releases?video=${download.videoId}`}><strong>{download.videoTitle}</strong></Link>
                  <span>{download.submittedName}</span>
                </td>
                <td><strong>{download.state}</strong><span>{stateDetail(download)}</span></td>
                <td>
                  <span>{download.lastSabnzbdStatus ?? 'Not seen yet'}</span>
                  {download.nzoId && <code>{download.nzoId}</code>}
                  {download.failMessage && <span>{download.failMessage}</span>}
                  {download.stageLog && <pre>{download.stageLog}</pre>}
                </td>
                <td>{download.indexer.name}</td>
                <td>{size(download.size)}</td>
                <td>{download.origin}</td>
                <td>{date(download.outstandingSince)}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}

      {pages > 1 && <nav className={styles.pager}>
        <button type="button" onClick={() => goTo(current - 1)} disabled={current <= 1}>Newer</button>
        <span>Page {current} of {pages}</span>
        <button type="button" onClick={() => goTo(current + 1)} disabled={current >= pages}>Older</button>
      </nav>}
    </main>
  )
}

function stateDetail(download: DownloadPage['downloads'][number]): string {
  if (download.state === 'Completed') return 'Waiting for collection.'
  if (download.state === 'Outstanding') return download.lastSabnzbdStatus ?? 'Waiting for SABnzbd.'
  if (download.state === 'Collected') return 'Files were handed to Filing.'
  const causes: Record<string, string> = {
    Rejected: 'SABnzbd did not accept this submission.',
    Failed: 'SABnzbd reported that the job failed.',
    Unusable: 'The job is paused as encrypted or unwanted.',
    Vanished: 'Not found in SABnzbd after three successful polls; likely deleted.',
    Abandoned: 'A person stopped following this job.',
    Empty: 'The completed Download contained no video file.',
  }
  return download.cause ? causes[download.cause] : 'The Download failed.'
}

function size(bytes: number | string | null): string {
  if (bytes === null) return 'Unknown'
  const value = Number(bytes)
  return `${(value / 1024 / 1024 / 1024).toFixed(value >= 10 * 1024 ** 3 ? 0 : 1)} GiB`
}

function date(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}
