import { useEffect, useId, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import {
  askForPictures,
  askForUserPreviews,
  readSpriteTiles,
  readVideoPreview,
  type PreviewImage,
  type SpriteTileView,
  type UserPreviewCard,
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
    // Except while prdb is being asked about this Video, which is the one
    // thing about a Preview that can change under it — ADR 0060's pictures and
    // ADR 0061's user previews alike. The read is local, so this costs a query
    // and no request. It is also what makes a moderation removal show up while
    // the sheet is open: a withdrawn preview stops being in the answer.
    refetchInterval: (query) =>
      query.state.data?.picturesComing || query.state.data?.userPreviewsComing ? 2000 : false,
  })

  usePictures(prdbId, preview.data)
  useUserPreviews(prdbId, preview.data)
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
          <Gallery
            coming={preview.data?.picturesComing}
            images={preview.data?.images}
            key={prdbId}
            title={title}
            video={video}
          />

          {video && (
            <div className={styles.actions}>
              <CardActions includeSite video={video} returnTo={returnTo} />
            </div>
          )}

          <Contributed
            coming={preview.data?.userPreviewsComing}
            key={`${prdbId}-contributed`}
            previews={preview.data?.userPreviews}
            title={title}
          />

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
 * ADR 0060's one request, asked once for the Video on screen.
 *
 * A Preview opened on a Video the Catalogue holds no picture of asks prdb for
 * its detail. The sheet never waits on that: it renders what it has, the ask is
 * scheduled, and the gallery fills in when the read lands — for whoever opened
 * it, if they are still there.
 *
 * The backend decides whether the ask is spent at all. A Video that already has
 * pictures, or that was read recently enough that having none is prdb's answer,
 * is refused rather than repeated, so this is safe to call whenever a Preview
 * finds itself empty.
 */
