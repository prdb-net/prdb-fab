import { useState } from 'react'

import type { VideoCard } from '../api/client.ts'
import styles from './Grid.module.css'

/**
 * The grid, written once and used by every browse surface.
 *
 * ADR 0012 makes five surfaces artwork grids and says the differences between
 * them are the source and the actions, never the card — so this takes a list
 * and nothing else. There is no action on a card today: *find this on the
 * indexers* belongs to the matching slice, and a card that does nothing is
 * honest until it exists.
 */
export function Grid({ videos }: { videos: readonly VideoCard[] }) {
  return (
    <ul className={styles.grid}>
      {videos.map((video) => (
        <Card key={video.id} video={video} />
      ))}
    </ul>
  )
}

function Card({ video }: { video: VideoCard }) {
  return (
    <li className={styles.card}>
      <Artwork videoId={video.id} title={video.title} />
      <span className={styles.title}>{video.title}</span>
      <span className={styles.detail}>{describe(video)}</span>
    </li>
  )
}

/**
 * ADR 0030: the grid asks the tool for a picture by video, never the CDN. What
 * comes back is a cached file, one fetched on sight, or a 404 — and the third
 * is not an error to report. It is a video prdb publishes no image for, one
 * whose URL has died, or a slow CDN, and all a grid can do about any of them is
 * leave the frame empty.
 */
function Artwork({ videoId, title }: { videoId: VideoCard['id']; title: string }) {
  const [absent, setAbsent] = useState(false)

  return (
    <div className={styles.frame}>
      {absent ? (
        <span className={styles.absent} aria-hidden="true">
          ▤
        </span>
      ) : (
        <img
          className={styles.image}
          src={`/api/artwork/${videoId}`}
          alt={title}
          loading="lazy"
          onError={() => setAbsent(true)}
        />
      )}
    </div>
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
