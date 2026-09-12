import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import type { RestoreVerdict } from '../api/client.ts'
import { restoreBackup } from '../api/client.ts'
import { accessStateKey } from './state.ts'
import styles from './Access.module.css'

/**
 * ADR 0010's other entry point: restore a backup instead of setting up fresh.
 *
 * Reachable without being signed in, because the login credential is inside the
 * file and a fresh container has nobody to sign in as. What keeps that narrow is
 * the condition on the other side — a password here closes it for good, and a
 * restore refuses an installation that holds anything.
 *
 * Two steps, one endpoint. The file goes up, the tool says what is in it and
 * where its paths were recorded, and the second call carries where those roots
 * are in *this* container. ADR 0009 asks for them once, prefilled, rather than
 * reusing what the file remembers: the machine reading it may mount its library
 * somewhere else entirely.
 */
export function RestoreScreen({ onBack }: { onBack: () => void }) {
  const queries = useQueryClient()
  const [document, setDocument] = useState<string | null>(null)
  const [fileName, setFileName] = useState<string | null>(null)
  const [verdict, setVerdict] = useState<RestoreVerdict | null>(null)
  const [library, setLibrary] = useState('')
  const [downloads, setDownloads] = useState('')
  const [unreadable, setUnreadable] = useState<string | null>(null)

  const summary = verdict?.summary ?? null
  const asking = verdict !== null && summary !== null && verdict.outcome !== 'Restored'

  const send = useMutation({
    mutationFn: (roots?: { library: string | null; downloads: string | null }) =>
      restoreBackup(document ?? '', roots),
    onSuccess: async (answer) => {
      setVerdict(answer)

      if (answer.outcome === 'RootsNeeded' && answer.summary) {
        // Prefilled from the file, which is what makes this one question rather
        // than a path somebody has to remember.
        setLibrary(answer.summary.recordedLibraryRoot ?? '')
        setDownloads(answer.summary.recordedDownloadDirectory ?? '')
      }

      if (answer.outcome === 'Restored') {
        // The installation now has the password that was in the file, so the
        // one page decides again and lands on the sign-in screen.
        await queries.invalidateQueries({ queryKey: accessStateKey })
      }
    },
  })

  async function take(file: File) {
    setUnreadable(null)
    setVerdict(null)

    try {
      const text = await file.text()

      setDocument(text)
      setFileName(file.name)
    } catch (error) {
      setUnreadable(String(error))
    }
  }

  if (verdict?.outcome === 'Restored') {
    return (
      <main className={styles.screen}>
        <h1>prdb-fab</h1>
        <p className={styles.lede}>{verdict.detail}</p>
      </main>
    )
  }

  return (
    <main className={styles.screen}>
      <h1>prdb-fab</h1>
      <p className={styles.lede}>
        Restore a backup into this installation. It has to be empty &mdash; a
        restore replaces an installation rather than joining one &mdash; and what
        comes back is every setting, every key and the record of what was
        downloaded and filed. Not the videos themselves, and not the caches: all
        of that is fetched again.
      </p>

      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault()

          if (verdict?.outcome === 'RootsNeeded' || asking) {
            send.mutate({
              library: library.trim() === '' ? null : library.trim(),
              downloads: downloads.trim() === '' ? null : downloads.trim(),
            })

            return
          }

          send.mutate(undefined)
        }}
      >
        <label className={styles.label} htmlFor="backup">
          The backup file
        </label>
        <input
          id="backup"
          className={styles.field}
          type="file"
          accept="application/json,.json"
          onChange={(event) => {
            const file = event.target.files?.[0]

            if (file) void take(file)
          }}
        />

        {summary && (
          <dl className={styles.summary}>
            <dt>Written by</dt>
            <dd>
              {summary.toolVersion} on{' '}
              {new Date(summary.writtenAt).toLocaleString()}
            </dd>
            <dt>Indexers</dt>
            <dd>{summary.indexers}</dd>
            <dt>Automation rules</dt>
            <dd>{summary.automationRules}</dd>
            <dt>Library entries</dt>
            <dd>
              {summary.libraryEntries} with {summary.videoFiles} file(s)
            </dd>
            <dt>Downloads</dt>
            <dd>
              {summary.downloads}, and {summary.arrivingFiles} arriving file(s)
            </dd>
          </dl>
        )}

        {asking && summary?.needsLibraryRoot && (
          <>
            <label className={styles.label} htmlFor="library">
              The library root, in this container
            </label>
            <input
              id="library"
              className={styles.field}
              type="text"
              value={library}
              onChange={(event) => setLibrary(event.target.value)}
            />
          </>
        )}

        {asking && summary?.needsDownloadDirectory && (
          <>
            <label className={styles.label} htmlFor="downloads">
              The download directory, in this container
            </label>
            <input
              id="downloads"
              className={styles.field}
              type="text"
              value={downloads}
              onChange={(event) => setDownloads(event.target.value)}
            />
          </>
        )}

        {unreadable && <p className={styles.refusal}>{unreadable}</p>}
        {send.isError && <p className={styles.refusal}>{String(send.error)}</p>}

        {verdict && verdict.outcome !== 'RootsNeeded' && (
          <p className={styles.refusal}>
            {verdict.detail}
            {verdict.writtenBy && <> It was written by {verdict.writtenBy}.</>}
          </p>
        )}

        {verdict && verdict.found.length > 0 && (
          <ul className={styles.found}>
            {verdict.found.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        )}

        <button
          className={styles.button}
          type="submit"
          disabled={document === null || send.isPending}
        >
          {asking ? 'Restore this backup' : 'Read the backup'}
        </button>
      </form>

      {fileName && !summary && (
        <p className={styles.note}>
          {fileName} is ready to be read. Nothing is written until the roots
          below have been answered.
        </p>
      )}

      <p className={styles.fork}>
        Nothing to restore?{' '}
        <button className={styles.forkButton} type="button" onClick={onBack}>
          Set this installation up fresh
        </button>{' '}
        instead.
      </p>
    </main>
  )
}
