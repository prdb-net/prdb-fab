import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'

import {
  listReleases,
  listIndexers,
  downloadRelease,
  previewResetDownloads,
  previewReleaseDownload,
  readLatestManualSearch,
  resetDownloads,
  retryManualSearchIndexer,
  startManualSearch,
  type IdentificationState,
  type ManualSearchView,
  type ReleasePage,
} from '../api/client.ts'
import styles from './ReleaseScreen.module.css'
import type { ReleaseAddress } from './routes.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { DownloadOrigin } from '../download/DownloadsScreen.tsx'

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
    refetchInterval: context && 'video' in context ? 3000 : false,
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
      clearFilters={() => {
        const next = new URLSearchParams(parameters)
        next.delete('state')
        next.delete('indexer')
        next.delete('page')
        setParameters(next)
      }}
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
  clearFilters,
  setFilter,
  goTo,
}: {
  page: ReleasePage
  returnTo: { to: string; label: string }
  state: IdentificationState | undefined
  indexer: string | undefined
  clearFilters: () => void
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
        <strong>{page.recentWindow.complete ? 'Recent Window ready.' : 'Recent Window still filling.'}</strong>{' '}
        Reading or refreshing this table never queries an Indexer. Background Sync keeps the newest{' '}
        {page.recentWindow.days} days of Catalogue, Indexer and Identification data prepared.
        {!page.recentWindow.complete && ' An empty table is not authoritative until every configured source is complete.'}
      </div>

      {page.context.kind === 'Video' && <ManualSearchPanel videoId={page.context.prdbId} />}

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
        <ReleaseEmpty
          filtered={Boolean(state || indexer)}
          returnTo={returnTo}
          clearFilters={clearFilters}
        />
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
                <th>Automation</th>
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
                  <td>{automationExplanation(release)}</td>
                  <td>
                    <ReleaseActionCell page={page} release={release} />
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

function ReleaseActionCell({
  page,
  release,
}: {
  page: ReleasePage
  release: ReleasePage['releases'][number]
}) {
  if (page.context.kind !== 'Video') {
    return release.video ? (
      <Link to={videoReleasePathFor(release.video.prdbId)}>Open identified Video</Link>
    ) : actionReason(release)
  }
  if (release.video && release.rankingPosition) {
    return (
      <DownloadAction
        releaseId={release.id}
        videoId={page.context.prdbId}
      />
    )
  }
  return release.rankingExclusion
    ? `Cannot download — ${release.rankingExclusion}`
    : actionReason(release)
}

function videoReleasePathFor(videoId: string): string {
  const parameters = new URLSearchParams({ video: videoId })
  return `/releases?${parameters}`
}

function actionReason(release: ReleasePage['releases'][number]): string {
  const reasons: Record<IdentificationState, string> = {
    Unexamined: 'Cannot download — not screened yet',
    Unremarkable: 'Cannot download — not a relevant Release',
    Awaiting: 'Cannot download — awaiting Identification',
    Matched: 'Not eligible for this Video',
    Ambiguous: 'Cannot download — ambiguous Video',
    SiteOnly: 'Cannot download — Site-Only Match',
    Unknown: 'Cannot download — no Video identified',
  }
  return reasons[release.identificationState]
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
  if (pathname === '/search') return 'Search'
  if (pathname.startsWith('/sites')) return 'Sites'
  if (pathname.startsWith('/actors')) return 'Actors'
  if (pathname.startsWith('/library')) return 'Library'
  return 'What’s new'
}

function ManualSearchPanel({ videoId }: { videoId: string }) {
  const queryClient = useQueryClient()
  const latest = useQuery({
    queryKey: ['manual-search', videoId],
    queryFn: () => readLatestManualSearch(videoId),
    refetchInterval: (query) => query.state.data?.active ? 1500 : false,
  })
  const indexers = useQuery({ queryKey: ['indexers'], queryFn: listIndexers })
  const start = useMutation({
    mutationFn: (indexerId: string | null) => startManualSearch(videoId, indexerId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['manual-search', videoId] })
      await queryClient.invalidateQueries({ queryKey: ['releases'] })
    },
  })
  const retry = useMutation({
    mutationFn: ({ searchId, indexerId }: { searchId: string; indexerId: string }) =>
      retryManualSearchIndexer(searchId, indexerId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['manual-search', videoId] }),
  })

  return (
    <section className={styles.manualSearch}>
      <div className={styles.manualSearchHeading}>
        <div>
          <h2>Search Indexers</h2>
          <p>Search explicitly for older material or retry now. Recent Releases arrive and flow through Identification automatically.</p>
        </div>
        <form onSubmit={(event) => {
          event.preventDefault()
          const selected = String(new FormData(event.currentTarget).get('indexer') ?? '')
          start.mutate(selected || null)
        }}>
          <select name="indexer" aria-label="Indexer">
            <option value="">All enabled Indexers</option>
            {indexers.data?.filter((indexer) => indexer.enabled).map((indexer) => (
              <option key={indexer.id} value={indexer.id}>{indexer.name}</option>
            ))}
          </select>
          <button type="submit" disabled={start.isPending || latest.data?.active === true}>
            {start.isPending ? 'Queueing…' : latest.data?.active ? 'Search in progress' : 'Search now'}
          </button>
        </form>
      </div>
      {start.data && <p className={styles.searchVerdict}>{start.data.detail}</p>}
      {start.isError && <p className={styles.searchVerdict}>The Manual Search could not be queued.</p>}
      {latest.data && <ManualSearchProgress search={latest.data} retry={(indexerId) => retry.mutate({ searchId: latest.data!.id, indexerId })} />}
    </section>
  )
}

