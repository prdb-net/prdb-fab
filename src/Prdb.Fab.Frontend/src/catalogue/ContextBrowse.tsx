import { SyncControl } from './SyncControl.tsx'
import { PageNotice } from '../ui/PageNotice.tsx'
import type { FormEvent, ReactNode } from 'react'
import { Link, useLocation } from 'react-router'

import type { VideoCard } from '../api/client.ts'
import { CachedArtwork, Grid } from './Grid.tsx'
import { PreviewOverlay } from './Preview.tsx'
import { usePreview } from './preview.ts'
import gridStyles from './Grid.module.css'
import styles from './ContextBrowse.module.css'

export type DirectoryItem = {
  prdbId: string
  title: string
  detail?: string | null
  videoCount: number
  heldVideoCount?: number
  favourite: boolean
  artworkPath?: string | null
  artworkLabel?: string
}

export function DirectoryView({
  title,
  noun,
  items,
  search,
  page,
  pages,
  total,
  selectPath,
  releasePath,
  setFilter,
  goTo,
  toggleFavourite,
  scope,
  setScope,
  heldOnly,
  setHeldOnly,
}: {
  title: string
  noun: string
  items: readonly DirectoryItem[]
  search: string
  page: number
  pages: number
  total: number
  selectPath: (item: DirectoryItem) => string
  releasePath: (item: DirectoryItem) => string
  setFilter: (search: string) => void
  goTo: (page: number) => void
  toggleFavourite: (item: DirectoryItem) => ReactNode
  scope: 'Favourites' | 'All'
  setScope: (scope: 'Favourites' | 'All') => void
  heldOnly?: boolean
  setHeldOnly?: (held: boolean) => void
}) {
  return (
    <main className={styles.screen}>
      <div className={styles.browseFilters}>
        <Filter value={search} placeholder={`Search ${title.toLowerCase()}…`} apply={setFilter} />
        <nav className={styles.scope} aria-label={`${title} scope`}>
          <button type="button" aria-pressed={scope === 'Favourites'} onClick={() => setScope('Favourites')}>
            Favourites
          </button>
          <button type="button" aria-pressed={scope === 'All'} onClick={() => setScope('All')}>
            All
          </button>
        </nav>
        {setHeldOnly && (
          <button
            aria-pressed={heldOnly}
            className={styles.heldFilter}
            onClick={() => setHeldOnly(!heldOnly)}
            title="Show only Sites with videos in the Library"
            type="button"
          >
            <DirectoryIcon name="library" />
            In Library
          </button>
        )}
      </div>
      <div className={styles.summary}>
        <span>{total} {total === 1 ? noun : title} · Page {page} of {pages}</span>
        <SyncControl surface={noun === 'Actor' ? 'actors' : 'sites'} />
      </div>

      <PageNotice page={page} pages={pages} />
      {page > pages ? null : items.length === 0 ? (
        <div className={styles.empty}>
          <p>
            {scope === 'Favourites'
              ? `No favourite ${noun}s match this filter.`
              : `No ${noun}s match this filter.`}
          </p>
          {scope === 'Favourites' && (
            <button type="button" onClick={() => setScope('All')}>Show all</button>
          )}
        </div>
      ) : (
        <ul className={`${styles.directory} ${noun === 'Actor' ? styles.actorDirectory : ''}`}>
          {items.map((item) => (
            <li key={item.prdbId}>
              <Link className={styles.directoryVisual} to={selectPath(item)} aria-label={`Browse ${item.title}`}>
                {item.artworkPath ? (
                  <CachedArtwork
                    path={item.artworkPath}
                    title={item.artworkLabel ?? item.title}
                    frameClassName={styles.directoryFrame}
                    imageClassName={styles.directoryImage}
                    absentClassName={styles.directoryAbsent}
                  />
                ) : (
                  <span className={styles.directoryFrame} aria-label={item.artworkLabel ?? item.title}>
                    <span className={styles.directoryAbsent}>▤</span>
                  </span>
                )}
              </Link>
              <div>
                <Link className={styles.itemTitle} to={selectPath(item)}>
                  {item.title}
                </Link>
                <span className={styles.itemDetail}>
                  {[
                    item.detail,
                    `${item.videoCount} videos`,
                    item.heldVideoCount === undefined
                      ? null
                      : `${item.heldVideoCount} in Library`,
                  ].filter(Boolean).join(' · ')}
                </span>
              </div>
              <div className={styles.directoryActions}>
                {toggleFavourite(item)}
                <Link
                  aria-label={`View Releases for ${item.title}`}
                  className={styles.iconAction}
                  title="View Releases"
                  to={releasePath(item)}
                >
                  <DirectoryIcon name="releases" />
                </Link>
              </div>
            </li>
          ))}
        </ul>
      )}

      <Pager page={page} pages={pages} goTo={goTo} />
    </main>
  )
}

