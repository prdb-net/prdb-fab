import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  deleteReviewEntries,
  dismissReviewEntries,
  fileOnlyCopyFromReview,
  fileReviewAs,
  previewReviewDelete,
  readReviewQueue,
  replaceFromReview,
  searchReviewVideos,
  type ArrivingFileReason,
  type ReviewQueueEntry,
  type ReviewVideo,
} from '../api/client.ts'
import styles from './Filing.module.css'

const reasons: ArrivingFileReason[] = ['IdenticalFile', 'UnreadableQuality', 'Unidentified', 'Duplicate', 'EntryMissing']
const bytes = (value: number | string) => `${(Number(value) / 1024 / 1024).toFixed(1)} MiB`

export function ReviewQueueScreen() {
  const queries = useQueryClient()
  const [reason, setReason] = useState<ArrivingFileReason | ''>('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string[]>([])
  const queue = useQuery({ queryKey: ['review-queue', reason, page], queryFn: () => readReviewQueue({ reason: reason || undefined, page }) })
  const refresh = async () => { setSelected([]); await queries.invalidateQueries({ queryKey: ['review-queue'] }); await queries.invalidateQueries({ queryKey: ['review-queue-count'] }) }
  const dismiss = useMutation({ mutationFn: dismissReviewEntries, onSuccess: refresh })
  const remove = useMutation({
    mutationFn: async (ids: string[]) => {
      const preview = await previewReviewDelete(ids)
      if (preview.outcome !== 'Ready') return null
      const listing = preview.files.map((file) => `${file.fileName} — ${bytes(file.sizeBytes)}`).join('\n')
      return window.confirm(`Delete these Video Files permanently?\n\n${listing}`) ? deleteReviewEntries(ids) : null
    },
    onSuccess: (answer) => { if (answer) void refresh() },
  })
  const data = queue.data
  return <main className={styles.screen}>
    <header className={styles.heading}><div><h1>Review Queue</h1><p>Every row has Dismiss and Delete; only its reason may add one corrective action.</p></div><span className={styles.quiet}>{Number(data?.globalCount ?? 0)} open</span></header>
    <div className={styles.filters}><label>Reason<select className={styles.field} value={reason} onChange={(event) => { setReason(event.target.value as ArrivingFileReason | ''); setPage(1) }}><option value="">All reasons</option>{reasons.map((value) => <option key={value}>{value}</option>)}</select></label></div>
    <div className={styles.actions}><button className={styles.button} disabled={selected.length === 0} onClick={() => dismiss.mutate(selected)}>Dismiss selected</button><button className={styles.button} disabled={selected.length === 0} onClick={() => remove.mutate(selected)}>Delete selected…</button><span className={styles.quiet}>{selected.length} selected</span></div>
    {queue.isError && <p className={styles.error}>The Review Queue could not be read.</p>}
    {data?.entries.length === 0 && <p className={styles.empty}>Nothing needs review.</p>}
    <div className={styles.reviewList}>{data?.entries.map((entry) => <ReviewRow key={entry.id} entry={entry} checked={selected.includes(entry.id)} onChecked={(checked) => setSelected(checked ? [...selected, entry.id] : selected.filter((id) => id !== entry.id))} onChanged={refresh} />)}</div>
    <div className={styles.pager}><button className={styles.button} disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button><span>Page {page}</span><button className={styles.button} disabled={!data || page * Number(data.pageSize) >= Number(data.total)} onClick={() => setPage(page + 1)}>Next</button></div>
  </main>
}

function ReviewRow({ entry, checked, onChecked, onChanged }: { entry: ReviewQueueEntry; checked: boolean; onChecked: (value: boolean) => void; onChanged: () => Promise<void> }) {
  const [message, setMessage] = useState('')
  const act = useMutation({
    mutationFn: async (video?: ReviewVideo) => {
      if (entry.actingAction === 'FileAs' && video) return fileReviewAs(entry.id, video.id)
      if (entry.actingAction === 'Replace' && entry.filedFile && window.confirm(`Replace ${entry.filedFile.path} (${bytes(entry.filedFile.sizeBytes)}) with ${entry.fileName} (${bytes(entry.sizeBytes)})?`)) return replaceFromReview(entry.id)
      if (entry.actingAction === 'FileAsOnlyCopy' && window.confirm(`The recorded copy is missing. File ${entry.fileName} as the only copy?`)) return fileOnlyCopyFromReview(entry.id)
      return null
    },
    onSuccess: async (answer) => { if (answer) { setMessage(answer.detail); await onChanged() } },
  })
  return <article className={`${styles.panel} ${styles.review}`}>
    <input type="checkbox" aria-label={`Select ${entry.fileName}`} checked={checked} onChange={(event) => onChecked(event.target.checked)} />
    <div className={styles.reviewBody}>
      <h2>{entry.fileName}</h2><div className={styles.badges}><span className={styles.badge}>{entry.reason}</span>{entry.quality && <span className={styles.badge}>{entry.quality}</span>}<span className={styles.badge}>{bytes(entry.sizeBytes)}</span></div>
      <div className={styles.evidence}><span><b>Download:</b> {entry.download.name}</span><span><b>Release:</b> {entry.release}</span><span><b>Indexer:</b> {entry.indexer}</span><span><b>Path:</b> <code>{entry.path}</code></span><span><b>Identification:</b> {entry.video?.title ?? 'None'} {entry.confidence && `(${entry.confidence}, ${entry.matchedBy})`}</span>{entry.filedFile && <span><b>Filed copy:</b> <code>{entry.filedFile.path}</code> ({bytes(entry.filedFile.sizeBytes)})</span>}{entry.probeError && <span className={styles.error}><b>Probe:</b> {entry.probeError}</span>}</div>
      {entry.actingAction === 'FileAs' && <Picker candidates={entry.candidates} onPick={(video) => act.mutate(video)} />}
      {entry.actingAction && entry.actingAction !== 'FileAs' && <button className={styles.button} disabled={act.isPending} onClick={() => act.mutate(undefined)}>{entry.actingAction === 'Replace' ? 'Replace filed copy…' : 'File as only copy…'}</button>}
      {message && <p className={styles.notice}>{message}</p>}{act.isError && <p className={styles.error}>{String(act.error)}</p>}
    </div>
  </article>
}

function Picker({ candidates, onPick }: { candidates: ReviewVideo[]; onPick: (video: ReviewVideo) => void }) {
  const [search, setSearch] = useState('')
  const results = useQuery({ queryKey: ['review-video-search', search], queryFn: () => searchReviewVideos(search), enabled: search.trim().length >= 2 })
  const videos = search.trim().length >= 2 ? results.data?.videos ?? [] : candidates
  return <div className={styles.picker}><div className={styles.actions}><input className={styles.field} placeholder="Search prdb Videos" value={search} onChange={(event) => setSearch(event.target.value)} /></div><div className={styles.pickerResults}>{videos.map((video) => <div className={styles.candidate} key={video.id}><span>{video.title}{video.site ? ` · ${video.site}` : ''}</span><button className={styles.button} onClick={() => onPick(video)}>File as this Video</button></div>)}</div></div>
}
