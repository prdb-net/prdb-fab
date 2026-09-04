import { useEffect, useId, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import { downloadBest, setWanted, type VideoCard } from '../api/client.ts'
import { videoReleasePath } from '../release/routes.ts'
import { prdbVideoUrl } from './prdb.ts'
import styles from './CardActions.module.css'

/**
 * The compact action language shared by catalogue cards: one named primary
 * act, one familiar state toggle, and an overflow for everything less frequent.
 * A surface may add its own contextual destination to that overflow without
 * changing the controls a person learns from the other catalogue grids.
 */
export function CardActions({
  video,
  returnTo,
  includeSite = false,
}: {
  video: VideoCard
  returnTo: string
  includeSite?: boolean
}) {
  const cache = useQueryClient()
  const preference = useMutation({
    mutationFn: () => setWanted(video.prdbId, !video.wanted),
    onSuccess: async (verdict) => {
      if (verdict.outcome === 'Updated') {
        await cache.invalidateQueries({ queryKey: ['catalogue'] })
      }
    },
  })
  const download = useMutation({
    mutationFn: () => downloadBest(video.prdbId),
    onSuccess: async () => {
      await Promise.all([
        cache.invalidateQueries({ queryKey: ['catalogue'] }),
        cache.invalidateQueries({ queryKey: ['downloads'] }),
        cache.invalidateQueries({ queryKey: ['releases'] }),
      ])
    },
  })
  const preferenceVerdict = preference.data
  const preferenceProblem = preference.error?.message
    ?? (preferenceVerdict && preferenceVerdict.outcome !== 'Updated' ? preferenceVerdict.detail : null)
  const downloadVerdict = download.data
  const downloadFailed = downloadVerdict
    && !['Submitted', 'Pending', 'SubmissionUnknown'].includes(downloadVerdict.outcome)
  const downloadProblem = download.error?.message ?? (downloadFailed ? downloadVerdict.detail : null)
  const problem = downloadProblem ?? preferenceProblem
  const held = Boolean(video.heldQualities?.length)
  const canDownload = video.downloadReady && !video.outstanding && !held
  const releaseLabel = held ? 'View Library' : canDownload ? 'Download' : 'Search'
  const releaseDescription = held
    ? `View ${video.title} in the Library`
    : canDownload
      ? `Download the preferred available Quality of ${video.title}`
      : `Search Indexers for ${video.title}`
  const primaryPath = held
    ? `/library/${video.prdbId}`
    : videoReleasePath(video.prdbId, returnTo)
  const wantedLabel = video.wanted ? 'Remove from Wanted' : 'Mark Wanted'

  return (
    <span className={styles.control}>
      <span className={styles.actions}>
        {canDownload ? (
          <button
            aria-busy={download.isPending}
            aria-label={releaseDescription}
            className={styles.primary}
            disabled={download.isPending}
            onClick={() => download.mutate()}
            title={releaseDescription}
            type="button"
          >
            <Icon name="download" />
            <span>{download.isPending ? 'Starting…' : releaseLabel}</span>
          </button>
        ) : (
          <Link
            aria-label={releaseDescription}
            className={styles.primary}
            title={releaseDescription}
            to={primaryPath}
          >
            <Icon name={held ? 'library' : 'search'} />
            <span>{releaseLabel}</span>
          </Link>
        )}

        <button
          aria-label={wantedLabel}
          aria-busy={preference.isPending}
          aria-pressed={video.wanted}
          className={`${styles.iconButton} ${video.wanted ? styles.wantedButton : ''}`}
          disabled={preference.isPending}
          onClick={() => preference.mutate()}
          title={wantedLabel}
          type="button"
        >
          <Icon active={video.wanted} name="wanted" />
        </button>

        <MoreActions includeSite={includeSite} video={video} returnTo={returnTo} />
      </span>

      {problem && (
        <span className={styles.error} role="alert">
          <span>{problem}</span>
          <button
            type="button"
            disabled={preference.isPending || download.isPending}
            onClick={() => downloadProblem ? download.mutate() : preference.mutate()}
          >
            Retry
          </button>
        </span>
      )}
    </span>
  )
}

function MoreActions({
  includeSite,
  video,
  returnTo,
}: {
  includeSite: boolean
  video: VideoCard
  returnTo: string
}) {
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
          <Link
            to={videoReleasePath(video.prdbId, returnTo)}
            onClick={() => setOpen(false)}
          >
            <Icon name="search" />
            <span>{video.heldQualities?.length ? 'Find another Release' : 'View Releases'}</span>
          </Link>
          {includeSite && video.sitePrdbId && (
            <Link to={`/sites/${video.sitePrdbId}`} onClick={() => setOpen(false)}>
              <Icon name="site" />
              <span>{video.site ?? 'View Site'}</span>
            </Link>
          )}
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

type IconName = 'download' | 'external' | 'library' | 'more' | 'search' | 'site' | 'wanted'

function Icon({ name, active = false }: { name: IconName; active?: boolean }) {
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
      {name === 'library' && (
        <path d="M4 5h16v15H4V5Zm4 0v15m9-15v15M3 9h18" {...common} />
      )}
      {name === 'wanted' && (
        <path
          d="M12 20S4 15.6 4 9.5A4.5 4.5 0 0 1 12 6.7a4.5 4.5 0 0 1 8 2.8C20 15.6 12 20 12 20Z"
          fill={active ? 'currentColor' : 'none'}
          stroke="currentColor"
          strokeLinejoin="round"
          strokeWidth="1.8"
        />
      )}
      {name === 'more' && (
        <path d="M5 12h.01M12 12h.01M19 12h.01" stroke="currentColor" strokeLinecap="round" strokeWidth="3" />
      )}
      {name === 'external' && (
        <path d="M14 4h6v6m0-6-9 9M18 13v6H5V6h6" {...common} />
      )}
      {name === 'site' && (
        <path d="M4 20V8l8-4 8 4v12M8 11h.01M12 11h.01M16 11h.01M8 15h.01M12 15h.01M16 15h.01M10 20v-2h4v2" {...common} />
      )}
    </svg>
  )
}
