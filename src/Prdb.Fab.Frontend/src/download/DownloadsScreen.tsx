import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Link, Navigate, useParams, useSearchParams } from 'react-router'

import {
  listDownloads,
  previewStopFollowing,
  stopFollowing,
  type DownloadPage,
  type DownloadOriginView,
  type DownloadSelectionPreview,
  type DownloadState,
} from '../api/client.ts'
import { ConfirmationDialog } from '../ui/ConfirmationDialog.tsx'
import styles from './DownloadsScreen.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { videoReleasePath } from '../release/routes.ts'

const states: readonly DownloadState[] = ['Outstanding', 'Completed', 'Collected', 'Failed', 'Abandoned']

type Download = DownloadPage['downloads'][number]

/**
 * `/downloads` is the list and `/downloads/{id}` is the list with one entry
 * selected — a second column where the window is wide enough for both, and the
 * whole page where it is not. Both are addresses, which is ADR 0036's rule: a
 * selected Download is linkable and the back button does what it looks like it
 * does.
 *
 * The pane reads its own Download rather than looking for it in the loaded
 * page, so a link to one that sits on page seven shows its detail while the
 * list stays on page one under whatever filters are in force.
 */
export function DownloadsScreen() {
  const { downloadId } = useParams()
  const [parameters, setParameters] = useSearchParams()
  const stateValue = parameters.get('state')
  const state = states.find((value) => value === stateValue)
  const indexer = parameters.get('indexer') ?? undefined
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)

  // `?download=` selected a Download before there was an address for one. It
  // still arrives from links sent or bookmarked back then.
  const legacy = parameters.get('download')
  const downloads = useQuery({
    queryKey: ['downloads', state ?? '', indexer ?? '', page],
    queryFn: () => listDownloads({ state, indexer, page }),
    enabled: legacy === null,
  })

  if (legacy !== null) {
    const rest = new URLSearchParams(parameters)
    rest.delete('download')
    const search = rest.toString()
    return <Navigate replace to={`/downloads/${legacy}${search ? `?${search}` : ''}`} />
  }

  if (downloads.isPending) return <PageLoading label="Loading Downloads" />
  if (downloads.isError) return <main className={styles.screen}>Downloads could not be read.</main>

  return (
    <DownloadList
      answer={downloads.data}
      selectedId={downloadId}
      state={state}
      indexer={indexer}
      search={parameters.toString()}
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

function DownloadList({
  answer,
  selectedId,
  state,
  indexer,
  search,
  setFilter,
  goTo,
}: {
  answer: DownloadPage
  selectedId: string | undefined
  state: DownloadState | undefined
  indexer: string | undefined
  search: string
  setFilter: (name: 'state' | 'indexer', value: string) => void
  goTo: (page: number) => void
}) {
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirming, setConfirming] = useState<DownloadSelectionPreview | null>(null)
  const queryClient = useQueryClient()

  // The bar names how many Downloads Stop following covers, so it must not
  // count rows that a changed filter or a turned page has taken off the list.
  const listing = `${state ?? ''}:${indexer ?? ''}:${answer.page}`
  useEffect(() => setSelected(new Set()), [listing])

  // ADR 0058: a destructive act is confirmed in the application. ADR 0040 has
  // the backend compute what the act covers and the act take the identifiers
  // that were shown, so the preview is fetched first and its own list is what
  // the dialog names.
  const ask = useMutation({
    mutationFn: (ids: string[]) => previewStopFollowing(ids),
    onSuccess: (preview) => setConfirming(preview),
  })
  const action = useMutation({
    mutationFn: (preview: DownloadSelectionPreview) =>
      stopFollowing(preview.downloads.map((download) => download.id)),
    onSuccess: () => {
      setConfirming(null)
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['downloads'] })
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
    },
  })

  const total = Number(answer.total)
  const current = Number(answer.page)
  const pages = Math.max(1, Math.ceil(total / Number(answer.pageSize)))
  const counts = answer.counts
  const filtered = state !== undefined || indexer !== undefined
  const busy = ask.isPending || action.isPending

  return (
    <main className={`${styles.screen} ${selectedId ? styles.hasSelection : ''}`}>
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
        <div className={styles.states} role="group" aria-label="Filter by state">
          <StateSegment
            label="All"
            count={Number(counts.outstanding) + Number(counts.completed) + Number(counts.collected)
              + Number(counts.failed) + Number(counts.abandoned)}
            active={state === undefined}
            onSelect={() => setFilter('state', '')}
          />
          {states.map((value) => (
            <StateSegment
              key={value}
              label={value}
              state={value}
              count={Number(counts[countKey(value)])}
              active={state === value}
              onSelect={() => setFilter('state', value)}
            />
          ))}
        </div>

        {/* An Indexer has no fixed set of values and no symbol to carry, so it
            stays what it was. */}
        <label className={styles.indexer}>
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

      {selected.size > 0 && (
        <div className={styles.selectionBar}>
          <span>{selected.size} selected</span>
          <button type="button" disabled={busy} onClick={() => ask.mutate([...selected])}>
            {ask.isPending ? 'Checking…' : 'Stop following'}
          </button>
          <button type="button" className={styles.quietButton} disabled={busy} onClick={() => setSelected(new Set())}>
            Clear
          </button>
          {ask.data && ask.data.outcome !== 'Ready' && <span className={styles.selectionNote}>{ask.data.detail}</span>}
          {(ask.isError || action.isError) && <span className={styles.selectionNote}>The selection could not be checked.</span>}
        </div>
      )}

      {confirming && confirming.outcome === 'Ready' && (
        <ConfirmationDialog
          title={`Stop following ${confirming.downloads.length} Download(s)?`}
          confirmLabel="Stop following"
          danger
          busy={action.isPending}
          onCancel={() => setConfirming(null)}
          onConfirm={() => action.mutate(confirming)}
        >
          <p>
            SABnzbd is left untouched — prdb-fab never retries or deletes a job
            there. Failed Downloads still spend this Video's retry budget, and the
            next ranked Release may be submitted automatically.
          </p>
          <ul className={styles.confirmationList}>
            {confirming.downloads.map((download) => (
              <li key={download.id}>{download.submittedName}</li>
            ))}
          </ul>
        </ConfirmationDialog>
      )}

      <div className={styles.workspace}>
        {answer.downloads.length === 0 ? (
          <div className={styles.empty}>
            <strong>{filtered ? 'No Downloads match these filters.' : 'No Downloads yet.'}</strong>
            <p>
              {filtered
                ? 'Clear the filters to see the complete local history.'
                : 'Open a wanted Video, then use the Download button on its Release page.'}
            </p>
            <Link to={filtered ? '/downloads' : '/wanted'}>{filtered ? 'Clear filters' : 'Go to Wanted'}</Link>
          </div>
        ) : (
          <ul className={styles.list}>
            {answer.downloads.map((download) => (
              <Row
                key={download.id}
                download={download}
                search={search}
                selected={download.id === selectedId}
                checked={selected.has(download.id)}
                onCheck={(checked) => setSelected((held) => {
                  const next = new Set(held)
                  if (checked) next.add(download.id)
                  else next.delete(download.id)
                  return next
                })}
              />
            ))}
          </ul>
        )}

        {selectedId && (
          <DownloadDetail
            key={selectedId}
            downloadId={selectedId}
            search={search}
            busy={busy}
            onStopFollowing={() => ask.mutate([selectedId])}
          />
        )}
      </div>

      {pages > 1 && <nav className={styles.pager}>
        <button type="button" onClick={() => goTo(current - 1)} disabled={current <= 1}>Newer</button>
        <span>Page {current} of {pages}</span>
        <button type="button" onClick={() => goTo(current + 1)} disabled={current >= pages}>Older</button>
      </nav>}
    </main>
  )
}

