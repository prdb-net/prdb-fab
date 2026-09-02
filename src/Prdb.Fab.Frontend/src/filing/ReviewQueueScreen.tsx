import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'

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
  type ReviewSelectionPreview,
  type ReviewVideo,
} from '../api/client.ts'
import { Artwork } from '../catalogue/Grid.tsx'
import styles from './Filing.module.css'

const reasons: ArrivingFileReason[] = ['IdenticalFile', 'UnreadableQuality', 'Unidentified', 'Duplicate', 'EntryMissing']

const reasonLabels: Record<ArrivingFileReason, string> = {
  IdenticalFile: 'Identical file',
  UnreadableQuality: 'Unreadable quality',
  Unidentified: 'Unidentified',
  Duplicate: 'Duplicate',
  EntryMissing: 'Library entry missing',
}

const reasonDescriptions: Record<ArrivingFileReason, string> = {
  IdenticalFile: 'Its bytes are already held in the Library.',
  UnreadableQuality: 'The file probe could not determine a quality.',
  Unidentified: 'No video was identified strongly enough to file this automatically.',
  Duplicate: 'The Library already holds this video at the same quality.',
  EntryMissing: 'The recorded Library copy is no longer present on disk.',
}

type Feedback = { tone: 'notice' | 'error'; text: string }

