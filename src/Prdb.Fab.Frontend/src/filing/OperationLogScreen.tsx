import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'

import { readOperationLog } from '../api/client.ts'
import { OperationRows } from './LibraryEntryScreen.tsx'
import styles from './Filing.module.css'

export function OperationLogScreen() {
  const [act, setAct] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const log = useQuery({ queryKey: ['operation-log', act, search, page], queryFn: () => readOperationLog({ act, search, page }) })
  const data = log.data
  return <main className={styles.screen}>
    <header className={styles.heading}><div><h1>Operation Log</h1><p>An audit trail of changes to Video Files and leftover files.</p></div><span className={styles.quiet}>{Number(data?.total ?? 0)} operations</span></header>
    <div className={styles.filters}>
      <label>Act<select className={styles.field} value={act} onChange={(event) => { setAct(event.target.value); setPage(1) }}><option value="">All acts</option>{['Filed', 'Replaced', 'Deleted', 'Tidied'].map((value) => <option key={value}>{value}</option>)}</select></label>
      <label>Path or file name<input className={styles.field} value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label>
    </div>
    {log.isError && <p className={styles.error}>The Operation Log could not be read.</p>}
    {data && <OperationRows entries={data.entries} />}
    <div className={styles.pager}><button className={styles.button} disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button><span>Page {page}</span><button className={styles.button} disabled={!data || page * Number(data.pageSize) >= Number(data.total)} onClick={() => setPage(page + 1)}>Next</button></div>
  </main>
}