function ManualSearchProgress({ search, retry }: { search: ManualSearchView; retry: (indexerId: string) => void }) {
  const results = search.results
  return (
    <div className={styles.searchProgress}>
      <p><strong>{phaseLabel(search.phase)}</strong> · query “{search.query}” · {Number(results.seen)} results seen, {Number(results.added)} new</p>
      <p className={styles.secondary}>
        {Number(results.pending)} awaiting screening or Identification · {Number(results.matchedVideo)} matched this Video · {Number(results.ambiguous)} ambiguous · {Number(results.siteOnly)} Site-Only Match · {Number(results.unknown)} unknown
      </p>
      <ul>
        {search.indexers.map((part) => (
          <li key={part.indexerId}>
            <strong>{part.indexer}</strong> — {part.state}
            {part.detail && `: ${part.detail}`}
            {part.deferredUntil && ` until ${new Date(part.deferredUntil).toLocaleString()}`}
            {part.canRetry && <button type="button" onClick={() => retry(part.indexerId)}>Retry</button>}
          </li>
        ))}
      </ul>
    </div>
  )
}

function phaseLabel(phase: ManualSearchView['phase']): string {
  const labels: Record<ManualSearchView['phase'], string> = {
    Queued: 'Queued',
    Searching: 'Searching Indexers',
    Deferred: 'Waiting for query budget',
    Identifying: 'Identifying Releases',
    Complete: 'Search complete',
    Failed: 'Search failed',
  }
  return labels[phase]
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
  const ready = spent < budget ? acquisition.nextRelease : null
  return (
    <section className={`${styles.acquisition} ${ready ? styles.ready : ''}`}>
      <div className={styles.acquisitionHeading}>
        <div>
          <h2>{ready ? 'Ready to download' : 'No download-ready Release'}</h2>
          {spent >= budget ? (
            <p>The retry budget is spent ({spent} of {budget} attempts).</p>
          ) : ready ? (
            <p>
              Best available Release: <strong>{ready.title}</strong>
              <span className={styles.secondary}>Attempt {spent + 1} of {budget}</span>
            </p>
          ) : (
            <p>No unconsumed eligible Release is currently available.</p>
          )}
        </div>
        <div className={styles.acquisitionActions}>
          {ready && (
            <DownloadAction
              releaseId={ready.id}
              videoId={videoId}
              label="Download best Release"
            />
          )}
          <button type="button" disabled={spent === 0 || reset.isPending} onClick={() => reset.mutate()}>
            {reset.isPending ? 'Checking…' : 'Reset Download history'}
          </button>
        </div>
      </div>
      {reset.data?.detail && <p className={styles.secondary}>{reset.data.detail}</p>}
      {reset.isError && <p className={styles.secondary}>The Download history could not be checked.</p>}
      {acquisition.downloads.length > 0 && (
        <ul className={styles.attempts}>
          {acquisition.downloads.map((download) => (
            <li key={download.id}>
              <strong>{download.state}</strong>
              {download.cause && ` / ${download.cause}`} — {download.submittedName}
              {' · '}<DownloadOrigin origin={download.origin} />
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
  videoId,
  label = 'Download',
}: {
  releaseId: number | string
  videoId: string
  label?: string
}) {
  const queryClient = useQueryClient()
  const action = useMutation({
    mutationFn: async () => {
      const preview = await previewReleaseDownload(releaseId, videoId)
      if (preview.outcome !== 'Ready' || !preview.downloadId) return preview
      return downloadRelease(releaseId, videoId, preview.downloadId)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
      void queryClient.invalidateQueries({ queryKey: ['downloads'] })
    },
  })

  const answer = action.data
  const outcome = answer && 'outcome' in answer ? answer.outcome : null
  const detail = answer && 'detail' in answer ? answer.detail : null

  return (
    <div className={styles.action}>
      <button type="button" onClick={() => action.mutate()} disabled={action.isPending}>
        {action.isPending ? 'Submitting…' : label}
      </button>
      {outcome && <span data-outcome={outcome}>{detail}</span>}
      {action.isError && <span>The Download could not be planned.</span>}
    </div>
  )
}

function ReleaseEmpty({
  filtered,
  returnTo,
  clearFilters,
}: {
  filtered: boolean
  returnTo: { to: string; label: string }
  clearFilters: () => void
}) {
  return (
    <div className={styles.empty}>
      <strong>{filtered ? 'No Releases match these filters.' : 'No cached Releases yet.'}</strong>
      <p>
        {filtered
          ? 'Clear the filters to see every cached result for this context.'
          : 'Discovery runs in the background. You can return later or choose another Video; opening this page does not start an Indexer search.'}
      </p>
      {filtered ? (
        <button type="button" onClick={clearFilters}>Clear filters</button>
      ) : (
        <Link to={returnTo.to}>Back to {returnTo.label}</Link>
      )}
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

function automationExplanation(release: ReleasePage['releases'][number]) {
  if (release.applicableRules.length > 0) {
    return (
      <>
        <strong>Matching rules</strong>
        {release.applicableRules.map((rule) => (
          <Link className={styles.secondary} key={rule.id} to={`/settings/automation/rules/${rule.id}`}>
            {rule.name}
          </Link>
        ))}
      </>
    )
  }
  if (!release.automaticDecisionReason) return 'Not evaluated'
  const reasons: Record<string, string> = {
    NotWanted: 'Video is not Wanted',
    ConfidenceGate: 'Held by the before-download gate',
    Size: 'Outside every allowed size range',
    IndexerNotAllowed: 'No enabled rule allows this Indexer',
    HeldVideo: 'Video is already held in the Library',
    OpenReviewQueue: 'Waiting for this Video’s Review Queue entry',
    AutomaticDownloadCap: 'Waiting for the automatic Download cap',
    RetryBudgetSpent: 'Retry Budget is spent',
    NoReleasesLeft: 'No unconsumed Release remains',
    DownloadInFlight: 'Waiting for this Video’s current Download',
  }
  return reasons[release.automaticDecisionReason] ?? release.automaticDecisionReason
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