export function ReviewQueueScreen() {
  const queries = useQueryClient()
  const [parameters, setParameters] = useSearchParams()
  const reasonValue = parameters.get('reason')
  const reason = reasons.find((value) => value === reasonValue) ?? ''
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const [selected, setSelected] = useState<string[]>([])
  const [feedback, setFeedback] = useState<Feedback | null>(null)
  const [deleteConfirmation, setDeleteConfirmation] = useState<ReviewSelectionPreview | null>(null)
  useEffect(() => setSelected([]), [page, reason])

  const queue = useQuery({
    queryKey: ['review-queue', reason, page],
    queryFn: () => readReviewQueue({ reason: reason || undefined, page }),
  })
  const refresh = async () => {
    setSelected([])
    await queries.invalidateQueries({ queryKey: ['review-queue'] })
    await queries.invalidateQueries({ queryKey: ['review-queue-count'] })
  }
  const dismiss = useMutation({
    mutationFn: dismissReviewEntries,
    onSuccess: async (answer) => {
      setFeedback({ tone: answer.outcome === 'Dismissed' ? 'notice' : 'error', text: answer.detail })
      await refresh()
    },
    onError: (error) => setFeedback({ tone: 'error', text: messageOf(error) }),
  })
  const previewDelete = useMutation({
    mutationFn: previewReviewDelete,
    onSuccess: (answer) => {
      if (answer.outcome === 'Ready') setDeleteConfirmation(answer)
      else setFeedback({ tone: 'error', text: answer.detail })
    },
    onError: (error) => setFeedback({ tone: 'error', text: messageOf(error) }),
  })
  const remove = useMutation({
    mutationFn: deleteReviewEntries,
    onSuccess: async (answer) => {
      setDeleteConfirmation(null)
      setFeedback({ tone: answer.outcome === 'Deleted' ? 'notice' : 'error', text: answer.detail })
      await refresh()
    },
    onError: (error) => setFeedback({ tone: 'error', text: messageOf(error) }),
  })

  const data = queue.data
  const visibleIds = data?.entries.map((entry) => entry.id) ?? []
  const allVisibleSelected = visibleIds.length > 0 && visibleIds.every((id) => selected.includes(id))
  const someVisibleSelected = visibleIds.some((id) => selected.includes(id))
  const selectionBusy = dismiss.isPending || previewDelete.isPending || remove.isPending
  const globalCount = Number(data?.globalCount ?? 0)
  const matchingCount = Number(data?.total ?? 0)
  const setReason = (value: ArrivingFileReason | '') => {
    const next = new URLSearchParams(parameters)
    if (value) next.set('reason', value)
    else next.delete('reason')
    next.delete('page')
    setSelected([])
    setFeedback(null)
    setParameters(next)
  }
  const goTo = (wanted: number) => {
    const next = new URLSearchParams(parameters)
    if (wanted <= 1) next.delete('page')
    else next.set('page', String(wanted))
    setSelected([])
    setParameters(next)
    window.scrollTo({ top: 0 })
  }
  const toggleVisible = () => {
    setSelected((held) => allVisibleSelected
      ? held.filter((id) => !visibleIds.includes(id))
      : [...new Set([...held, ...visibleIds])])
  }

  return <main className={styles.screen} aria-busy={queue.isFetching}>
    <header className={styles.heading}>
      <div>
        <h1>Review Queue</h1>
        <p>Select one or more files to dismiss or delete them. Each reason may offer one additional corrective action.</p>
      </div>
      <span className={styles.quiet}>{reason ? `${matchingCount} matching · ${globalCount} open` : `${globalCount} open`}</span>
    </header>

    <div className={styles.filters}>
      <label htmlFor="review-reason">Reason</label>
      <select id="review-reason" name="reason" className={styles.field} value={reason} onChange={(event) => setReason(event.target.value as ArrivingFileReason | '')}>
        <option value="">All reasons</option>
        {reasons.map((value) => <option value={value} key={value}>{reasonLabels[value]}</option>)}
      </select>
    </div>

    <div className={styles.selectionBar}>
      <label className={styles.selectAll}>
        <SelectionCheckbox
          ariaLabel="Select every file on this page"
          checked={allVisibleSelected}
          disabled={visibleIds.length === 0 || selectionBusy}
          indeterminate={!allVisibleSelected && someVisibleSelected}
          onChange={toggleVisible}
        />
        Select page
      </label>
      <div className={styles.selectionActions}>
        <button className={styles.button} disabled={selected.length === 0 || selectionBusy} onClick={() => dismiss.mutate(selected)}>
          {dismiss.isPending ? 'Dismissing…' : 'Dismiss selected'}
        </button>
        <button className={`${styles.button} ${styles.dangerButton}`} disabled={selected.length === 0 || selectionBusy} onClick={() => previewDelete.mutate(selected)}>
          {previewDelete.isPending ? 'Checking files…' : 'Delete selected…'}
        </button>
      </div>
      <span className={styles.quiet}>{selected.length} selected{queue.isFetching ? ' · Refreshing…' : ''}</span>
    </div>

    {feedback && <p className={feedback.tone === 'error' ? styles.error : styles.notice} role="status">{feedback.text}</p>}
    {queue.isError && <p className={styles.error} role="alert">The Review Queue could not be read.</p>}
    {data?.entries.length === 0 && <p className={styles.empty}>Nothing needs review.</p>}
    <div className={styles.reviewList}>
      {data?.entries.map((entry) => <ReviewRow
        key={entry.id}
        entry={entry}
        checked={selected.includes(entry.id)}
        onChecked={(checked) => setSelected((held) => checked ? [...new Set([...held, entry.id])] : held.filter((id) => id !== entry.id))}
        onChanged={refresh}
        onFeedback={setFeedback}
      />)}
    </div>

    {data && Number(data.total) > Number(data.pageSize) && <QueuePager
      page={page}
      pageSize={Number(data.pageSize)}
      total={Number(data.total)}
      onPage={goTo}
    />}

    {deleteConfirmation && <ConfirmationDialog
      title="Delete video files permanently?"
      confirmLabel={remove.isPending ? 'Deleting…' : `Delete ${deleteConfirmation.files.length} file${deleteConfirmation.files.length === 1 ? '' : 's'}`}
      busy={remove.isPending}
      danger
      onCancel={() => setDeleteConfirmation(null)}
      onConfirm={() => remove.mutate(deleteConfirmation.files.map((file) => file.id))}
    >
      <p>This cannot be undone. The files will be removed from disk and the acts recorded in the Operation Log.</p>
      <p className={styles.dialogSummary}><strong>{formatBytes(deleteConfirmation.files.reduce((total, file) => total + Number(file.sizeBytes), 0))}</strong> across {deleteConfirmation.files.length} selected file{deleteConfirmation.files.length === 1 ? '' : 's'}.</p>
      <ul className={styles.confirmationList}>
        {deleteConfirmation.files.map((file) => <li key={file.id}><strong>{file.fileName}</strong><span>{formatBytes(file.sizeBytes)}</span><code>{file.path}</code></li>)}
      </ul>
    </ConfirmationDialog>}
  </main>
}