function usePictures(prdbId: string, preview?: VideoPreview) {
  const ask = useMutation({ mutationFn: () => askForPictures(prdbId) })
  const { mutate, reset } = ask

  useEffect(() => reset(), [prdbId, reset])

  useEffect(() => {
    if (!preview || preview.images.length > 0 || preview.picturesComing) return
    if (ask.isPending || ask.isSuccess || ask.isError) return

    mutate()
  }, [ask.isError, ask.isPending, ask.isSuccess, mutate, preview])
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
  coming = false,
  images,
  title,
  video,
}: {
  /** Whether ADR 0060's one request is outstanding for this Video. */
  coming?: boolean
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
      <div className={styles.gallery}>
        <span aria-label={`No pictures for ${title}`} className={styles.frame} role="img">
          <span aria-hidden="true" className={styles.absent}>▤</span>
        </span>
        {coming && (
          <p className={styles.looking}>
            prdb published no picture of this Video when it was last read. Asking
            again &mdash; anything it has will appear here.
          </p>
        )}
      </div>
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
 * ADR 0061's demand, asked once for the Video on screen.
 *
 * The same shape as ADR 0060's above and for the same reason: the sheet renders
 * what it has, the ask is a request of its own rather than a side effect of the
 * read, and the strip fills in when the answer lands. The backend decides
 * whether it costs anything — a Video asked about inside the freshness window
 * is refused rather than repeated, so this is safe to call whenever a Preview
 * opens.
 *
 * Unlike the pictures ask it is not conditional on the Video having none. A
 * Video with ten pictures may still have a scrubbing strip somebody made from
 * their own copy, and the only way to find out is to ask.
 */
function useUserPreviews(prdbId: string, preview?: VideoPreview) {
  const ask = useMutation({ mutationFn: () => askForUserPreviews(prdbId) })
  const { mutate, reset } = ask

  useEffect(() => reset(), [prdbId, reset])

  useEffect(() => {
    if (!preview || preview.userPreviewsComing) return
    if (ask.isPending || ask.isSuccess || ask.isError) return

    mutate()
  }, [ask.isError, ask.isPending, ask.isSuccess, mutate, preview])
}

/**
 * The pictures other people made, under the ones prdb publishes itself.
 *
 * Under rather than mixed in, and that is the whole of the layout decision: a
 * user preview is made from **one file** by somebody who had it, and prdb's own
 * `images[]` are about the Video. A strip that blurred the two would make *a
 * picture of this Video* and *a picture of this file* the same claim, which is
 * exactly the distinction the Library and Identification are built on.
 *
 * Nothing here changes what the grids draw or what filing copies as the Entry
 * Image: ADR 0027's choice is untouched, and so is ADR 0060's gallery above.
 */
function Contributed({
  coming = false,
  previews,
  title,
}: {
  coming?: boolean
  previews?: readonly UserPreviewCard[]
  title: string
}) {
  const sprites = previews?.filter((preview) => preview.sprite) ?? []
  const singles = previews?.filter((preview) => !preview.sprite) ?? []

  if (sprites.length === 0 && singles.length === 0) {
    return coming ? (
      <p className={styles.looking}>Looking for previews people have made of this Video&hellip;</p>
    ) : null
  }

  return (
    <section className={styles.contributed}>
      <h3 className={styles.contributedHeading}>
        <span>From other people</span>
        {coming && <span>Looking&hellip;</span>}
      </h3>

      <ul className={styles.contributedList}>
        {sprites.map((preview) => (
          <li key={preview.prdbId}>
            <Sprite preview={preview} title={title} />
          </li>
        ))}
      </ul>

      {singles.length > 0 && (
        <ul className={styles.singles}>
          {singles.map((preview, index) => (
            <li key={preview.prdbId}>
              <CachedArtwork
                absentClassName={styles.thumbAbsent}
                frameClassName={styles.thumbFrame}
                imageClassName={styles.thumbImage}
                path={`/api/previews/${preview.prdbId}/${preview.version}/image`}
                title={`${title} — a picture somebody submitted, ${index + 1} of ${singles.length}`}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/**
 * One sprite sheet, shown as the thing it is: a strip somebody can move
 * through, a tile at a time, with the second each tile belongs to.
 *
 * **Never as an ordinary still.** A sheet drawn whole is a grid of a hundred
 * small pictures that reads as one frame of something it is not, and a sheet
 * cropped to its first tile is a still that quietly discards what the preview
 * is for. So it is a control, and the control is a range input: keyboard,
 * touch and screen reader support for a one-dimensional value is what that
 * element already is, and none of the three is worth reimplementing.
 *
 * **Nothing streams the Video File.** The strip is a picture and a list of
 * times; there is no player here and no byte of the file is read.
 */
function Sprite({ preview, title }: { preview: UserPreviewCard; title: string }) {
  const [at, setAt] = useState(0)
  const frame = useRef<HTMLDivElement>(null)
  const near = useNear(frame)
  const label = useId()

  const tiles = useQuery({
    queryKey: ['sprite-tiles', preview.prdbId, preview.version],
    queryFn: () => readSpriteTiles(preview.prdbId, preview.version),
    // Lazily, like every other picture in this sheet: a Preview opened and
    // stepped straight past has fetched nothing (ADR 0060).
    enabled: near,
    staleTime: 5 * 60 * 1000,
    // The first answer may be *still coming* while the pair is fetched from the
    // CDN. That is the one state worth asking about again, and it settles in a
    // second or two.
    refetchInterval: (query) => (query.state.data?.coming ? 1500 : false),
  })

  const sheet = useCachedImage(
    `/api/previews/${preview.prdbId}/${preview.version}/image`,
    near && (tiles.data?.usable ?? false),
  )

  const list = tiles.data?.tiles ?? []
  const tile = list[Math.min(at, Math.max(list.length - 1, 0))]

  if (tiles.data && !tiles.data.usable && !tiles.data.coming) {
    // Withdrawn between the read and now, at another version, or a pair that
    // does not describe its sheet. A gallery is a strip of pictures rather than
    // a census of rows, so it is simply not here.
    return null
  }

  return (
    <div className={styles.contributed}>
      <div
        aria-label={`${title} — a preview strip somebody made`}
        className={`${styles.tile} ${sheet && tile ? '' : styles.tileEmpty}`}
        ref={frame}
        role="img"
        style={sheet && tile ? position(preview, tile, sheet) : undefined}
      >
        {!(sheet && tile) && <span aria-hidden="true">▤</span>}
      </div>

      {list.length > 1 && (
        <>
          <label className={styles.at} htmlFor={label}>
            {clock(Number(tile?.startMs ?? 0))} of {clock(Number(list[list.length - 1].endMs))}
          </label>
          <input
            className={styles.scrub}
            id={label}
            max={list.length - 1}
            min={0}
            onChange={(event) => setAt(Number(event.target.value))}
            step={1}
            type="range"
            value={Math.min(at, list.length - 1)}
          />
        </>
      )}
    </div>
  )
}

/**
 * Where on the sheet one tile is, as a share of the grid rather than in pixels.
 *
 * Percentages, so that the strip is right at any width with nothing measured
 * and nothing re-measured when the window changes — the CSS sprite arithmetic,
 * with the columns and rows worked out from the tile the WebVTT actually named
 * rather than from what the payload claimed the grid was.
 */
function position(
  preview: UserPreviewCard,
  tile: SpriteTileView,
  sheet: string,
): React.CSSProperties {
  // The generated types carry every integer as `number | string`, because the
  // document says int32 and JSON says whatever it likes. Everything below is
  // arithmetic, so each one is read as a number once, here.
  const tileWidth = Math.max(Number(tile.width), 1)
  const tileHeight = Math.max(Number(tile.height), 1)
  const columns = Math.max(1, Math.round(Number(preview.width) / tileWidth))
  const rows = Math.max(1, Math.round(Number(preview.height) / tileHeight))
  const column = Math.round(Number(tile.x) / tileWidth)
  const row = Math.round(Number(tile.y) / tileHeight)

  return {
    aspectRatio: `${tileWidth} / ${tileHeight}`,
    backgroundImage: `url(${sheet})`,
    backgroundSize: `${columns * 100}% ${rows * 100}%`,
    backgroundPositionX: columns > 1 ? `${(column / (columns - 1)) * 100}%` : '0%',
    backgroundPositionY: rows > 1 ? `${(row / (rows - 1)) * 100}%` : '0%',
  }
}

/** `1:23` — the second a tile belongs to, in the form a player writes it. */
function clock(ms: number): string {
  const seconds = Math.max(0, Math.round(ms / 1000))
  const minutes = Math.floor(seconds / 60)

  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`
}

/**
 * Whether an element is near enough to the viewport to be worth fetching for.
 *
 * The same margin and the same observer `CachedArtwork` uses, as a hook,
 * because a sprite sheet is not fetched into an `<img>` — it is a background
 * over which a tile is positioned, and the element that has to be watched is
 * the one being positioned rather than one this could hand to that component.
 */
function useNear(element: React.RefObject<HTMLElement | null>, margin = '240px'): boolean {
  const [near, setNear] = useState(false)

  useEffect(() => {
    const target = element.current

    if (!target || !('IntersectionObserver' in window)) {
      setNear(true)
      return
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          observer.disconnect()
          setNear(true)
        }
      },
      { rootMargin: margin },
    )

    observer.observe(target)

    return () => observer.disconnect()
  }, [element, margin])

  return near
}

/**
 * One image fetched through the tool and held as an object URL for as long as
 * it is on screen.
 *
 * The browser asks this tool and never the CDN (ADR 0030), which for a user
 * preview is what makes a withdrawal enforceable at all: the address is
 * same-origin, the server stops answering it, and no URL prdb published ever
 * reaches the page.
 */
function useCachedImage(path: string, enabled: boolean): string | null {
  const [source, setSource] = useState<string | null>(null)

  useEffect(() => {
    if (!enabled) return

    const controller = new AbortController()
    let objectUrl: string | null = null

    const load = async () => {
      try {
        const answer = await fetch(path, { signal: controller.signal })

        if (answer.status === 204 || !answer.ok) return

        objectUrl = URL.createObjectURL(await answer.blob())

        if (!controller.signal.aborted) setSource(objectUrl)
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') return
      }
    }

    void load()

    return () => {
      controller.abort()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
      setSource(null)
    }
  }, [enabled, path])

  return source
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
