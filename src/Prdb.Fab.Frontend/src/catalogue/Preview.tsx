import { useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import {
  readVideoPreview,
  type PreviewImage,
  type VideoCard,
  type VideoPreview,
} from '../api/client.ts'
import { CardActions } from './CardActions.tsx'
import { CachedArtwork } from './Grid.tsx'
import { usePreview } from './preview.ts'
import styles from './Preview.module.css'

/**
 * The sheet, over whichever grid the screen is showing, and the two keys that
 * walk that grid without closing it.
 *
 * Every browse surface renders this once and passes `onOpen` to its `Grid`;
 * ADR 0012 keeps the card the same across the five, and this keeps the way into
 * a Video the same with it.
 */
export function PreviewOverlay({
  videos,
  returnTo,
}: {
  videos: readonly VideoCard[]
  returnTo: string
}) {
  const { open, openPreview, closePreview } = usePreview()

  useEffect(() => {
    if (!open) return

    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return

      const at = videos.findIndex((video) => video.prdbId === open)
      const next = at + (event.key === 'ArrowRight' ? 1 : -1)

      // The ends of the loaded page are the ends. A keypress that fetched the
      // next page and changed the list under the sheet would be a different act
      // from stepping, and it would make the last card of a page unpredictable.
      if (at < 0 || next < 0 || next >= videos.length) return

      event.preventDefault()
      openPreview(videos[next].prdbId)

      // The grid underneath follows, so closing lands in front of whatever was
      // last looked at rather than back at the top.
      requestAnimationFrame(() =>
        document
          .querySelector(`[data-video="${videos[next].prdbId}"]`)
          ?.scrollIntoView({ block: 'nearest' }),
      )
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, openPreview, videos])

  if (!open) return null

  return (
    <Preview
      card={videos.find((video) => video.prdbId === open)}
      onClose={closePreview}
      prdbId={open}
      returnTo={returnTo}
    />
  )
}

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
function Preview({
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
          <Gallery images={preview.data?.images} key={prdbId} title={title} video={video} />

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

/**
 * Every picture prdb publishes for the Video, oldest first — its own order,
 * which it documents as stable and expressly not a ranking, so the strip shows
 * it as given and says nothing about which picture is best.
 *
 * **A tile is fetched when it is scrolled into view, never when the sheet
 * opens.** A Video here carries about ten pictures where it carries any, and a
 * Preview that a person opens and steps straight past must have cost one
 * picture rather than ten (ADR 0060). That is what `CachedArtwork` already
 * does for a grid tile, so this reuses it rather than writing a second image
 * loader: one observer, one fetch, an object URL revoked on unmount, and the
 * no-artwork tile on an empty answer, a failed fetch and a broken image alike.
 */
function Gallery({
  images,
  title,
  video,
}: {
  images?: readonly PreviewImage[]
  title: string
  video?: VideoCard
}) {
  const [shown, setShown] = useState(0)
  const [pulled, setPulled] = useState<number | null>(null)
  const dwelt = useDwell()
  const gallery = useRef<HTMLDivElement>(null)

  if (images?.length === 0 || (!images && !video)) {
    return (
      <span aria-label={`No pictures for ${title}`} className={styles.frame} role="img">
        <span aria-hidden="true" className={styles.absent}>▤</span>
      </span>
    )
  }

  const at = images ? Math.min(shown, images.length - 1) : 0
  const step = (by: number) => {
    if (!images) return

    const next = (at + by + images.length) % images.length
    setShown(next)

    // The ring follows the picture. Leaving focus on the thumbnail that was
    // selected a moment ago would put the keyboard somewhere other than where
    // the eye is, and the next press would step from there.
    requestAnimationFrame(() =>
      gallery.current
        ?.querySelectorAll<HTMLElement>('[aria-label^="Picture "]')
        .item(next)
        ?.focus(),
    )
  }

  // Until the read lands there is one picture worth showing and the grid has
  // already shown it: the Video's own, at the address every grid tile uses. The
  // chosen picture then resolves to that same address, so the picture does not
  // change hands when the read arrives and nothing is fetched twice.
  const path = images ? pathOf(images[at], video) : `/api/artwork/${video!.id}`

  return (
    <div
      className={styles.gallery}
      ref={gallery}
      onKeyDown={(event) => {
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return

        // The same two keys walk the grid underneath (ADR 0060). Which one they
        // mean is decided by what has focus, so a gallery being used swallows
        // them rather than moving the sheet to another Video.
        event.stopPropagation()
        event.preventDefault()
        step(event.key === 'ArrowRight' ? 1 : -1)
      }}
    >
      <div
        className={styles.stage}
        onPointerDown={(event) => setPulled(event.clientX)}
        onPointerUp={(event) => {
          if (pulled !== null && Math.abs(event.clientX - pulled) > 50) {
            step(event.clientX < pulled ? 1 : -1)
          }
          setPulled(null)
        }}
      >
        <CachedArtwork
          absentClassName={styles.absent}
          frameClassName={styles.frame}
          imageClassName={styles.image}
          key={path}
          path={path}
          title={images ? `${title} — picture ${at + 1} of ${images.length}` : title}
        />
      </div>

      {images && images.length > 1 && dwelt && (
        <ul className={styles.strip}>
          {images.map((image, index) => (
            <li key={image.prdbId}>
              <button
                aria-current={index === at}
                aria-label={`Picture ${index + 1} of ${images.length}`}
                className={`${styles.thumb} ${index === at ? styles.thumbShown : ''}`}
                onClick={() => setShown(index)}
                type="button"
              >
                <CachedArtwork
                  absentClassName={styles.thumbAbsent}
                  frameClassName={styles.thumbFrame}
                  imageClassName={styles.thumbImage}
                  path={pathOf(image, video)}
                  title=""
                />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/**
 * Whether the sheet has been showing this Video long enough to be worth
 * fetching a strip of thumbnails for.
 *
 * ADR 0060 wants a Preview that is opened and stepped straight past to have
 * cost one picture rather than ten, and on a wide window the strip is in view
 * the moment the sheet opens — so *scrolled into view* is not on its own the
 * bound the ADR describes. Walking a page of twenty-four cards with the arrow
 * keys must not be two hundred fetches, and a quarter of a second is longer
 * than a keypress and shorter than a look.
 *
 * The Gallery is keyed by the Video, so stepping remounts it and the wait
 * starts again.
 */
function useDwell(after = 250): boolean {
  const [dwelt, setDwelt] = useState(false)

  useEffect(() => {
    const waiting = setTimeout(() => setDwelt(true), after)
    return () => clearTimeout(waiting)
  }, [after])

  return dwelt
}

/**
 * Where a picture's bytes are asked for.
 *
 * The chosen one is asked for by Video, which is the address the grid tile
 * already used and the one the browser therefore already holds — ADR 0030 lets
 * it keep an image for a year. Asking for the same bytes by image id would be a
 * second fetch of a picture that is on the screen behind the sheet.
 */
function pathOf(image: PreviewImage, video?: VideoCard): string {
  return image.chosen && video
    ? `/api/artwork/${video.id}`
    : `/api/artwork/images/${image.prdbId}`
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