function ReviewRow({
  entry,
  checked,
  onChecked,
  onChanged,
  onFeedback,
}: {
  entry: ReviewQueueEntry
  checked: boolean
  onChecked: (value: boolean) => void
  onChanged: () => Promise<void>
  onFeedback: (feedback: Feedback) => void
}) {
  const [confirmation, setConfirmation] = useState<'Replace' | 'FileAsOnlyCopy' | null>(null)
  const act = useMutation({
    mutationFn: async (video?: ReviewVideo) => {
      if (entry.actingAction === 'FileAs' && video) return fileReviewAs(entry.id, video.id)
      if (entry.actingAction === 'Replace') return replaceFromReview(entry.id)
      if (entry.actingAction === 'FileAsOnlyCopy') return fileOnlyCopyFromReview(entry.id)
      return null
    },
    onSuccess: async (answer) => {
      setConfirmation(null)
      if (!answer) return
      onFeedback({ tone: answer.outcome === 'QueuedForFiling' || answer.outcome === 'QueuedForReplacement' ? 'notice' : 'error', text: answer.detail })
      await onChanged()
    },
    onError: (error) => onFeedback({ tone: 'error', text: messageOf(error) }),
  })
  const identification = entry.video
    ? `${entry.video.title}${entry.confidence ? ` · ${confidenceLabel(entry.confidence)}${entry.matchedBy ? `, matched by ${matchedByLabel(entry.matchedBy)}` : ''}` : ''}`
    : 'None'

  return <article className={`${styles.panel} ${styles.review} ${checked ? styles.reviewSelected : ''}`}>
    <header className={styles.reviewHeader}>
      <SelectionCheckbox ariaLabel={`Select ${entry.fileName}`} checked={checked} onChange={() => onChecked(!checked)} />
      <div className={styles.reviewTitle}>
        <h2>{entry.fileName}</h2>
        <div className={styles.badges}>
          <span className={`${styles.badge} ${styles.reasonBadge}`}>{reasonLabels[entry.reason]}</span>
          {entry.runtimeSeconds != null && <span className={styles.badge}>{formatDuration(Number(entry.runtimeSeconds))}</span>}
          {entry.quality && <span className={styles.badge}>{entry.quality}</span>}
          {entry.width && entry.height && <span className={styles.badge}>{entry.width}×{entry.height}</span>}
          {entry.videoCodec && <span className={styles.badge}>{codecLabel(entry.videoCodec)}</span>}
          <span className={styles.badge}>{formatBytes(entry.sizeBytes)}</span>
          {!entry.isOnDisk && <span className={`${styles.badge} ${styles.missingBadge}`}>File missing</span>}
        </div>
      </div>
    </header>

    <p className={styles.reasonDescription}>{reasonDescriptions[entry.reason]}</p>
    <dl className={styles.evidence}>
      <Evidence label="Download">{entry.download.name}</Evidence>
      <Evidence label="Release">{entry.release}</Evidence>
      <Evidence label="Indexer">{entry.indexer}</Evidence>
      <Evidence label="Identification">{identification}</Evidence>
      <Evidence label="File path" wide><span className={styles.pathValue}><code>{entry.path}</code><CopyButton value={entry.path} /></span></Evidence>
      {entry.filedFile && <Evidence label="Filed copy" wide><span className={styles.pathValue}><code>{entry.filedFile.path}</code><span>{entry.filedFile.quality} · {formatBytes(entry.filedFile.sizeBytes)}</span><CopyButton value={entry.filedFile.path} /></span></Evidence>}
      {entry.probeError && <Evidence label="Probe" wide error>{entry.probeError}</Evidence>}
    </dl>

    {entry.actingAction === 'FileAs' && <Picker idPrefix={entry.id} candidates={entry.candidates} disabled={act.isPending} onPick={(video) => act.mutate(video)} />}
    {entry.actingAction && entry.actingAction !== 'FileAs' && <button className={styles.button} disabled={act.isPending} onClick={() => setConfirmation(entry.actingAction as 'Replace' | 'FileAsOnlyCopy')}>
      {entry.actingAction === 'Replace' ? 'Replace filed copy…' : 'File as only copy…'}
    </button>}

    {confirmation === 'Replace' && entry.filedFile && <ConfirmationDialog
      title="Replace the filed copy?"
      confirmLabel={act.isPending ? 'Replacing…' : 'Replace filed copy'}
      busy={act.isPending}
      danger
      onCancel={() => setConfirmation(null)}
      onConfirm={() => act.mutate(undefined)}
    >
      <p>The arriving file will replace the Library copy. The operation will be verified and recorded.</p>
      <dl className={styles.dialogDefinition}>
        <dt>Filed copy</dt><dd><code>{entry.filedFile.path}</code><span>{entry.filedFile.quality} · {formatBytes(entry.filedFile.sizeBytes)}</span></dd>
        <dt>Arriving file</dt><dd><code>{entry.path}</code><span>{entry.quality ?? 'Unknown quality'} · {formatBytes(entry.sizeBytes)}</span></dd>
      </dl>
    </ConfirmationDialog>}
    {confirmation === 'FileAsOnlyCopy' && <ConfirmationDialog
      title="File this as the only copy?"
      confirmLabel={act.isPending ? 'Filing…' : 'File as only copy'}
      busy={act.isPending}
      onCancel={() => setConfirmation(null)}
      onConfirm={() => act.mutate(undefined)}
    >
      <p>The recorded Library directory is missing. This will correct the record and file the arriving file as the only copy.</p>
      <code className={styles.dialogPath}>{entry.path}</code>
    </ConfirmationDialog>}
  </article>
}