function Row({
  download,
  search,
  selected,
  checked,
  onCheck,
}: {
  download: Download
  search: string
  selected: boolean
  checked: boolean
  onCheck: (checked: boolean) => void
}) {
  return (
    <li className={`${styles.row} ${selected ? styles.rowSelected : ''}`}>
      {/* Only an Outstanding Download can be stopped, so only those carry one. */}
      {download.state === 'Outstanding' ? (
        <input
          className={styles.rowCheckbox}
          type="checkbox"
          name="selected-download"
          value={download.id}
          checked={checked}
          aria-label={`Select ${download.submittedName}`}
          onChange={(event) => onCheck(event.target.checked)}
        />
      ) : <span className={styles.rowCheckbox} />}

      <Link
        className={styles.rowLink}
        to={{ pathname: `/downloads/${download.id}`, search }}
        aria-current={selected ? 'page' : undefined}
      >
        <StateSymbol state={download.state} />
        <span className={styles.rowName}>
          {download.site && <>
            <span className={styles.rowSite}>{download.site}</span>
            <span className={styles.rowSeparator} aria-hidden="true">&rsaquo;</span>
          </>}
          <span className={styles.rowTitle}>{download.videoTitle}</span>
        </span>
        <span className={styles.rowSize}>{size(download.size)}</span>
        <span className={styles.rowWhen}>{shortDate(download.createdAt)}</span>
      </Link>
    </li>
  )
}

/**
 * Everything the card carried before, for one Download. It reads itself rather
 * than being handed a row out of the list, which is what makes it independent
 * of the page and the filters the list is under.
 */
