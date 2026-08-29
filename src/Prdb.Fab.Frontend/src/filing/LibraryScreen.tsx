import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'

import { readLibrary } from '../api/client.ts'
import { Artwork } from '../catalogue/Grid.tsx'
import styles from './Filing.module.css'

export function LibraryScreen() {
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

  return (
    <main className={styles.screen}>
      <header className={styles.heading}>
        <div><h1>Library</h1><p>Video Files currently held by this installation.</p></div>
        <span className={styles.quiet}>{Number(data?.total ?? 0)} Entries</span>
      </header>
      <div className={styles.filters}>
        <label>
          Title
          <input id="library-title" name="search" type="search" autoComplete="off" className={styles.field} value={searchInput} onChange={(event) => setSearchInput(event.target.value)} />
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
      {data?.entries.length === 0 && <p className={styles.empty}>No held Library Entries match these filters.</p>}
      <ul className={styles.grid}>
        {data?.entries.map((entry) => <li className={styles.card} key={entry.id}>
          <Link className={styles.frame} to={`/library/${entry.id}`}>
            <Artwork
              videoId={entry.artworkId}
              title={entry.title}
              frameClassName={styles.artwork}
              imageClassName={styles.artworkImage}
              absentClassName={styles.artworkAbsent}
            />
          </Link>
          <h2><Link to={`/library/${entry.id}`}>{entry.title}</Link></h2>
          <div className={styles.metadata}>{[entry.site, entry.releaseDate].filter(Boolean).join(' · ')}</div>
          <div className={styles.badges}>{entry.qualities.map((item) => <span className={styles.badge} key={item}>{item}</span>)}</div>
        </li>)}
      </ul>
      <div className={styles.pager}><button className={styles.button} disabled={page <= 1} onClick={() => goTo(page - 1)}>Previous</button><span>Page {page}</span><button className={styles.button} disabled={!data || page * Number(data.pageSize) >= Number(data.total)} onClick={() => goTo(page + 1)}>Next</button></div>
    </main>
  )
}
