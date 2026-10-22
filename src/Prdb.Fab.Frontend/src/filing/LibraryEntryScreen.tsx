import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'

import { readLibraryEntry } from '../api/client.ts'
import { InfoHelp } from '../ui/InfoHelp.tsx'
import { OperationList } from './OperationLogList.tsx'
import styles from './Filing.module.css'
import entryStyles from './LibraryEntryScreen.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'

const size = (bytes: number | string) => `${(Number(bytes) / 1024 / 1024 / 1024).toFixed(2)} GiB`
const duration = (seconds: number | string | null) => seconds == null ? 'Unknown' : `${Math.round(Number(seconds) / 60)} min`

export function LibraryEntryScreen() {
  const { id = '' } = useParams()
  const entry = useQuery({ queryKey: ['library-entry', id], queryFn: () => readLibraryEntry(id), enabled: Boolean(id) })
  const data = entry.data
  if (entry.isPending) return <PageLoading label="Loading Library entry" />
  if (!data) return <main className={styles.screen}>
    <Link to="/library">← Library</Link>
    <h1>Library entry</h1>
    <p className={styles.error} role="alert">This Library Entry could not be read.</p>
    <button type="button" className={styles.button} onClick={() => void entry.refetch()}>Try again</button>
  </main>
  const consensus = data.consensusRuntimeMs == null ? null : Math.round(Number(data.consensusRuntimeMs) / 60000)

  return <main className={styles.screen}>
    <p><Link to="/library">← Library</Link></p>
    <header className={`${styles.heading} ${entryStyles.heading}`}>
      <div><h1>{data.title}</h1><p>{[data.site, data.releaseDate].filter(Boolean).join(' · ')}</p></div>
    </header>
    <section className={styles.panel} aria-label="Video details">
      <dl className={entryStyles.metadata}>
        <div><dt>Actors</dt><dd>{data.actors.map((actor) => actor.name).join(', ') || 'None recorded'}</dd></div>
        <div><dt>Filed</dt><dd>{new Date(data.filedAt).toLocaleString()}</dd></div>
        <div><dt>prdb runtime</dt><dd>
          <InfoHelp label="Runtime consensus" heading={<span>{consensus == null ? 'Not available' : `${consensus} min`}</span>}>
            {consensus == null ? 'prdb has no runtime consensus for this Video.' : `Based on ${data.consensusRuntimeFileCount ?? 0} files, with a spread of ${Math.round(Number(data.consensusRuntimeSpreadMs ?? 0) / 1000)} seconds. This is a reference; each file’s measured runtime is shown below.`}
          </InfoHelp>
        </dd></div>
      </dl>
      <details className={entryStyles.path}><summary>Library directory</summary><code>{data.entryDirectory}</code></details>
    </section>
    <h2>Video Files <span className={entryStyles.count}>({data.files.length})</span></h2>
    {data.catalogueNotice && <p>{data.catalogueNotice}</p>}
    <ul className={entryStyles.files}>
      {data.files.map((file) => <li key={file.id}>
        <h3>{file.quality}</h3>
        <dl className={entryStyles.fileFacts}>
          <div><dt>Runtime</dt><dd>{duration(file.runtimeSeconds)}</dd></div>
          <div><dt>Size</dt><dd>{size(file.sizeBytes)}</dd></div>
          <div><dt>Resolution</dt><dd>{file.width && file.height ? `${file.width} × ${file.height}` : 'Unknown'}</dd></div>
          <div><dt>Codec</dt><dd>{file.videoCodec || 'Unknown'}</dd></div>
        </dl>
        <details className={entryStyles.path}><summary>File path</summary><code>{file.filedPath}</code></details>
      </li>)}
    </ul>
    <h2>Operation Log</h2>
    <OperationList entries={data.operations.entries} />
    {Number(data.operations.total) > data.operations.entries.length && <p className={styles.quiet}>
      Latest {data.operations.entries.length} of {Number(data.operations.total)} operations for this Video.
    </p>}
  </main>
}
