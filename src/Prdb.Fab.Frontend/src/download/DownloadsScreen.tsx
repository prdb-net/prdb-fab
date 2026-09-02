import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'

import {
  listDownloads,
  previewStopFollowing,
  stopFollowing,
  type DownloadPage,
  type DownloadOriginView,
  type DownloadState,
} from '../api/client.ts'
import styles from './DownloadsScreen.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'

const states: readonly DownloadState[] = ['Outstanding', 'Completed', 'Collected', 'Failed', 'Abandoned']

export function DownloadsScreen() {
  const [parameters, setParameters] = useSearchParams()
  const stateValue = parameters.get('state')
  const state = states.find((value) => value === stateValue)
  const indexer = parameters.get('indexer') ?? undefined
  const download = parameters.get('download') ?? undefined
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const downloads = useQuery({
    queryKey: ['downloads', parameters.toString()],
    queryFn: () => listDownloads({ state, indexer, download, page }),
  })

  if (downloads.isPending) return <PageLoading label="Loading Downloads" />
  if (downloads.isError) return <main className={styles.screen}>Downloads could not be read.</main>

  return (
    <DownloadTable
      answer={downloads.data}
      state={state}
      indexer={indexer}
      download={download}
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
  download,
  setFilter,
  goTo,
}: {
  answer: DownloadPage
  state: DownloadState | undefined
  indexer: string | undefined
  download: string | undefined
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
        <span className={styles.total}>{total} Downloads</span>
      </div>

      <div className={styles.boundary}>
        prdb-fab reads SABnzbd but never retries or deletes a SABnzbd job. Stop following changes
        only this local record.
      </div>

      <div className={styles.toolbar}>
        <div className={styles.filters}>
          <label>
            State
            <select
              id="downloads-state"
              name="state"
              value={state ?? ''}
              onChange={(event) => setFilter('state', event.target.value)}
            >
              <option value="">All states</option>
              {states.map((value) => <option value={value} key={value}>{value}</option>)}
            </select>
          </label>
          <label>
            Indexer
            <select
              id="downloads-indexer"
              name="indexer"
              value={indexer ?? ''}
              onChange={(event) => setFilter('indexer', event.target.value)}
            >
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
      </div>

      {answer.downloads.length === 0 ? (
        <div className={styles.empty}>
          <strong>{state || indexer || download ? 'No Downloads match these filters.' : 'No Downloads yet.'}</strong>
          <p>
            {state || indexer || download
              ? 'Clear the filters to see the complete local history.'
              : 'Open a wanted Video, then use the Download button on its Release page.'}
          </p>
          <Link to={state || indexer || download ? '/downloads' : '/wanted'}>
            {state || indexer || download ? 'Clear filters' : 'Go to Wanted'}
          </Link>
        </div>
      ) : (
        <div className={styles.downloads}>
          {answer.downloads.map((download) => (
            <article className={styles.download} key={download.id}>
              <header className={styles.downloadHeading}>
                <div className={styles.release}>
                  <Link to={`/releases?video=${download.videoId}`}>{download.videoTitle}</Link>
                  <span>{download.submittedName}</span>
                </div>
                <span className={`${styles.state} ${stateClass(download.state)}`}>{download.state}</span>
              </header>

              <p className={styles.stateDetail}>{stateDetail(download)}</p>
              {download.failMessage && <p className={styles.failure}>{download.failMessage}</p>}

              <dl className={styles.facts}>
                <div><dt>SABnzbd</dt><dd>{download.lastSabnzbdStatus ?? 'Not seen yet'}</dd></div>
                <div><dt>Indexer</dt><dd>{download.indexer.name}</dd></div>
                <div><dt>Size</dt><dd>{size(download.size)}</dd></div>
                <div><dt>Outstanding since</dt><dd>{date(download.outstandingSince)}</dd></div>
                <div className={styles.origin}>
                  <dt>Origin</dt>
                  <dd><DownloadOrigin origin={download.origin} /></dd>
                </div>
              </dl>

              <footer className={styles.downloadFooter}>
                {download.state === 'Outstanding' ? (
                  <label className={styles.selectDownload}>
                    <input
                      type="checkbox"
                      name="selected-download"
                      value={download.id}
                      checked={selected.has(download.id)}
                      onChange={(event) => setSelected((held) => {
                        const next = new Set(held)
                        if (event.target.checked) next.add(download.id)
                        else next.delete(download.id)
                        return next
                      })}
                    />
                    Select to stop following
                  </label>
                ) : <span />}

                {(download.nzoId || download.stageLog) && (
                  <details className={styles.technicalDetails}>
                    <summary>SABnzbd details</summary>
                    <div>
                      {download.nzoId && (
                        <p><span>Job ID</span><code>{download.nzoId}</code></p>
                      )}
                      {download.stageLog && (
                        <><span>Stage log (JSON)</span><pre>{formatStageLog(download.stageLog)}</pre></>
                      )}
                    </div>
                  </details>
                )}
              </footer>
            </article>
          ))}
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

function stateClass(state: DownloadState): string {
  if (state === 'Collected') return styles.collected
  if (state === 'Failed') return styles.failed
  if (state === 'Outstanding') return styles.outstanding
  if (state === 'Abandoned') return styles.abandoned
  return styles.completed
}

function formatStageLog(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function stateDetail(download: DownloadPage['downloads'][number]): string {
  if (download.state === 'Abandoned') return 'Wanted intent ended; SABnzbd was left untouched.'
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

export function DownloadOrigin({ origin }: { origin: DownloadOriginView }) {
  if (origin.kind === 'Person') return <>Person</>
  if (origin.rules.length === 0) return <>Automation</>
  return (
    <>
      Automation
      {origin.rules.map((rule, index) => rule.ruleId ? (
        <Link key={rule.ruleId} to={`/settings/automation/rules/${rule.ruleId}`}>{rule.name}</Link>
      ) : (
        <span key={`${rule.name}:${index}`}>{rule.name} (deleted)</span>
      ))}
    </>
  )
}

function size(bytes: number | string | null): string {
  if (bytes === null) return 'Unknown'
  const value = Number(bytes)
  return `${(value / 1024 / 1024 / 1024).toFixed(value >= 10 * 1024 ** 3 ? 0 : 1)} GiB`
}

function date(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}
