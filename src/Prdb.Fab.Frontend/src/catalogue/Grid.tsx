import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router'

import type { LibraryPage, VideoCard } from '../api/client.ts'
import styles from './Grid.module.css'

type LibraryCard = LibraryPage['entries'][number]

/**
 * The grid, written once and used by every browse surface.
 *
 * ADR 0012 makes five surfaces artwork grids and says the differences between
 * them are the source and the actions, never the card. So the card is fixed
 * here and what may go under it is the caller's — which is the seam that keeps
 * *find this on the indexers* from having to be invented before the slice that
 * owns it, and keeps the wanted list's way out to prdb off every other surface.
 */
export function Grid({
  videos,
  action,
  onOpen,
}: {
  videos: readonly VideoCard[]
  /** What this surface offers on a card, if anything. */
  action?: (video: VideoCard) => ReactNode
  /**
   * What a click on the card itself does, if anything. ADR 0060 makes that a
   * Preview over this grid, which is why it is a callback rather than a
   * destination: nothing is navigated to, and the list stays where it was.
   */
  onOpen?: (video: VideoCard) => void
}) {
  return (
    <ul className={styles.grid}>
      {videos.map((video) => (
        <Card key={video.id} video={video} action={action} onOpen={onOpen} />
      ))}
    </ul>
  )
}

/** The held-only sibling of the Catalogue grids, using the same card grammar. */
export function LibraryGrid({
  entries,
  action,
}: {
  entries: readonly LibraryCard[]
  action?: (entry: LibraryCard) => ReactNode
}) {
  return (
    <ul className={styles.grid}>
      {entries.map((entry) => <HeldCard action={action?.(entry)} entry={entry} key={entry.id} />)}
    </ul>
  )
}

function Card({
  video,
  action,
  onOpen,
}: {
  video: VideoCard
  action?: (video: VideoCard) => ReactNode
  onOpen?: (video: VideoCard) => void
}) {
  const held = video.heldQualities?.length
    ? video.heldQualities.join(', ')
    : null

  const badge = video.activeDownloadId ? (
    <span className={styles.downloading}>
      {video.activeDownloadState === 'Completed' ? 'Processing' : 'Downloading'}
    </span>
  ) : held ? (
    <span className={styles.held}>In Library · {held}</span>
  ) : video.downloadReady ? (
    <span className={styles.ready}>Ready to download</span>
  ) : null

  return <GridCard
    artworkId={video.id}
    title={video.title}
    detail={describe(video)}
    status={statusOf(video).join(' · ')}
    badge={badge}
    action={action?.(video)}
    onOpen={onOpen && (() => onOpen(video))}
    prdbId={video.prdbId}
  />
}

function HeldCard({ entry, action }: { entry: LibraryCard; action?: ReactNode }) {
  const qualities = compactQualities(entry.qualities)
  return <GridCard
    artworkId={entry.artworkId}
    title={entry.title}
    detail={[entry.site, entry.releaseDate].filter(Boolean).join(' · ')}
    status={[describeCopies(entry.qualities), describeRuntime(entry.runtimeSeconds)].filter(Boolean).join(' · ')}
    badge={qualities.length > 0 && <span className={styles.held}>In Library · {qualities.join(', ')}</span>}
    to={`/library/${entry.id}`}
    action={action}
  />
}

/**
 * The card, and the three things a caller may put on it: a destination, a way
 * to open it in place, or neither.
 *
 * A Library Entry has a page of its own about the files it holds, so it takes
 * the destination. A Catalogue Video takes the Preview (ADR 0060). Nothing
 * takes both — two answers to one click is worse than none.
 */
function GridCard({
  artworkId,
  title,
  detail,
  status,
  badge,
  action,
  to,
  onOpen,
  prdbId,
}: {
  artworkId: VideoCard['id']
  title: string
  detail: string
  status: string
  badge: ReactNode
  action?: ReactNode
  to?: string
  onOpen?: () => void
  /** Named on the opener so the Preview can scroll the grid to this card. */
  prdbId?: string
}) {
  const artwork = <span className={styles.artwork}>
    <Artwork videoId={artworkId} title={title} />
    {badge}
  </span>

  return (
    <li className={styles.card}>
      {to
        ? <Link className={styles.artworkLink} to={to}>{artwork}</Link>
        : onOpen
          ? <button
              aria-label={`Preview ${title}`}
              className={styles.opener}
              data-video={prdbId}
              onClick={onOpen}
              type="button"
            >
              {artwork}
            </button>
          : artwork}
      {to
        ? <Link className={`${styles.title} ${styles.titleLink}`} to={to}>{title}</Link>
        : onOpen
          ? <button
              className={`${styles.title} ${styles.titleButton}`}
              onClick={onOpen}
              type="button"
            >
              {title}
            </button>
          : <span className={styles.title}>{title}</span>}
      <span className={styles.detail}>{detail}</span>
      <span className={styles.status}>{status}</span>
      {action}
    </li>
  )
}

