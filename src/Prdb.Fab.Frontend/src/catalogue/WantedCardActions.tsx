import { useEffect, useId, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import { setWanted, type VideoCard } from '../api/client.ts'
import { videoReleasePath } from '../release/routes.ts'
import { prdbVideoUrl } from './prdb.ts'
import styles from './WantedCardActions.module.css'

/**
 * The compact action language is tried on Wanted first: one named primary act,
 * one familiar state toggle, and an overflow for everything less frequent.
 * Keeping it local means the other catalogue surfaces retain their established
 * actions until this shape has proved useful here.
 */
export function WantedCardActions({
  video,
  returnTo,
}: {
  video: VideoCard
  returnTo: string
}) {
  const cache = useQueryClient()
  const remove = useMutation({
    mutationFn: () => setWanted(video.prdbId, false),
    onSuccess: async (verdict) => {
      if (verdict.outcome === 'Updated') {
        await cache.invalidateQueries({ queryKey: ['catalogue'] })
      }
    },
  })
  const verdict = remove.data
  const problem = remove.error?.message
    ?? (verdict && verdict.outcome !== 'Updated' ? verdict.detail : null)
  const releaseLabel = video.downloadReady ? 'Download' : 'Search'
  const releaseDescription = video.downloadReady
    ? `Open Releases for ${video.title} to Download`
    : `Search Indexers for ${video.title}`

  return (
    <span className={styles.control}>
      <span className={styles.actions}>
        <Link
          aria-label={releaseDescription}
          className={styles.primary}
          title={releaseDescription}
          to={videoReleasePath(video.prdbId, returnTo)}
        >
          <Icon name={video.downloadReady ? 'download' : 'search'} />
          <span>{releaseLabel}</span>
        </Link>

        <button
          aria-label="Remove from Wanted"
          aria-busy={remove.isPending}
          className={`${styles.iconButton} ${styles.wantedButton}`}
          disabled={remove.isPending}
          onClick={() => remove.mutate()}
          title="Remove from Wanted"
          type="button"
        >
          <Icon name="wanted" />
        </button>

        <MoreActions video={video} />
      </span>

      {problem && (
        <span className={styles.error} role="alert">
          <span>{problem}</span>
          <button type="button" disabled={remove.isPending} onClick={() => remove.mutate()}>
            Retry
          </button>
        </span>
      )}
    </span>
  )
}

function MoreActions({ video }: { video: VideoCard }) {
  const [open, setOpen] = useState(false)
  const menuId = useId()
  const container = useRef<HTMLSpanElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return

    const closeOutside = (event: PointerEvent) => {
      if (event.target instanceof Node && !container.current?.contains(event.target)) {
        setOpen(false)
      }
    }
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
        trigger.current?.focus()
      }
    }

    document.addEventListener('pointerdown', closeOutside)
    window.addEventListener('keydown', closeOnEscape)
    return () => {
      document.removeEventListener('pointerdown', closeOutside)
      window.removeEventListener('keydown', closeOnEscape)
    }
  }, [open])

  return (
    <span
      className={styles.more}
      onBlur={(event) => {
        if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget)) {
          setOpen(false)
        }
      }}
      ref={container}
    >
      <button
        aria-controls={open ? menuId : undefined}
        aria-expanded={open}
        aria-haspopup="true"
        aria-label={`More actions for ${video.title}`}
        className={styles.iconButton}
        onClick={() => setOpen((current) => !current)}
        ref={trigger}
        title="More actions"
        type="button"
      >
        <Icon name="more" />
      </button>

      {open && (
        <span className={styles.menu} id={menuId}>
          <a
            href={prdbVideoUrl(String(video.prdbId))}
            onClick={() => setOpen(false)}
            rel="noreferrer"
            target="_blank"
          >
            <Icon name="external" />
            <span>Open in prdb</span>
          </a>
        </span>
      )}
    </span>
  )
}

type IconName = 'download' | 'external' | 'more' | 'search' | 'wanted'

function Icon({ name }: { name: IconName }) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth: 1.8,
  }

  return (
    <svg aria-hidden="true" className={styles.icon} viewBox="0 0 24 24">
      {name === 'search' && (
        <path d="m20 20-4.3-4.3m2.3-5.2a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z" {...common} />
      )}
      {name === 'download' && (
        <path d="M12 3v12m-4-4 4 4 4-4M4 20h16" {...common} />
      )}
      {name === 'wanted' && (
        <path d="M12 20S4 15.6 4 9.5A4.5 4.5 0 0 1 12 6.7a4.5 4.5 0 0 1 8 2.8C20 15.6 12 20 12 20Z" fill="currentColor" />
      )}
      {name === 'more' && (
        <path d="M5 12h.01M12 12h.01M19 12h.01" stroke="currentColor" strokeLinecap="round" strokeWidth="3" />
      )}
      {name === 'external' && (
        <path d="M14 4h6v6m0-6-9 9M18 13v6H5V6h6" {...common} />
      )}
    </svg>
  )
}