function DownloadDetail({
  downloadId,
  search,
  busy,
  onStopFollowing,
}: {
  downloadId: string
  search: string
  busy: boolean
  onStopFollowing: () => void
}) {
  const answer = useQuery({
    queryKey: ['downloads', 'one', downloadId],
    queryFn: () => listDownloads({ download: downloadId, page: 1 }),
  })
  const backTo = { pathname: '/downloads', search }

  if (answer.isPending) return <article className={styles.detail}><p>Loading this Download…</p></article>
  if (answer.isError) return <article className={styles.detail}><p>This Download could not be read.</p></article>

  const download = answer.data.downloads[0]
  if (!download) {
    return (
      <article className={styles.detail}>
        <Link className={styles.back} to={backTo}>All Downloads</Link>
        <p>This Download is no longer in the local history.</p>
      </article>
    )
  }

  return (
    <article className={styles.detail}>
      <Link className={styles.back} to={backTo}>All Downloads</Link>

      <header className={styles.detailHeading}>
        <StateSymbol state={download.state} />
        <div className={styles.detailName}>
          {download.site && <span className={styles.detailSite}>{download.site}</span>}
          <h2>{download.videoTitle}</h2>
        </div>
        <span className={`${styles.state} ${stateClass(download.state)}`}>{download.state}</span>
      </header>

      <p className={styles.stateDetail}>{stateDetail(download)}</p>
      {download.failMessage && <p className={styles.failure}>{download.failMessage}</p>}

      <p className={styles.submitted}>{download.submittedName}</p>

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

      <div className={styles.detailActions}>
        <Link to={videoReleasePath(download.videoId, `/downloads/${download.id}${search ? `?${search}` : ''}`)}>
          Releases for this Video
        </Link>
        {download.state === 'Outstanding' && (
          <button type="button" disabled={busy} onClick={onStopFollowing}>Stop following</button>
        )}
      </div>

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
    </article>
  )
}

function StateSegment({
  label,
  state,
  count,
  active,
  onSelect,
}: {
  label: string
  state?: DownloadState
  count: number
  active: boolean
  onSelect: () => void
}) {
  return (
    <button
      type="button"
      className={`${styles.segment} ${active ? styles.segmentActive : ''}`}
      aria-pressed={active}
      onClick={onSelect}
    >
      {state ? <StateSymbol state={state} decorative /> : <AllSymbol />}
      <span>{label}</span>
      <span className={styles.segmentCount}>{count}</span>
    </button>
  )
}

/**
 * The state as a shape as well as a colour, in the inline-SVG idiom the shell's
 * navigation already uses. Colour alone would leave the list unreadable to
 * anybody who does not separate these five hues.
 */
function StateSymbol({ state, decorative = false }: { state: DownloadState; decorative?: boolean }) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth: 1.9,
  }

  return (
    <svg
      aria-hidden={decorative ? 'true' : undefined}
      aria-label={decorative ? undefined : state}
      className={`${styles.symbol} ${stateClass(state)}`}
      role={decorative ? undefined : 'img'}
      viewBox="0 0 24 24"
    >
      {/* Outstanding: a clock — it is still running. */}
      {state === 'Outstanding' && <path d="M12 4a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm0 3.6V12l3 2" {...common} />}
      {/* Completed: arrived, and waiting to be collected. */}
      {state === 'Completed' && <path d="M12 4v9m-3.5-3.5L12 13l3.5-3.5M5 16v3h14v-3" {...common} />}
      {/* Collected: handed to Filing, and done with. */}
      {state === 'Collected' && <path d="m4.5 12.5 5 5 10-11" {...common} />}
      {/* Failed: the one shape that should stop a reader. */}
      {state === 'Failed' && <path d="M12 4 2.8 20h18.4L12 4Zm0 6v4.5m0 2.6v.2" {...common} />}
      {/* Abandoned: nothing is following it any more. */}
      {state === 'Abandoned' && <path d="M12 4a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm-5.7 2.3 11.4 11.4" {...common} />}
    </svg>
  )
}

function AllSymbol() {
  return (
    <svg aria-hidden="true" className={styles.symbol} viewBox="0 0 24 24">
      <path
        d="M4 6h16M4 12h16M4 18h16"
        fill="none"
        stroke="currentColor"
        strokeLinecap="round"
        strokeWidth={1.9}
      />
    </svg>
  )
}

function countKey(state: DownloadState): 'outstanding' | 'completed' | 'collected' | 'failed' | 'abandoned' {
  if (state === 'Outstanding') return 'outstanding'
  if (state === 'Completed') return 'completed'
  if (state === 'Collected') return 'collected'
  if (state === 'Failed') return 'failed'
  return 'abandoned'
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

function stateDetail(download: Download): string {
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

function shortDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}
