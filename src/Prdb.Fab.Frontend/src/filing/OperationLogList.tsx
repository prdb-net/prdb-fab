import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'

import type { OperationLogPage } from '../api/client.ts'
import styles from './Filing.module.css'

type Operation = OperationLogPage['entries'][number]

const time = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' })
const exactTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'long', timeStyle: 'medium' })
const date = new Intl.DateTimeFormat(undefined, { dateStyle: 'long' })

export function OperationList({ entries }: { entries: OperationLogPage['entries'] }) {
  if (entries.length === 0) return <p className={styles.empty}>No operation has been recorded.</p>

  return (
    <div className={styles.operationGroups}>
      {groupByDay(entries).map((group) => (
        <section aria-labelledby={`operations-${group.key}`} className={styles.operationGroup} key={group.key}>
          <h2 className={styles.operationDay} id={`operations-${group.key}`}>{group.label}</h2>
          <div className={styles.operationList}>
            {group.entries.map((entry) => <OperationCard entry={entry} key={entry.id} />)}
          </div>
        </section>
      ))}
    </div>
  )
}

function OperationCard({ entry }: { entry: Operation }) {
  const happenedAt = new Date(entry.at)
  const subject = operationSubject(entry)
  const leftovers = leftoverPaths(entry)

  return (
    <article className={`${styles.operationCard} ${actClass(entry.act)}`}>
      <div aria-hidden="true" className={styles.operationIcon}><ActIcon act={entry.act} /></div>
      <div className={styles.operationBody}>
        <div className={styles.operationTitleLine}>
          <ActBadge act={entry.act} />
          <OperationSubject entry={entry}>{subject}</OperationSubject>
        </div>
        <p className={styles.operationDescription}>{operationDescription(entry, leftovers.length)}</p>
        <div className={styles.operationMeta}>
          <span title="Actor">{entry.actor}</span>
          <span className={styles.operationReason} title="Reason">{entry.reason}</span>
          {entry.origin && !(entry.actor === 'Person' && entry.origin.kind === 'Person') && <OriginLink compact entry={entry} />}
        </div>
      </div>
      <time className={styles.operationTime} dateTime={entry.at} title={exactTime.format(happenedAt)}>
        {time.format(happenedAt)}
      </time>
      <details className={styles.operationDetails}>
        <summary><span className={styles.operationDetailsClosed}>View details</span><span className={styles.operationDetailsOpen}>Hide details</span></summary>
        <dl className={styles.operationDefinition}>
          <OperationPaths entry={entry} leftovers={leftovers} />
          <dt>Actor</dt><dd>{entry.actor}</dd>
          <dt>Reason</dt><dd>{entry.reason}</dd>
          {entry.origin && <><dt>Origin</dt><dd><OriginLink entry={entry} /></dd></>}
          <dt>Time</dt><dd><time dateTime={entry.at}>{exactTime.format(happenedAt)}</time></dd>
        </dl>
      </details>
    </article>
  )
}

function OperationSubject({ entry, children }: { entry: Operation; children: ReactNode }) {
  const hasLibraryEntry = entry.videoId && ['Filed', 'Relabelled', 'Replaced'].includes(entry.act)
  return <h3 className={styles.operationSubject}>{hasLibraryEntry
    ? <Link to={`/library/${entry.videoId}`}>{children}</Link>
    : children}</h3>
}

function OperationPaths({ entry, leftovers }: { entry: Operation; leftovers: string[] }) {
  if (entry.act === 'Tidied') {
    return <>
      <PathDefinition label="Directory" path={entry.pathBefore} />
      <dt>Removed leftovers</dt>
      <dd>{leftovers.length > 0
        ? <ul className={styles.operationPathList}>{leftovers.map((path) => <li key={path}><PathValue path={path} /></li>)}</ul>
        : 'None recorded'}</dd>
    </>
  }

  if (entry.act === 'Replaced') {
    return <>
      <PathDefinition label="Incoming file" path={entry.pathBefore} />
      <PathDefinition label="Filed as" path={entry.pathAfter} />
      <PathDefinition label="Replaced file" path={entry.displacedPath} />
    </>
  }

  if (entry.act === 'Deleted') return <PathDefinition label="Deleted file" path={entry.pathBefore ?? entry.pathAfter} />

  if (entry.act === 'Relabelled') {
    return <>
      <PathDefinition label="Previous path" path={entry.pathBefore} />
      <PathDefinition label="New path" path={entry.pathAfter} />
    </>
  }

  return <>
    <PathDefinition label="From" path={entry.pathBefore} />
    <PathDefinition label="To" path={entry.pathAfter} />
  </>
}

function PathDefinition({ label, path }: { label: string; path: string | null }) {
  return <><dt>{label}</dt><dd>{path ? <PathValue path={path} /> : 'Not recorded'}</dd></>
}

