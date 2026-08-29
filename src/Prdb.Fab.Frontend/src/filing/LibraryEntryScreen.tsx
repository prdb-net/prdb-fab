import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'

import { readLibraryEntry, type OperationLogPage } from '../api/client.ts'
import styles from './Filing.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'

const size = (bytes: number | string) => `${(Number(bytes) / 1024 / 1024 / 1024).toFixed(2)} GiB`
const duration = (seconds: number | string | null) => seconds == null ? 'Unknown' : `${Math.round(Number(seconds) / 60)} min`

export function LibraryEntryScreen() {
  const { id = '' } = useParams()
  const entry = useQuery({ queryKey: ['library-entry', id], queryFn: () => readLibraryEntry(id), enabled: Boolean(id) })
  const data = entry.data
  if (entry.isPending) return <PageLoading label="Loading Library entry" />
  if (!data) return <main className={styles.screen}><p className={styles.error}>This Library Entry could not be read.</p></main>
  const consensus = data.consensusRuntimeMs == null ? null : Math.round(Number(data.consensusRuntimeMs) / 60000)

  return <main className={styles.screen}>
    <p><Link to="/library">← Library</Link></p>
    <header className={styles.heading}><div><h1>{data.title}</h1><p>{[data.site, data.releaseDate].filter(Boolean).join(' · ')}</p></div></header>
    <section className={styles.panel}>
      <h2>Entry</h2>
      <dl className={styles.definition}><dt>Actors</dt><dd>{data.actors.map((actor) => actor.name).join(', ') || 'None recorded'}</dd><dt>Directory</dt><dd><code>{data.entryDirectory}</code></dd><dt>Filed</dt><dd>{new Date(data.filedAt).toLocaleString()}</dd><dt>prdb runtime consensus</dt><dd>{consensus == null ? 'Not available' : `${consensus} min from ${data.consensusRuntimeFileCount ?? 0} file(s), spread ${Math.round(Number(data.consensusRuntimeSpreadMs ?? 0) / 1000)} s`}</dd></dl>
    </section>
    <h2>Video Files</h2>
    <div className={styles.tableFrame}><table className={styles.table}><thead><tr><th>Quality</th><th>Runtime</th><th>Size</th><th>Probe</th><th>Path</th></tr></thead><tbody>{data.files.map((file) => <tr key={file.id}><td>{file.quality}</td><td>{duration(file.runtimeSeconds)}</td><td>{size(file.sizeBytes)}</td><td>{file.width && file.height ? `${file.width}×${file.height}` : '—'} {file.videoCodec}</td><td><code>{file.filedPath}</code></td></tr>)}</tbody></table></div>
    <h2>Operation Log</h2>
    <OperationRows entries={data.operations.entries} />
  </main>
}

export function OperationRows({ entries }: { entries: OperationLogPage['entries'] }) {
  return <div className={styles.tableFrame}><table className={styles.table}><thead><tr><th>When</th><th>Act</th><th>Path</th><th>Reason</th></tr></thead><tbody>{entries.map((item) => <tr key={item.id}><td>{new Date(item.at).toLocaleString()}</td><td>{item.act}</td><td><code>{item.pathAfter ?? item.pathBefore ?? '—'}</code></td><td>{item.reason}</td></tr>)}</tbody></table>{entries.length === 0 && <p className={styles.empty}>No operation has been recorded.</p>}</div>
}
