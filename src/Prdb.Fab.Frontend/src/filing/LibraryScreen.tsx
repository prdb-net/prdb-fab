import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useLocation, useSearchParams } from 'react-router'

import { readLibrary } from '../api/client.ts'
import browseStyles from '../catalogue/BrowseScreen.module.css'
import { LibraryGrid } from '../catalogue/Grid.tsx'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { LibraryCardActions } from './LibraryCardActions.tsx'
import styles from './LibraryScreen.module.css'

export function LibraryScreen() {
  const location = useLocation()
  const [parameters, setParameters] = useSearchParams()
  const search = parameters.get('search') ?? ''
  const [searchInput, setSearchInput] = useState(search)
  const site = parameters.get('site') ?? ''
  const actor = parameters.get('actor') ?? ''
  const quality = parameters.get('quality') ?? ''
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const library = useQuery({
    queryKey: ['library', search, site, actor, quality, page],
    queryFn: () => readLibrary({ search, site, actor, quality, page }),
    placeholderData: (held) => held,
  })
  const data = library.data
  const total = Number(data?.total ?? 0)
  const pageSize = Number(data?.pageSize ?? 48)
  const pages = Math.max(1, Math.ceil(total / pageSize))
  useEffect(() => setSearchInput(search), [search])
  useEffect(() => {
    if (searchInput === search) return
    const timeout = window.setTimeout(() => {
      const next = new URLSearchParams(parameters)
      if (searchInput) next.set('search', searchInput)
      else next.delete('search')
      next.delete('page')
      setParameters(next, { replace: true })
    }, 250)
    return () => window.clearTimeout(timeout)
  }, [parameters, search, searchInput, setParameters])

  const setFilter = (name: 'site' | 'actor' | 'quality', value: string) => {
    const next = new URLSearchParams(parameters)
    if (searchInput) next.set('search', searchInput)
    else next.delete('search')
    if (value) next.set(name, value)
    else next.delete(name)
    next.delete('page')
    setParameters(next, { replace: true })
  }
  const goTo = (wanted: number) => {
    const next = new URLSearchParams(parameters)
    if (wanted <= 1) next.delete('page')
    else next.set('page', String(wanted))
    setParameters(next)
    window.scrollTo({ top: 0 })
  }

  if (library.isPending && !data) {
    return <PageLoading label="Loading Library" />
  }

  return (
    <main className={browseStyles.screen}>
      <div className={browseStyles.heading}>
        <h1>Library</h1>
        {total > 0 && <span className={browseStyles.count}>{total} videos</span>}
      </div>
      <p className={browseStyles.lede}>
        Browse the Videos held by this installation. Open an entry to see its files,
        qualities and where they are filed.
      </p>
      <div className={styles.filters}>
        <label className={styles.searchLabel}>
          Title
          <input id="library-title" name="search" type="search" autoComplete="off" className={`${styles.field} ${styles.searchField}`} value={searchInput} onChange={(event) => setSearchInput(event.target.value)} />
        </label>
        <label>
          Site
          <select id="library-site" name="site" className={styles.field} value={site} onChange={(event) => setFilter('site', event.target.value)}>
            <option value="">All Sites</option>
            {data?.filters.sites.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>
          Actor
          <select id="library-actor" name="actor" className={styles.field} value={actor} onChange={(event) => setFilter('actor', event.target.value)}>
            <option value="">All Actors</option>
            {data?.filters.actors.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>
          Quality
          <select id="library-quality" name="quality" className={styles.field} value={quality} onChange={(event) => setFilter('quality', event.target.value)}>
            <option value="">All Qualities</option>
            {data?.filters.qualities.map((item) => <option key={item}>{item}</option>)}
          </select>
        </label>
      </div>
      {library.isError && <p className={styles.error}>The Library could not be read.</p>}
      {data?.entries.length === 0
        ? <p className={browseStyles.empty}>No held Library Entries match these filters.</p>
        : <LibraryGrid
          entries={data?.entries ?? []}
          action={(entry) => <LibraryCardActions entry={entry} returnTo={location.pathname + location.search} />}
        />}
      {pages > 1 && (
        <nav className={browseStyles.pager}>
          <button type="button" disabled={page <= 1} onClick={() => goTo(page - 1)}>Previous</button>
          <span className={browseStyles.where}>Page {page} of {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => goTo(page + 1)}>Next</button>
        </nav>
      )}
    </main>
  )
}
