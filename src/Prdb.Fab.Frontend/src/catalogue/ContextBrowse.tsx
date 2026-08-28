import type { FormEvent, ReactNode } from 'react'
import { Link } from 'react-router'

import type { VideoCard } from '../api/client.ts'
import { Grid } from './Grid.tsx'
import gridStyles from './Grid.module.css'
import styles from './ContextBrowse.module.css'

export type DirectoryItem = {
  prdbId: string
  title: string
  detail?: string | null
  videoCount: number
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
}) {
  return (
    <main className={styles.screen}>
      <Heading title={title} total={total} noun={noun} />
      <Filter value={search} placeholder={`Filter ${title.toLowerCase()}…`} apply={setFilter} />

      {items.length === 0 ? (
        <p className={styles.empty}>No {noun}s match this filter.</p>
      ) : (
        <ul className={styles.directory}>
          {items.map((item) => (
            <li key={item.prdbId}>
              <div>
                <Link className={styles.itemTitle} to={selectPath(item)}>
                  {item.title}
                </Link>
                <span className={styles.itemDetail}>
                  {[item.detail, `${item.videoCount} videos`].filter(Boolean).join(' · ')}
                </span>
              </div>
              <Link to={releasePath(item)}>Find releases</Link>
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
}) {
  return (
    <main className={styles.screen}>
      <Link className={styles.back} to={backTo}>
        ← {backLabel}
      </Link>
      <div className={styles.contextHeading}>
        <Heading title={title} total={total} noun="video" />
        <Link to={releaseAction}>Find releases for this selection</Link>
      </div>
      <Filter value={search} placeholder="Filter video titles…" apply={setFilter} />

      {videos.length === 0 ? (
        <p className={styles.empty}>No videos match this filter.</p>
      ) : (
        <Grid
          videos={videos}
          action={(video) => <span className={gridStyles.actions}>{videoAction(video)}</span>}
        />
      )}

      <Pager page={page} pages={pages} goTo={goTo} />
    </main>
  )
}

function Heading({ title, total, noun }: { title: string; total: number; noun: string }) {
  return (
    <div className={styles.heading}>
      <h1>{title}</h1>
      {total > 0 && (
        <span className={styles.count}>
          {total} {noun}{total === 1 ? '' : 's'}
        </span>
      )}
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
    <form className={styles.filter} onSubmit={submit} key={value}>
      <input name="search" type="search" defaultValue={value} placeholder={placeholder} />
      <button type="submit">Apply</button>
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
  if (pages <= 1) return null

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
