import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'

import {
  listReleases,
  downloadRelease,
  previewReleaseDownload,
  type IdentificationState,
  type ReleasePage,
} from '../api/client.ts'
import styles from './ReleaseScreen.module.css'
import type { ReleaseAddress } from './routes.ts'

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

  if (releases.isPending) return null
  if (releases.isError) {
    return <main className={styles.screen}>That Release context could not be read.</main>
  }

  return (
    <ReleaseTable
      page={releases.data}
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
  state,
  indexer,
  setFilter,
  goTo,
}: {
  page: ReleasePage
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
        <strong>Manual acquisition.</strong> From a Video, choose an identified Release to
        fetch its NZB and submit it to the checked SABnzbd category. The choice is
        recorded before SABnzbd is written.
      </div>

      <div className={styles.filters}>
        <label>
          Identification State
          <select value={state ?? ''} onChange={(event) => setFilter('state', event.target.value)}>
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
