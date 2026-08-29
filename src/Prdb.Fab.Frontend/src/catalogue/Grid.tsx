import { useEffect, useRef, useState, type ReactNode } from 'react'

import type { VideoCard } from '../api/client.ts'
import styles from './Grid.module.css'

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
}: {
  videos: readonly VideoCard[]
  /** What this surface offers on a card, if anything. */
  action?: (video: VideoCard) => ReactNode
}) {
  return (
    <ul className={styles.grid}>
      {videos.map((video) => (
        <Card key={video.id} video={video} action={action} />
      ))}
    </ul>
  )
}

function Card({
  video,
  action,
}: {
  video: VideoCard
  action?: (video: VideoCard) => ReactNode
}) {
  return (
    <li className={styles.card}>
      <Artwork videoId={video.id} title={video.title} />
      <span className={styles.title}>{video.title}</span>
      <span className={styles.detail}>{describe(video)}</span>
      {action?.(video)}
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
  const frame = useRef<HTMLSpanElement>(null)
  const [source, setSource] = useState<string | null>(null)
  const [absent, setAbsent] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    let objectUrl: string | null = null

    const load = async () => {
      try {
        const answer = await fetch(`/api/artwork/${videoId}`, { signal: controller.signal })
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
  }, [videoId])

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