function PathValue({ path }: { path: string }) {
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle')

  const copy = async () => {
    if (await copyText(path)) {
      setCopyState('copied')
    } else {
      setCopyState('failed')
    }
    window.setTimeout(() => setCopyState('idle'), 1600)
  }
  const buttonLabel = copyState === 'copied' ? 'Copied' : copyState === 'failed' ? 'Copy failed' : 'Copy'
  const accessibleLabel = copyState === 'copied' ? `Copied ${path}` : copyState === 'failed' ? `Could not copy ${path}` : `Copy ${path}`

  return <span className={styles.operationPath}>
    <code>{path}</code>
    <button aria-label={accessibleLabel} aria-live="polite" className={styles.copyButton} onClick={() => void copy()} type="button">{buttonLabel}</button>
  </span>
}

async function copyText(value: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(value)
    return true
  } catch {
    const input = document.createElement('textarea')
    try {
      input.value = value
      input.style.position = 'fixed'
      input.style.opacity = '0'
      document.body.append(input)
      input.select()
      return document.execCommand('copy')
    } catch {
      return false
    } finally {
      input.remove()
    }
  }
}

function OriginLink({ compact = false, entry }: { compact?: boolean; entry: Operation }) {
  if (!entry.origin) return null
  const label = compact ? entry.origin.kind : originLabel(entry.origin)
  return entry.downloadId
    ? <Link className={styles.operationOrigin} title={compact ? originLabel(entry.origin) : undefined} to={`/downloads?download=${entry.downloadId}`}>{label}</Link>
    : <span className={styles.operationOrigin}>{label}</span>
}

function ActBadge({ act }: { act: string }) {
  return <span className={styles.operationAct}>{act}</span>
}

function ActIcon({ act }: { act: string }) {
  if (act === 'Deleted') return <svg viewBox="0 0 24 24"><path d="M5 7h14M9 7V4h6v3m-8 0 1 13h8l1-13M10 11v5m4-5v5" /></svg>
  if (act === 'Replaced') return <svg viewBox="0 0 24 24"><path d="m7 7-3 3 3 3m-3-3h12a4 4 0 0 1 4 4m-3 3 3-3-3-3m3 3H8a4 4 0 0 1-4-4" /></svg>
  if (act === 'Relabelled') return <svg viewBox="0 0 24 24"><path d="M4 5h10l6 6-9 9-7-7V5Zm5 4h.01" /></svg>
  if (act === 'Tidied') return <svg viewBox="0 0 24 24"><path d="m5 14 5-9 4 2-5 9m-4-2 4 2m1-1 8 4m-9-2-2 3m5-4-2 4m5-3-1 3" /></svg>
  return <svg viewBox="0 0 24 24"><path d="M5 12.5 9.5 17 19 7.5" /></svg>
}

function operationSubject(entry: Operation): string {
  const candidate = entry.pathAfter ?? entry.pathBefore ?? entry.displacedPath
  if (!candidate) return entry.act
  const trimmed = candidate.replace(/[\\/]+$/, '')
  return trimmed.split(/[\\/]/).at(-1) || trimmed
}

function operationDescription(entry: Operation, leftoverCount: number): string {
  if (entry.act === 'Deleted') return 'Deleted from the Review Queue'
  if (entry.act === 'Replaced') return 'Replaced the held Video File'
  if (entry.act === 'Relabelled') return 'Added its Quality label'
  if (entry.act === 'Tidied') return `${leftoverCount} leftover ${leftoverCount === 1 ? 'file' : 'files'} removed`
  if (entry.act === 'Filed') return 'Moved into the Library'
  return 'Changed content'
}

function leftoverPaths(entry: Operation): string[] {
  if (!entry.leftoverNamesJson) return []
  try {
    const paths: unknown = JSON.parse(entry.leftoverNamesJson)
    return Array.isArray(paths) ? paths.filter((path): path is string => typeof path === 'string') : []
  } catch {
    return []
  }
}

function originLabel(origin: NonNullable<Operation['origin']>): string {
  if (origin.kind === 'Person') return 'Person'
  const rules = origin.rules.map((rule) => rule.name).join(', ')
  return rules ? `Automation — ${rules}` : 'Automation'
}

function actClass(act: string): string {
  if (act === 'Deleted') return styles.operationDeleted
  if (act === 'Replaced') return styles.operationReplaced
  if (act === 'Relabelled') return styles.operationRelabelled
  if (act === 'Tidied') return styles.operationTidied
  return styles.operationFiled
}

function groupByDay(entries: OperationLogPage['entries']) {
  const groups: Array<{ key: string; label: string; entries: OperationLogPage['entries'] }> = []
  for (const entry of entries) {
    const happenedAt = new Date(entry.at)
    const key = localDateKey(happenedAt)
    const previous = groups.at(-1)
    if (previous?.key === key) previous.entries.push(entry)
    else groups.push({ key, label: dayLabel(happenedAt), entries: [entry] })
  }
  return groups
}

function localDateKey(value: Date): string {
  return [value.getFullYear(), String(value.getMonth() + 1).padStart(2, '0'), String(value.getDate()).padStart(2, '0')].join('-')
}

function dayLabel(value: Date): string {
  const today = new Date()
  const yesterday = new Date(today)
  yesterday.setDate(today.getDate() - 1)
  if (localDateKey(value) === localDateKey(today)) return 'Today'
  if (localDateKey(value) === localDateKey(yesterday)) return 'Yesterday'
  return date.format(value)
}
