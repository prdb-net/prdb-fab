import type { FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useLocation, useSearchParams } from 'react-router'

import {
  listVideos,
  type CatalogueVideoFilter,
  type CatalogueVideoSort,
} from '../api/client.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { CardActions } from './CardActions.tsx'
import { Grid } from './Grid.tsx'
import styles from './SearchScreen.module.css'

const filters: readonly CatalogueVideoFilter[] = [
  'Available',
  'All',
  'DownloadReady',
  'NeedsSearch',
  'Wanted',
  'Held',
  'Outstanding',
]

const sorts: readonly CatalogueVideoSort[] = [
  'ReleaseDateDescending',
  'ReleaseDateAscending',
  'CreatedDescending',
  'TitleAscending',
  'TitleDescending',
]

/** The local Catalogue doorway into a person-requested Indexer search. */
export function SearchScreen() {
  const [parameters, setParameters] = useSearchParams()
  const location = useLocation()
  const search = parameters.get('search') ?? ''
  const filter = choice(parameters.get('filter'), filters, 'Available')
  const availableSorts = search ? (['Relevance', ...sorts] as const) : sorts
  const sort = choice(
    parameters.get('sort'),
    availableSorts,
    search ? 'Relevance' : 'ReleaseDateDescending',
  )
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const videos = useQuery({
    queryKey: ['catalogue-videos', search, filter, sort, page],
    queryFn: () => listVideos(search, page, filter, sort),
    placeholderData: (held) => held,
  })
  const total = Number(videos.data?.total ?? 0)
  const pages = Math.max(1, Math.ceil(total / Number(videos.data?.pageSize ?? 24)))

  const apply = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const value = String(new FormData(event.currentTarget).get('search') ?? '').trim()
    const next = new URLSearchParams(parameters)
    if (value) next.set('search', value)
    else next.delete('search')
    if (!value && next.get('sort') === 'Relevance') next.delete('sort')
    next.delete('page')
    setParameters(next)
  }
  const setChoice = (name: 'filter' | 'sort', value: string) => {
    const next = new URLSearchParams(parameters)
    const isDefault = name === 'filter'
      ? value === 'Available'
      : value === (search ? 'Relevance' : 'ReleaseDateDescending')
    if (isDefault) next.delete(name)
    else next.set(name, value)
    next.delete('page')
    setParameters(next)
  }
  const clearSearch = () => {
    const next = new URLSearchParams(parameters)
    next.delete('search')
    next.delete('page')
    if (sort === 'Relevance') next.delete('sort')
    setParameters(next)
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
          <p>Find a Video in the local prdb Catalogue, then search enabled Indexers from its Release page.</p>
        </div>
        {total > 0 && <span>{total} videos</span>}
      </div>

      <div className={styles.controls}>
        <form className={styles.search} onSubmit={apply} key={search}>
          <label htmlFor="catalogue-search">Video title</label>
          <span>
            <input id="catalogue-search" autoFocus name="search" type="search" defaultValue={search} placeholder="Search Video titles…" />
            <button type="submit">Search</button>
            {search && <button type="button" onClick={clearSearch}>Clear</button>}
          </span>
        </form>
        <label>
          Show
          <select value={filter} onChange={(event) => setChoice('filter', event.target.value)}>
            {filters.map((value) => <option value={value} key={value}>{filterLabel(value)}</option>)}
          </select>
        </label>
        <label>
          Sort by
          <select value={sort} onChange={(event) => setChoice('sort', event.target.value)}>
            {availableSorts.map((value) => <option value={value} key={value}>{sortLabel(value)}</option>)}
          </select>
        </label>
      </div>

      <p className={styles.summary}>{summary(filter, sort, search)}</p>

      {videos.isError ? (
        <p className={styles.empty}>The local Catalogue could not be searched.</p>
      ) : total === 0 ? (
        <div className={styles.empty}>
          <p>No locally known Videos match this search and filter.</p>
          {filter !== 'All' && (
            <button type="button" onClick={() => setChoice('filter', 'All')}>Show all locally known Videos</button>
          )}
        </div>
      ) : (
        <Grid
          videos={videos.data?.videos ?? []}
          action={(video) => (
            <CardActions
              includeSite
              video={video}
              returnTo={location.pathname + location.search}
            />
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

function choice<T extends string>(supplied: string | null, choices: readonly T[], fallback: T): T {
  return choices.find((value) => value === supplied) ?? fallback
}

function filterLabel(filter: CatalogueVideoFilter): string {
  const labels: Record<CatalogueVideoFilter, string> = {
    Available: 'Available to acquire',
    All: 'All locally known',
    DownloadReady: 'Ready to download',
    NeedsSearch: 'Needs an Indexer search',
    Wanted: 'Wanted',
    Held: 'In the Library',
    Outstanding: 'Download outstanding',
  }
  return labels[filter]
}

function sortLabel(sort: CatalogueVideoSort): string {
  const labels: Record<CatalogueVideoSort, string> = {
    ReleaseDateDescending: 'Newest release',
    ReleaseDateAscending: 'Oldest release',
    CreatedDescending: 'Recently added to prdb',
    Relevance: 'Title relevance',
    TitleAscending: 'Title A–Z',
    TitleDescending: 'Title Z–A',
  }
  return labels[sort]
}

function summary(filter: CatalogueVideoFilter, sort: CatalogueVideoSort, search: string): string {
  const population = filterLabel(filter).toLowerCase()
  const order = sortLabel(sort).toLowerCase()
  return search
    ? `Showing ${population} matching “${search}”, sorted by ${order}.`
    : `Showing ${population}, sorted by ${order}.`
}