function Evidence({ label, wide = false, error = false, children }: { label: string; wide?: boolean; error?: boolean; children: ReactNode }) {
  return <div className={`${styles.evidenceItem} ${wide ? styles.evidenceWide : ''} ${error ? styles.error : ''}`}>
    <dt>{label}</dt>
    <dd>{children}</dd>
  </div>
}

function Picker({ idPrefix, candidates, disabled, onPick }: { idPrefix: string; candidates: ReviewVideo[]; disabled: boolean; onPick: (video: ReviewVideo) => void }) {
  const generatedId = useId().replaceAll(':', '')
  const inputId = `review-video-search-${idPrefix}`
  const listboxId = `review-video-results-${generatedId}`
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [activeIndex, setActiveIndex] = useState(-1)

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setQuery(search.trim())
      setPage(1)
    }, 350)
    return () => window.clearTimeout(timer)
  }, [search])

  const live = query.length >= 2
  const results = useQuery({
    queryKey: ['review-video-search', query, page],
    queryFn: ({ signal }) => searchReviewVideos(query, undefined, page, signal),
    enabled: live,
  })
  const waitingForQuery = search.trim() !== query
  const videos = waitingForQuery || (search.trim().length > 0 && search.trim().length < 2)
    ? []
    : live ? results.data?.videos ?? [] : candidates
  const total = live ? Number(results.data?.total ?? 0) : candidates.length
  const pageSize = live ? Number(results.data?.pageSize ?? 20) : Math.max(1, candidates.length)
  const resultStart = total === 0 ? 0 : (page - 1) * pageSize + 1
  const resultEnd = Math.min(page * pageSize, total)

  useEffect(() => setActiveIndex(-1), [query, page])

  const moveActive = (direction: 1 | -1) => {
    if (videos.length === 0) return
    setActiveIndex((held) => {
      const next = held < 0 ? (direction > 0 ? 0 : videos.length - 1) : (held + direction + videos.length) % videos.length
      window.requestAnimationFrame(() => document.getElementById(`${listboxId}-${next}`)?.scrollIntoView({ block: 'nearest' }))
      return next
    })
  }

  return <section className={styles.picker} aria-label="Choose a prdb video">
    <div className={styles.pickerHeading}>
      <div>
        <label htmlFor={inputId}>File as a prdb video</label>
        <span>Search live by title or performer. Choose one result to resume the normal filing checks.</span>
      </div>
      {total > 0 && <span className={styles.quiet}>{live ? `${resultStart}–${resultEnd} of ${total}` : `${total} suggested`}</span>}
    </div>
    <input
      id={inputId}
      name={`video-search-${idPrefix}`}
      type="search"
      autoComplete="off"
      className={`${styles.field} ${styles.videoSearch}`}
      role="combobox"
      aria-autocomplete="list"
      aria-controls={listboxId}
      aria-expanded={videos.length > 0}
      aria-activedescendant={activeIndex >= 0 ? `${listboxId}-${activeIndex}` : undefined}
      placeholder="Search prdb videos"
      value={search}
      onChange={(event) => setSearch(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === 'ArrowDown') { event.preventDefault(); moveActive(1) }
        else if (event.key === 'ArrowUp') { event.preventDefault(); moveActive(-1) }
        else if (event.key === 'Enter' && activeIndex >= 0 && videos[activeIndex]) { event.preventDefault(); onPick(videos[activeIndex]) }
        else if (event.key === 'Escape') { setSearch(''); setQuery(''); setPage(1) }
      }}
    />

    <div className={styles.pickerStatus} role="status" aria-live="polite">
      {waitingForQuery ? 'Waiting to search…' : search.trim().length === 1 ? 'Type one more character to search.' : results.isFetching ? 'Searching prdb…' : results.isError ? 'prdb could not be searched. Try again.' : live && total === 0 ? 'No videos matched this search.' : !live && candidates.length === 0 ? 'No suggested matches. Search prdb to choose the video.' : ''}
    </div>

    {videos.length > 0 && <div className={styles.pickerResults} id={listboxId} role="listbox" aria-label="prdb video results">
      {videos.map((video, index) => <button
        className={`${styles.candidate} ${index === activeIndex ? styles.candidateActive : ''}`}
        disabled={disabled}
        id={`${listboxId}-${index}`}
        key={video.id}
        onClick={() => onPick(video)}
        onMouseMove={() => setActiveIndex(index)}
        role="option"
        aria-selected={index === activeIndex}
        tabIndex={-1}
        type="button"
      >
        {video.artworkId != null
          ? <Artwork videoId={video.artworkId} title={video.title} frameClassName={styles.candidateArtwork} imageClassName={styles.candidateImage} absentClassName={styles.candidateArtworkAbsent} />
          : <span className={styles.candidateArtwork}><span className={styles.candidateArtworkAbsent} aria-hidden="true">▤</span></span>}
        <span className={styles.candidateBody}>
          <strong>{video.title}</strong>
          <span>{[video.site, video.releaseDate ? formatDate(video.releaseDate) : null, video.consensusRuntimeMs != null ? `${formatDuration(Math.round(Number(video.consensusRuntimeMs) / 1000))} consensus` : null].filter(Boolean).join(' · ') || 'No additional details'}</span>
        </span>
        <span className={styles.candidateAction}>File as this video</span>
      </button>)}
    </div>}

    {live && total > pageSize && <div className={styles.pickerPager}>
      <button className={styles.button} disabled={page <= 1 || results.isFetching} onClick={() => setPage((held) => held - 1)}>Previous results</button>
      <span>Page {page} of {Math.ceil(total / pageSize)}</span>
      <button className={styles.button} disabled={page * pageSize >= total || results.isFetching} onClick={() => setPage((held) => held + 1)}>Next results</button>
    </div>}
  </section>
}