/**
 * ADR 0030: the grid asks the tool for a picture by video, never the CDN. What
 * comes back is a cached file, one fetched on sight, or an empty answer — and
 * the third is not an error to report. It is a video prdb publishes no image
 * for, one whose URL has died, or a slow CDN, and all a grid can do about any
 * of them is leave the frame empty.
 */
export function Artwork({
  videoId,
  title,
  frameClassName = styles.frame,
  imageClassName = styles.image,
  absentClassName = styles.absent,
}: {
  videoId: VideoCard['id']
  title: string
  frameClassName?: string
  imageClassName?: string
  absentClassName?: string
}) {
  return (
    <CachedArtwork
      path={`/api/artwork/${videoId}`}
      title={title}
      frameClassName={frameClassName}
      imageClassName={imageClassName}
      absentClassName={absentClassName}
    />
  )
}

export function CachedArtwork({
  path,
  title,
  frameClassName = styles.frame,
  imageClassName = styles.image,
  absentClassName = styles.absent,
}: {
  path: string
  title: string
  frameClassName?: string
  imageClassName?: string
  absentClassName?: string
}) {
  const frame = useRef<HTMLSpanElement>(null)
  const [source, setSource] = useState<string | null>(null)
  const [absent, setAbsent] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    let objectUrl: string | null = null

    const load = async () => {
      try {
        const answer = await fetch(path, { signal: controller.signal })
        if (answer.status === 204 || !answer.ok) {
          setAbsent(true)
          return
        }

        objectUrl = URL.createObjectURL(await answer.blob())
        if (!controller.signal.aborted) setSource(objectUrl)
      } catch (error) {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setAbsent(true)
      }
    }

    const target = frame.current
    if (!target || !('IntersectionObserver' in window)) {
      void load()
      return () => {
        controller.abort()
        if (objectUrl) URL.revokeObjectURL(objectUrl)
      }
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          observer.disconnect()
          void load()
        }
      },
      { rootMargin: '240px' },
    )
    observer.observe(target)

    return () => {
      observer.disconnect()
      controller.abort()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [path])

  return (
    <span className={frameClassName} ref={frame}>
      {absent ? (
        <span className={absentClassName} aria-hidden="true">
          ▤
        </span>
      ) : source ? (
        <img
          className={imageClassName}
          src={source}
          alt={title}
          onError={() => setAbsent(true)}
        />
      ) : null}
    </span>
  )
}

/**
 * The site and the release date, in one line and in that order: the site is
 * what a person recognises, and the date is what tells two releases of one
 * scene apart.
 */
function describe(video: VideoCard): string {
  const released = video.releaseDate

  return [video.site, released].filter(Boolean).join(' · ')
}

function statusOf(video: VideoCard): string[] {
  const held = video.heldQualities?.length
    ? `In Library: ${video.heldQualities.join(', ')}`
    : null
  const available = video.availability === 'Ready'
    ? 'Best Release ready'
    : video.availability === 'ReleasesNeedInspection'
      ? 'Releases need inspection'
      : 'No identified Release'
  const wanted = video.wantedSyncFailure
    ? `Wanted sync blocked: ${video.wantedSyncFailure}`
    : video.wantedSyncPending
      ? 'Wanted sync pending'
      : video.wanted ? 'Wanted' : 'Not wanted'
  const activeDownload = video.activeDownloadId
    ? video.activeDownloadState === 'Completed' ? 'Processing download' : 'Downloading'
    : null
  return [wanted, activeDownload, held, available]
    .filter((value): value is string => Boolean(value))
}

function compactQualities(qualities: readonly string[]): string[] {
  return qualities.length <= 2
    ? [...qualities]
    : [...qualities.slice(0, 2), `+${qualities.length - 2}`]
}

function describeCopies(qualities: readonly string[]): string {
  return qualities.length === 1 ? `${qualities[0]} copy` : `${qualities.length} quality copies`
}

function describeRuntime(seconds: LibraryCard['runtimeSeconds']): string {
  if (seconds == null) return ''
  const minutes = Math.round(Number(seconds) / 60)
  return minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes} min`
}