export function VideoContextView({
  title,
  backTo,
  backLabel,
  releaseAction,
  videos,
  search,
  page,
  pages,
  total,
  setFilter,
  goTo,
  videoAction,
  contextAction,
  contextDetail,
}: {
  title: string
  backTo: string
  backLabel: string
  releaseAction: string
  videos: readonly VideoCard[]
  search: string
  page: number
  pages: number
  total: number
  setFilter: (search: string) => void
  goTo: (page: number) => void
  videoAction: (video: VideoCard) => ReactNode
  contextAction?: ReactNode
  contextDetail?: ReactNode
}) {
  const location = useLocation()
  const { openPreview } = usePreview()
  const here = location.pathname + location.search

  return (
    <main className={styles.screen}>
      <Link className={styles.back} to={backTo}>
        ← {backLabel}
      </Link>
      <div className={styles.contextHeading}>
        <Heading title={title} total={total} noun="video" />
        <div className={styles.directoryActions}>
          {contextAction}
          <Link className={styles.actionButton} to={releaseAction}>View Releases</Link>
        </div>
      </div>
      {contextDetail}
      <Filter value={search} placeholder="Filter video titles…" apply={setFilter} />

      <PageNotice page={page} pages={pages} />
      {page > pages ? null : videos.length === 0 ? (
        <p className={styles.empty}>No videos match this filter.</p>
      ) : (
        <Grid compact
          videos={videos}
          onOpen={(video) => openPreview(video.prdbId)}
          action={(video) => <span className={gridStyles.actions}>{videoAction(video)}</span>}
        />
      )}

      <PreviewOverlay returnTo={here} videos={videos} />

      <Pager page={page} pages={pages} goTo={goTo} />
    </main>
  )
}

function DirectoryIcon({ name }: { name: 'library' | 'releases' }) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth: 1.8,
  }

  return (
    <svg aria-hidden="true" className={styles.actionIcon} viewBox="0 0 24 24">
      {name === 'library' && <path d="M4 5h16v15H4V5Zm4 0v15m9-15v15M3 9h18" {...common} />}
      {name === 'releases' && (
        <path d="M5 4h14v16H5V4Zm4 4h6m-6 4h6m-6 4h4" {...common} />
      )}
    </svg>
  )
}

function Heading({ title, total, noun, syncSurface }: { title: string; total: number; noun: string; syncSurface?: 'actors' | 'sites' }) {
  return (
    <div className={styles.heading}>
      <h1>{title}</h1>
      {total > 0 && (
        <span className={styles.count}>
          {total} {noun}{total === 1 ? '' : 's'}
        </span>
      )}
      {syncSurface && <SyncControl surface={syncSurface} />}
    </div>
  )
}

function Filter({
  value,
  placeholder,
  apply,
}: {
  value: string
  placeholder: string
  apply: (value: string) => void
}) {
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    apply(String(new FormData(event.currentTarget).get('search') ?? '').trim())
  }

  return (
    <form role="search" className={styles.filter} onSubmit={submit} key={value}>
      <input aria-label={placeholder.replace(/…$/, '')} name="search" type="search" defaultValue={value} placeholder={placeholder} />
      <button type="submit">Search</button>
      {value && (
        <button type="button" onClick={() => apply('')}>
          Clear
        </button>
      )}
    </form>
  )
}

function Pager({
  page,
  pages,
  goTo,
}: {
  page: number
  pages: number
  goTo: (page: number) => void
}) {
  if (pages <= 1 || page > pages) return null

  return (
    <nav className={styles.pager}>
      <button type="button" onClick={() => goTo(page - 1)} disabled={page <= 1}>
        Previous
      </button>
      <span>
        Page {page} of {pages}
      </span>
      <button type="button" onClick={() => goTo(page + 1)} disabled={page >= pages}>
        Next
      </button>
    </nav>
  )
}
