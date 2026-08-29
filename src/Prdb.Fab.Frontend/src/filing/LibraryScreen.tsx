import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readLibrary } from '../api/client.ts'
import styles from './Filing.module.css'

export function LibraryScreen() {
  const [search, setSearch] = useState('')
  const [site, setSite] = useState('')
  const [actor, setActor] = useState('')
  const [quality, setQuality] = useState('')
  const [page, setPage] = useState(1)
  const library = useQuery({
    queryKey: ['library', search, site, actor, quality, page],
    queryFn: () => readLibrary({ search, site, actor, quality, page }),
  })
  const data = library.data

  return (
    <main className={styles.screen}>
      <header className={styles.heading}>
        <div><h1>Library</h1><p>Video Files currently held by this installation.</p></div>
        <span className={styles.quiet}>{Number(data?.total ?? 0)} Entries</span>
      </header>
      <div className={styles.filters}>
        <label>Title<input className={styles.field} value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label>
        <label>Site<select className={styles.field} value={site} onChange={(event) => { setSite(event.target.value); setPage(1) }}><option value="">All Sites</option>{data?.filters.sites.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Actor<select className={styles.field} value={actor} onChange={(event) => { setActor(event.target.value); setPage(1) }}><option value="">All Actors</option>{data?.filters.actors.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Quality<select className={styles.field} value={quality} onChange={(event) => { setQuality(event.target.value); setPage(1) }}><option value="">All Qualities</option>{data?.filters.qualities.map((item) => <option key={item}>{item}</option>)}</select></label>
      </div>
      {library.isError && <p className={styles.error}>The Library could not be read.</p>}
      {data?.entries.length === 0 && <p className={styles.empty}>No held Library Entries match these filters.</p>}
      <ul className={styles.grid}>
        {data?.entries.map((entry) => <li className={styles.card} key={entry.id}>
          <Link className={styles.frame} to={`/library/${entry.id}`}><img src={`/api/artwork/${entry.id}`} alt="" /></Link>
          <h2><Link to={`/library/${entry.id}`}>{entry.title}</Link></h2>
          <div className={styles.metadata}>{[entry.site, entry.releaseDate].filter(Boolean).join(' · ')}</div>
          <div className={styles.badges}>{entry.qualities.map((item) => <span className={styles.badge} key={item}>{item}</span>)}</div>
        </li>)}
      </ul>
      <div className={styles.pager}><button className={styles.button} disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button><span>Page {page}</span><button className={styles.button} disabled={!data || page * Number(data.pageSize) >= Number(data.total)} onClick={() => setPage(page + 1)}>Next</button></div>
    </main>
  )
}
