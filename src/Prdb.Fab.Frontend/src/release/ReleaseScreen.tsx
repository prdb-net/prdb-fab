import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'

import {
  listReleases,
  downloadRelease,
  previewResetDownloads,
  previewReleaseDownload,
  readReleaseDiscoveryRoutines,
  resetDownloads,
  runReleaseDiscoveryRoutine,
  type IdentificationState,
  type ReleaseDiscoveryRoutine,
  type ReleasePage,
} from '../api/client.ts'
import styles from './ReleaseScreen.module.css'
import type { ReleaseAddress } from './routes.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'

const states: readonly IdentificationState[] = [
  'Matched',
  'Ambiguous',
  'SiteOnly',
  'Unknown',
]

export function ReleaseScreen() {
  const [parameters, setParameters] = useSearchParams()
  const context = releaseContext(parameters)
  const stateValue = parameters.get('state')
  const state = states.find((value) => value === stateValue)
  const indexer = parameters.get('indexer') ?? undefined
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const releases = useQuery({
    queryKey: ['releases', parameters.toString()],
    queryFn: () => {
      if (!context) throw new Error('A Release context is required.')
      return listReleases({ ...context, state, indexer, page })
    },
    enabled: context !== null,
  })

  if (!context) {
    return (
      <main className={styles.screen}>
        <h1>Releases</h1>
        <p>Open this table from a Video, Site, or Actor.</p>
      </main>
    )
  }

  if (releases.isPending) return <PageLoading label="Loading Releases" />
  if (releases.isError) {
    return <main className={styles.screen}>That Release context could not be read.</main>
  }

  return (
    <ReleaseTable
      page={releases.data}
      returnTo={releaseReturn(parameters, releases.data.context)}
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

function ReleaseTable({
  page,
  returnTo,
  state,
  indexer,
  setFilter,
  goTo,
}: {
  page: ReleasePage
  returnTo: { to: string; label: string }
  state: IdentificationState | undefined
  indexer: string | undefined
  setFilter: (name: 'state' | 'indexer', value: string) => void
  goTo: (page: number) => void
}) {
  const total = Number(page.total)
  const current = Number(page.page)
  const pages = Math.max(1, Math.ceil(total / Number(page.pageSize)))

  return (
    <main className={styles.screen}>
      <Link className={styles.back} to={returnTo.to}>
        &larr; {returnTo.label}
      </Link>
      <div className={styles.heading}>
        <div>
          <h1>Releases</h1>
          <p className={styles.context}>
            For {page.context.kind.toLowerCase()} <strong>{page.context.title}</strong>
          </p>
        </div>
        <span className={styles.count}>{total} releases</span>
      </div>

      <div className={styles.boundary}>
        <strong>Acquisition.</strong> From a Video, choose an identified Release to fetch its
        NZB and submit it to the checked SABnzbd category. prdb-fab follows that job and
        automatically tries the next ranked Release after a release failure, within the
        three-Download budget.
      </div>

      <ReleaseDiscoveryControls />

      {page.acquisition && (
        <AcquisitionSummary videoId={page.context.prdbId} acquisition={page.acquisition} />
      )}

      <div className={styles.filters}>
        <label>
          Identification State
          <select
            id="releases-state"
            name="state"
            value={state ?? ''}
            onChange={(event) => setFilter('state', event.target.value)}
          >
            <option value="">All visible states</option>
            {states.map((value) => (
              <option value={value} key={value}>
                {stateLabel(value)}
              </option>
            ))}
          </select>
        </label>
        <label>
          Indexer
          <select
            id="releases-indexer"
            name="indexer"
            value={indexer ?? ''}
            onChange={(event) => setFilter('indexer', event.target.value)}
          >
            <option value="">All Indexers</option>
            {page.indexers.map((entry) => (
              <option value={entry.id} key={entry.id}>
                {entry.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      {page.releases.length === 0 ? (
        <p className={styles.empty}>No cached Releases match this context and filter.</p>
      ) : (
        <div className={styles.tableFrame}>
          <table>
            <thead>
              <tr>
                <th>Release</th>
                <th>Rank</th>
                <th>Indexer</th>
                <th>Size</th>
                <th>First seen</th>
                <th>Identification State</th>
                <th>Identification</th>
                <th>Confidence</th>
                <th>matchedBy</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {page.releases.map((release) => (
                <tr key={String(release.id)}>
                  <td className={styles.release}>{release.title}</td>
                  <td>
                    {release.rankingPosition ??
                      (release.rankingExclusion ? `Excluded — ${release.rankingExclusion}` : '—')}
                  </td>
                  <td>
                    {release.indexer.name}
                    <span className={styles.secondary}>rank {release.indexer.rank}</span>
                  </td>
                  <td className={styles.nowrap}>{size(release.size)}</td>
                  <td className={styles.nowrap}>{firstSeen(release.firstSeenAt)}</td>
                  <td>
                    <span className={styles.state}>{stateLabel(release.identificationState)}</span>
                  </td>
                  <td>{identification(release)}</td>
                  <td>{release.confidence ?? '—'}</td>
                  <td>{release.matchedBy ?? '—'}</td>
                  <td>
                    {page.context.kind === 'Video' && release.video && release.rankingPosition ? (
                      <DownloadAction
                        releaseId={release.id}
                        releaseTitle={release.title}
                        videoId={page.context.prdbId}
                      />
                    ) : (
                      '—'
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {pages > 1 && (
        <nav className={styles.pager}>
          <button type="button" onClick={() => goTo(current - 1)} disabled={current <= 1}>
            Newer
          </button>
          <span>
            Page {current} of {pages}
          </span>
          <button type="button" onClick={() => goTo(current + 1)} disabled={current >= pages}>
            Older
          </button>
        </nav>
      )}
    </main>
  )
}

function releaseReturn(
  parameters: URLSearchParams,
  context: ReleasePage['context'],
): { to: string; label: string } {
  const supplied = parameters.get('from')
  if (supplied?.startsWith('/') && !supplied.startsWith('//') && !supplied.startsWith('/releases')) {
    return { to: supplied, label: returnLabel(supplied) }
  }

  if (context.kind === 'Site') {
    return { to: `/sites/${context.prdbId}`, label: context.title }
  }
  if (context.kind === 'Actor') {
    return { to: `/actors/${context.prdbId}`, label: context.title }
  }
  return { to: '/', label: 'What’s new' }
}

function returnLabel(path: string): string {
  const pathname = path.split('?')[0]
  if (pathname === '/wanted') return 'Wanted'
  if (pathname.startsWith('/sites')) return 'Sites'
  if (pathname.startsWith('/actors')) return 'Actors'
  if (pathname.startsWith('/library')) return 'Library'
  return 'What’s new'
}

function ReleaseDiscoveryControls() {
  const routines = useQuery({
    queryKey: ['release-discovery-routines'],
    queryFn: readReleaseDiscoveryRoutines,
  })

  return (
    <section className={styles.discovery}>
      <div className={styles.discoveryHeading}>
        <div>
          <h2>Release discovery</h2>
          <p>
            These routines run automatically. Run now makes one due immediately; its lane,
            Governor, and query budget still decide when work can proceed.
          </p>
        </div>
      </div>

      {routines.isPending && <p className={styles.secondary}>Reading the schedule…</p>}
      {routines.isError && (
        <p className={styles.secondary}>The Release discovery schedule could not be read.</p>
      )}
      {routines.data && routines.data.length === 0 && (
        <p className={styles.secondary}>No Release discovery routines are available.</p>
      )}
      {routines.data && routines.data.length > 0 && (
        <div className={styles.routines}>
          {routines.data.map((routine) => (
            <ReleaseDiscoveryRoutineControl
              key={`${routine.kind}:${routine.target ?? 'global'}`}
              routine={routine}
            />
          ))}
        </div>
      )}
    </section>
  )
}

function ReleaseDiscoveryRoutineControl({ routine }: { routine: ReleaseDiscoveryRoutine }) {
  const queryClient = useQueryClient()
  const runNow = useMutation({
    mutationFn: () => runReleaseDiscoveryRoutine(routine),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['release-discovery-routines'] }),
  })

  return (
    <div className={styles.routine}>
      <div>
        <h3>{routine.label}</h3>
        <p>{routine.detail}</p>
        <span className={styles.secondary}>{routineActivity(routine)}</span>
        {runNow.data && <span className={styles.secondary}>{runNow.data.detail}</span>}
        {runNow.isError && (
          <span className={styles.secondary}>The routine could not be made due.</span>
        )}
      </div>
      <button type="button" onClick={() => runNow.mutate()} disabled={runNow.isPending}>
        {runNow.isPending ? 'Scheduling…' : 'Run now'}
      </button>
    </div>
  )
}

function routineActivity(routine: ReleaseDiscoveryRoutine): string {
  if (routine.lastFailureAt && Number(routine.consecutiveFailures) > 0) {
    return `Last failed ${new Date(routine.lastFailureAt).toLocaleString()} (${routine.consecutiveFailures} consecutive).`
  }
  if (routine.lastSuccessAt) {
    return `Last completed ${new Date(routine.lastSuccessAt).toLocaleString()}.`
  }
  return 'No completed run recorded yet.'
}

function AcquisitionSummary({
  videoId,
  acquisition,
}: {
  videoId: string
  acquisition: NonNullable<ReleasePage['acquisition']>
}) {
  const queryClient = useQueryClient()
  const reset = useMutation({
    mutationFn: async () => {
      const preview = await previewResetDownloads(videoId)
      if (preview.outcome !== 'Ready') return preview
      const history = preview.downloads
        .map((download) => `• ${download.submittedName} — ${download.state}${download.cause ? ` / ${download.cause}` : ''}`)
        .join('\n')
      if (!window.confirm(
        `Reset this Video's ${preview.downloads.length} Download attempt(s)?\n\n${history}\n\nThis deletes only local Download history, restores the full retry budget, and allows these Releases again. Any SABnzbd jobs are left untouched and will no longer be followed.`,
      )) return null
      return resetDownloads(videoId, preview.downloads.map((download) => download.id))
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
      void queryClient.invalidateQueries({ queryKey: ['downloads'] })
    },
  })

  const spent = Number(acquisition.downloadsSpent)
  const budget = Number(acquisition.retryBudget)
  return (
    <section className={styles.acquisition}>
      <div className={styles.acquisitionHeading}>
        <div>
          <h2>Download attempts: {spent} of {budget}</h2>
          {spent >= budget ? (
            <p>The Retry Budget is spent.</p>
          ) : acquisition.nextRelease ? (
            <p>Next ranked Release: <strong>{acquisition.nextRelease.title}</strong></p>
          ) : (
            <p>No unconsumed eligible Release is currently available.</p>
          )}
        </div>
        <button type="button" disabled={spent === 0 || reset.isPending} onClick={() => reset.mutate()}>
          {reset.isPending ? 'Checking…' : 'Reset Download history'}
        </button>
      </div>
      {reset.data?.detail && <p className={styles.secondary}>{reset.data.detail}</p>}
      {reset.isError && <p className={styles.secondary}>The Download history could not be checked.</p>}
      {acquisition.downloads.length > 0 && (
        <ul className={styles.attempts}>
          {acquisition.downloads.map((download) => (
            <li key={download.id}>
              <strong>{download.state}</strong>
              {download.cause && ` / ${download.cause}`} — {download.submittedName}
              {download.state === 'Completed' && (
                <span> Waiting for collection.</span>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function DownloadAction({
  releaseId,
  releaseTitle,
  videoId,
}: {
  releaseId: number | string
  releaseTitle: string
  videoId: string
}) {
  const queryClient = useQueryClient()
  const action = useMutation({
    mutationFn: async () => {
      const preview = await previewReleaseDownload(releaseId, videoId)
      if (preview.outcome !== 'Ready' || !preview.downloadId) return preview

      const confirmed = window.confirm(
        `Submit “${releaseTitle}” to SABnzbd?\n\nThis spends bandwidth and consumes one of this Video's ${preview.retryBudget} Download attempts.`,
      )
      if (!confirmed) return null

      return downloadRelease(releaseId, videoId, preview.downloadId)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['releases'] }),
  })

  const answer = action.data
  const outcome = answer && 'outcome' in answer ? answer.outcome : null
  const detail = answer && 'detail' in answer ? answer.detail : null

  return (
    <div className={styles.action}>
      <button type="button" onClick={() => action.mutate()} disabled={action.isPending}>
        {action.isPending ? 'Submitting…' : 'Download'}
      </button>
      {outcome && <span data-outcome={outcome}>{detail}</span>}
      {action.isError && <span>The Download could not be planned.</span>}
    </div>
  )
}

function releaseContext(parameters: URLSearchParams): ReleaseAddress | null {
  const choices = (['video', 'site', 'actor'] as const)
    .map((name) => ({ name, value: parameters.get(name) }))
    .filter((choice): choice is { name: 'video' | 'site' | 'actor'; value: string } =>
      Boolean(choice.value),
    )

  if (choices.length !== 1) return null
  const selected = choices[0]
  if (selected.name === 'video') return { video: selected.value }
  if (selected.name === 'site') return { site: selected.value }
  return { actor: selected.value }
}

function identification(release: ReleasePage['releases'][number]) {
  if (release.video) {
    return (
      <>
        <strong>{release.video.title}</strong>
        {release.video.site && <span className={styles.secondary}>{release.video.site}</span>}
      </>
    )
  }

  if (release.candidates.length > 0) {
    return (
      <>
        <strong>Candidates — no Video selected</strong>
        <span className={styles.secondary}>
          {release.candidates.map((candidate) => candidate.title).join(' · ')}
        </span>
      </>
    )
  }

  if (release.siteOnlyMatch) {
    return (
      <>
        <strong>Site-Only Match — no Video identified</strong>
        <span className={styles.secondary}>{release.siteOnlyMatch.title}</span>
      </>
    )
  }

  return 'No Video identified'
}

function stateLabel(state: IdentificationState): string {
  return state === 'SiteOnly' ? 'Site-Only Match' : state
}

function size(bytes: number | string | null): string {
  if (bytes === null) return 'Unknown'
  const value = Number(bytes)
  return `${(value / 1024 / 1024 / 1024).toFixed(value >= 10 * 1024 ** 3 ? 0 : 1)} GiB`
}

function firstSeen(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(value),
  )
}
