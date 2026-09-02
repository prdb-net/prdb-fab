import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'

import { readOperationLog } from '../api/client.ts'
import { OperationList } from './OperationLogList.tsx'
import styles from './Filing.module.css'

const acts = ['Filed', 'Relabelled', 'Replaced', 'Deleted', 'Tidied'] as const

export function OperationLogScreen() {
  const [parameters, setParameters] = useSearchParams()
  const actParameter = parameters.get('act')
  const act = acts.find((value) => value === actParameter) ?? ''
  const search = parameters.get('search') ?? ''
  const [searchInput, setSearchInput] = useState(search)
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const log = useQuery({ queryKey: ['operation-log', act, search, page], queryFn: () => readOperationLog({ act, search, page }), placeholderData: (held) => held })
  const data = log.data
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

  const setAct = (value: string) => {
    const next = new URLSearchParams(parameters)
    if (searchInput) next.set('search', searchInput)
    else next.delete('search')
    if (value) next.set('act', value)
    else next.delete('act')
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
  const total = Number(data?.total ?? 0)
  const pageSize = Number(data?.pageSize ?? 0)
  const first = total === 0 ? 0 : ((page - 1) * pageSize) + 1
  const last = Math.min(page * pageSize, total)
  const hasMultiplePages = total > pageSize
  const operationCount = `${total} ${total === 1 ? 'operation' : 'operations'}`

  return <main className={`${styles.screen} ${styles.operationScreen}`}>
    <header className={styles.heading}><div><h1>Operation Log</h1><p>What changed on disk, who changed it, and why.</p></div><span className={styles.quiet}>{operationCount}</span></header>
    <div className={styles.operationFilters}>
      <label className={styles.operationSearch}>
        <span>Search</span>
        <span className={styles.operationSearchControl}>
          <svg aria-hidden="true" viewBox="0 0 24 24"><path d="m20 20-4.3-4.3m2.3-5.2a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z" /></svg>
          <input id="operation-search" name="search" type="search" autoComplete="off" className={styles.field} placeholder="Search file names or paths…" value={searchInput} onChange={(event) => setSearchInput(event.target.value)} />
        </span>
      </label>
      <div aria-label="Filter by act" className={styles.operationActFilters} role="group">
        <span>Act</span>
        <div className={styles.operationActButtons}>
          <button aria-pressed={!act} className={!act ? styles.operationActButtonActive : ''} onClick={() => setAct('')} type="button">All</button>
          {acts.map((value) => <button aria-pressed={act === value} className={act === value ? styles.operationActButtonActive : ''} key={value} onClick={() => setAct(value)} type="button">{value}</button>)}
        </div>
      </div>
      <label className={styles.operationActSelect}>Act<select id="operation-act" name="act" className={styles.field} value={act} onChange={(event) => setAct(event.target.value)}><option value="">All acts</option>{acts.map((value) => <option key={value}>{value}</option>)}</select></label>
    </div>
    {log.isError && <p className={styles.error}>The Operation Log could not be read.</p>}
    {data && <OperationList entries={data.entries} />}
    {data && <div className={styles.operationPager}>
      <span>{total === 0 ? 'No matching operations' : `${first}–${last} of ${total}`}</span>
      {hasMultiplePages && <div>
        <button className={styles.button} disabled={page <= 1} onClick={() => goTo(page - 1)}>Previous</button>
        <button className={styles.button} disabled={page * pageSize >= total} onClick={() => goTo(page + 1)}>Next</button>
      </div>}
    </div>}
  </main>
}
