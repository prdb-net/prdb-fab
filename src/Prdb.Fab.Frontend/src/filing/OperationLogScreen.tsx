import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'

import { readOperationLog } from '../api/client.ts'
import { OperationRows } from './LibraryEntryScreen.tsx'
import styles from './Filing.module.css'

const acts = ['Filed', 'Replaced', 'Deleted', 'Tidied'] as const

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
  return <main className={styles.screen}>
    <header className={styles.heading}><div><h1>Operation Log</h1><p>An audit trail of changes to Video Files and leftover files.</p></div><span className={styles.quiet}>{Number(data?.total ?? 0)} operations</span></header>
    <div className={styles.filters}>
      <label>Act<select id="operation-act" name="act" className={styles.field} value={act} onChange={(event) => setAct(event.target.value)}><option value="">All acts</option>{acts.map((value) => <option key={value}>{value}</option>)}</select></label>
      <label>Path or file name<input id="operation-search" name="search" type="search" autoComplete="off" className={styles.field} value={searchInput} onChange={(event) => setSearchInput(event.target.value)} /></label>
    </div>
    {log.isError && <p className={styles.error}>The Operation Log could not be read.</p>}
    {data && <OperationRows entries={data.entries} />}
    <div className={styles.pager}><button className={styles.button} disabled={page <= 1} onClick={() => goTo(page - 1)}>Previous</button><span>Page {page}</span><button className={styles.button} disabled={!data || page * Number(data.pageSize) >= Number(data.total)} onClick={() => goTo(page + 1)}>Next</button></div>
  </main>
}
