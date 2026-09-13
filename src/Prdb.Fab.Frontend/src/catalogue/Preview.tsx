import { useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readVideoPreview, type VideoCard, type VideoPreview } from '../api/client.ts'
import { CardActions } from './CardActions.tsx'
import { Artwork } from './Grid.tsx'
import styles from './Preview.module.css'

/**
 * One Video opened over the grid it was found in (ADR 0060).
 *
 * The list underneath is not unmounted, not re-read and not re-paged: this
 * renders beside the grid in the same screen rather than as a route that
 * replaces it, which is the whole of why a Preview is cheap to open and cheap
 * to leave. Four ways out, all of them one gesture — the scrim, `Esc`, the
 * close control, and the browser's back button, which works because the sheet
 * is an address.
 *
 * On a wide window it sits against the right edge and leaves a column of real
 * cards showing, so the way to the next Video is to click one of them; a
 * centred dialog would leave the same area of screen and none of it usable. On
 * a phone it is the screen.
 */
export function Preview({
  prdbId,
  card,
  returnTo,
  onClose,
}: {
  prdbId: string
  /**
   * The card the grid already drew, where the grid has it. The sheet opens on
   * this and never on nothing, so what a person sees when they click is what
   * they clicked rather than a spinner.
   */
  card?: VideoCard
  returnTo: string
  onClose: () => void
}) {
  const preview = useQuery({
    queryKey: ['catalogue-preview', prdbId],
    queryFn: () => readVideoPreview(prdbId),
    // Stepping back and forth across a page of cards re-reads nothing.
    staleTime: 5 * 60 * 1000,
  })
  const sheet = useRef<HTMLDivElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const titleId = useId()

  useEffect(() => {
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    closeButton.current?.focus()

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
        return
      }

      if (event.key !== 'Tab') return

      const focusable = sheet.current?.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled])',
      )
      const first = focusable?.item(0)
      const last = focusable?.item((focusable?.length ?? 1) - 1)

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last?.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first?.focus()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', onKey)
    }
  }, [onClose])

  const video = preview.data?.video ?? card
  const title = video?.title ?? 'Video'

  return (
    <div
      className={styles.backdrop}
      onMouseDown={(event) => {
        if (event.currentTarget === event.target) onClose()
      }}
    >
      <div
        aria-labelledby={titleId}
        aria-modal="true"
        className={styles.sheet}
        ref={sheet}
        role="dialog"
      >
        <Dismissable onDismiss={onClose} />

        <header className={styles.header}>
          <div className={styles.heading}>
            <h2 className={styles.title} id={titleId}>{title}</h2>
            {video && <p className={styles.facts}>{facts(video, preview.data)}</p>}
          </div>
          <button
            aria-label="Close preview"
            className={styles.close}
            onClick={onClose}
            ref={closeButton}
            title="Close preview"
            type="button"
          >
            <CloseIcon />
          </button>
        </header>

        <div className={styles.body}>
          {video && (
            <Artwork
              videoId={video.id}
              title={title}
              frameClassName={styles.frame}
              imageClassName={styles.image}
              absentClassName={styles.absent}
            />
          )}

          {video && (
            <div className={styles.actions}>
              <CardActions includeSite video={video} returnTo={returnTo} />
            </div>
          )}

          <People preview={preview.data} video={video} />

          {preview.isError && (
            <p className={styles.problem} role="alert">
              The rest of this Video could not be read. What is above came from the
              grid; everything else is on the Release page.
            </p>
          )}
        </div>
      </div>
    </div>
  )
}

/** The Site and the Actors, each a way further into the catalogue. */
function People({ preview, video }: { preview?: VideoPreview; video?: VideoCard }) {
  const actors = preview?.actors ?? []

  if (!video?.sitePrdbId && actors.length === 0) return null

  return (
    <dl className={styles.people}>
      {video?.sitePrdbId && (
        <>
          <dt>Site</dt>
          <dd>
            <Link to={`/sites/${video.sitePrdbId}`}>{video.site ?? 'This Site'}</Link>
          </dd>
        </>
      )}
      {actors.length > 0 && (
        <>
          <dt>{actors.length === 1 ? 'Actor' : 'Actors'}</dt>
          <dd className={styles.credits}>
            {actors.map((actor) => (
              <Link key={actor.prdbId} to={`/actors/${actor.prdbId}`}>{actor.name}</Link>
            ))}
          </dd>
        </>
      )}
    </dl>
  )
}

/**
 * The downward drag a phone expects, on the handle rather than the whole sheet
 * so that a scrolled gallery is still scrollable.
 */
function Dismissable({ onDismiss }: { onDismiss: () => void }) {
  const [pulled, setPulled] = useState<number | null>(null)

  return (
    <div
      aria-hidden="true"
      className={styles.handle}
      onPointerDown={(event) => {
        event.currentTarget.setPointerCapture(event.pointerId)
        setPulled(event.clientY)
      }}
      onPointerMove={(event) => {
        if (pulled !== null && event.clientY - pulled > 80) {
          setPulled(null)
          onDismiss()
        }
      }}
      onPointerUp={() => setPulled(null)}
    >
      <span className={styles.grip} />
    </div>
  )
}

/**
 * The line the card shows, plus the Consensus Runtime it has no room for.
 * ADR 0031 puts that number beside a file's own and lets it decide nothing, so
 * it reads as a fact rather than as a promise about what will be downloaded.
 */
function facts(video: VideoCard, preview?: VideoPreview): string {
  const held = video.heldQualities?.length
    ? `In Library · ${video.heldQualities.join(', ')}`
    : null
  const downloading = video.activeDownloadId
    ? video.activeDownloadState === 'Completed' ? 'Processing' : 'Downloading'
    : null

  return [
    video.site,
    video.releaseDate,
    runtime(preview),
    downloading,
    held,
    video.wanted ? 'Wanted' : null,
  ]
    .filter(Boolean)
    .join(' · ')
}

function runtime(preview?: VideoPreview): string | null {
  if (preview?.durationMs == null) return null

  const minutes = Math.round(Number(preview.durationMs) / 60_000)
  const spread = preview.durationSpreadMs == null || preview.durationFileCount == null
    ? ''
    : ` (±${Math.max(1, Math.round(Number(preview.durationSpreadMs) / 60_000))} min over ${preview.durationFileCount} files)`

  return minutes >= 60
    ? `${Math.floor(minutes / 60)}h ${minutes % 60}m${spread}`
    : `${minutes} min${spread}`
}

function CloseIcon() {
  return (
    <svg aria-hidden="true" className={styles.icon} viewBox="0 0 24 24">
      <path
        d="m6 6 12 12M18 6 6 18"
        fill="none"
        stroke="currentColor"
        strokeLinecap="round"
        strokeWidth="1.8"
      />
    </svg>
  )
}
