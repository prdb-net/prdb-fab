import type { FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useSearchParams } from 'react-router'

import { listVideos, setWanted } from '../api/client.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { videoReleasePath } from '../release/routes.ts'
import { Grid } from './Grid.tsx'
import gridStyles from './Grid.module.css'
import { PreferenceButton } from './PreferenceButton.tsx'
import styles from './SearchScreen.module.css'

/** The local Catalogue doorway into a person-requested Indexer search. */
export function SearchScreen() {
  const [parameters, setParameters] = useSearchParams()
  const location = useLocation()
  const search = parameters.get('search') ?? ''
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const videos = useQuery({
    queryKey: ['catalogue-videos', search, page],
    queryFn: () => listVideos(search, page),
    placeholderData: (held) => held,
  })
  const total = Number(videos.data?.total ?? 0)
  const pages = Math.max(1, Math.ceil(total / Number(videos.data?.pageSize ?? 48)))

  const apply = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const value = String(new FormData(event.currentTarget).get('search') ?? '').trim()
    setParameters(value ? { search: value } : {})
  }
  const goTo = (wanted: number) => {
    const next = new URLSearchParams(parameters)
    if (wanted === 1) next.delete('page')
    else next.set('page', String(wanted))
    setParameters(next)
    window.scrollTo({ top: 0 })
  }

  if (videos.isPending && !videos.data) return <PageLoading label="Searching the Catalogue" />

  return (
    <main className={styles.screen}>
      <div className={styles.heading}>
        <div>
          <h1>Search</h1>
          <p>Choose a locally known Video, then search enabled Indexers from its Release page.</p>
        </div>
        {total > 0 && <span>{total} videos</span>}
      </div>
      <form className={styles.filter} onSubmit={apply} key={search}>
        <input autoFocus name="search" type="search" defaultValue={search} placeholder="Search Video titles…" />
        <button type="submit">Search Catalogue</button>
        {search && <button type="button" onClick={() => setParameters({})}>Clear</button>}
      </form>

      {videos.isError ? (
        <p className={styles.empty}>The local Catalogue could not be searched.</p>
      ) : total === 0 ? (
        <p className={styles.empty}>No locally known Videos match this title.</p>
      ) : (
        <Grid
          videos={videos.data?.videos ?? []}
          action={(video) => (
            <span className={gridStyles.actions}>
              <Link to={videoReleasePath(video.prdbId, location.pathname + location.search)}>
                Search Indexers
              </Link>
              <PreferenceButton
                active={video.wanted}
                activeLabel="Remove Wanted"
                inactiveLabel="Mark Wanted"
                write={(desired) => setWanted(video.prdbId, desired)}
              />
            </span>
          )}
        />
      )}

      {pages > 1 && (
        <nav className={styles.pager}>
          <button type="button" onClick={() => goTo(page - 1)} disabled={page <= 1}>Previous</button>
          <span>Page {page} of {pages}</span>
          <button type="button" onClick={() => goTo(page + 1)} disabled={page >= pages}>Next</button>
        </nav>
      )}
    </main>
  )
}