function QueuePager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (page: number) => void }) {
  const start = (page - 1) * pageSize + 1
  const end = Math.min(page * pageSize, total)
  return <nav className={styles.pager} aria-label="Review Queue pages">
    <button className={styles.button} disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button>
    <span>{start}–{end} of {total}</span>
    <button className={styles.button} disabled={end >= total} onClick={() => onPage(page + 1)}>Next</button>
  </nav>
}

function SelectionCheckbox({ ariaLabel, checked, disabled = false, indeterminate = false, onChange }: { ariaLabel: string; checked: boolean; disabled?: boolean; indeterminate?: boolean; onChange: () => void }) {
  const input = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (input.current) input.current.indeterminate = indeterminate
  }, [indeterminate])
  return <input ref={input} className={styles.checkbox} aria-label={ariaLabel} type="checkbox" checked={checked} disabled={disabled} onChange={onChange} />
}

function CopyButton({ value }: { value: string }) {
  const [label, setLabel] = useState('Copy')
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setLabel('Copied')
      window.setTimeout(() => setLabel('Copy'), 1500)
    } catch {
      setLabel('Copy failed')
    }
  }
  return <button type="button" className={styles.copyButton} onClick={() => void copy()}>{label}</button>
}

function ConfirmationDialog({ title, confirmLabel, busy = false, danger = false, onCancel, onConfirm, children }: { title: string; confirmLabel: string; busy?: boolean; danger?: boolean; onCancel: () => void; onConfirm: () => void; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => {
    dialog.current?.showModal()
  }, [])
  return <dialog
    ref={dialog}
    className={styles.dialog}
    aria-labelledby={titleId}
    onCancel={(event) => { event.preventDefault(); if (!busy) onCancel() }}
    onClick={(event) => { if (event.currentTarget === event.target && !busy) onCancel() }}
  >
    <h2 id={titleId}>{title}</h2>
    <div className={styles.dialogBody}>{children}</div>
    <div className={styles.dialogActions}>
      <button type="button" className={styles.button} disabled={busy} onClick={onCancel}>Cancel</button>
      <button type="button" className={`${styles.button} ${danger ? styles.dangerButton : styles.primaryButton}`} disabled={busy} onClick={onConfirm}>{confirmLabel}</button>
    </div>
  </dialog>
}

function formatBytes(value: number | string) {
  const bytes = Number(value)
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(bytes >= 10 * 1024 ** 3 ? 1 : 2)} GiB`
  return `${(bytes / 1024 ** 2).toFixed(1)} MiB`
}

function formatDuration(seconds: number) {
  const whole = Math.max(0, Math.round(seconds))
  const hours = Math.floor(whole / 3600)
  const minutes = Math.floor((whole % 3600) / 60)
  const remainder = whole % 60
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}` : `${minutes}:${String(remainder).padStart(2, '0')}`
}

function codecLabel(codec: string) {
  const labels: Record<string, string> = { h264: 'H.264', hevc: 'HEVC', h265: 'HEVC', av1: 'AV1', vp9: 'VP9' }
  return labels[codec.toLowerCase()] ?? codec.toUpperCase()
}

function confidenceLabel(confidence: string) {
  return confidence.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase().replace(/^./, (letter) => letter.toUpperCase())
}

function matchedByLabel(matchedBy: string) {
  return matchedBy.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
}

function formatDate(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : String(error)
}
