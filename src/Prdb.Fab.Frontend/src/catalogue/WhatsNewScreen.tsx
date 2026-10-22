import { PageNotice } from '../ui/PageNotice.tsx'
import { readPage } from '../ui/paging.ts'
import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useLocation, useSearchParams } from 'react-router'

import { listVideos, listWhatsNew, observeWhatsNew, type VideoPage, type WhatsNewPage } from '../api/client.ts'
import { Grid } from './Grid.tsx'
import { whatsNewKey } from './state.ts'
import styles from './BrowseScreen.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { CardActions } from './CardActions.tsx'
import { PreviewOverlay } from './Preview.tsx'
import { usePreview } from './preview.ts'
import { Loaded } from '../ui/Loaded.tsx'

/**
 * What's New, and the landing page. ADR 0013 calls it that and it is what the
 * catalogue exists for: an installation that has finished setting up lands here
 * and sees what prdb has published, without having pressed anything.
 *
 * It makes no request to prdb. The catalogue is what the sync routines keep
 * current, and this is one query over it — so a reload costs no request and
 * spends no budget, which is ADR 0018's rule that refreshing never causes work.
 */
export function WhatsNewScreen() {
  // ADR 0036: what is worth linking to is in the address, which for a grid is
  // where in it the user is. It costs nothing now and cannot be retrofitted.
  const [parameters, setParameters] = useSearchParams()
  const location = useLocation()
  const page = readPage(parameters)
  const { openPreview } = usePreview()
  const latest = parameters.get('order') === 'latest'
  const favourites = parameters.get('favourites') === 'true'
  // Ready is the landing default; an explicit false keeps the broader view linkable.
  const ready = parameters.get('ready') !== 'false'
  const unfilteredRecent = !latest && !favourites && !ready
  const change = (name: string, value: string | null) => {
    const next = new URLSearchParams(parameters)
    if (value) next.set(name, value)
    else next.delete(name)
    next.delete('page')
    setParameters(next)
  }

  const videos = useQuery<VideoPage | WhatsNewPage>({
    queryKey: [...whatsNewKey(page), latest, favourites, ready],
    queryFn: () => unfilteredRecent
      ? listWhatsNew(page)
      : listVideos('', page, ready ? 'DownloadReady' : 'All', latest ? 'ReleaseDateDescending' : 'CreatedDescending', favourites),
    // The previous page stays on screen while the next one is read, so paging
    // does not flash the layout away and back.
    placeholderData: (held) => held,
  })

  // ASP.NET Core declares an integer as `integer | string`, because the web
  // defaults accept a number written as one. The document is right and the
  // frontend says so rather than casting the type away.
  const total = Number(videos.data?.total ?? 0)
  const pageSize = Number(videos.data?.pageSize ?? 24)
  const pages = Math.max(1, Math.ceil(total / pageSize))
  const shownPage = Number(videos.data?.page ?? page)
  const unavailable = videos.data !== undefined && !videos.isError && !videos.isPlaceholderData && page > pages
  const checkpointVideoId = videos.data && 'checkpointVideoId' in videos.data ? videos.data.checkpointVideoId : null
  const checkpointCreatedAt = videos.data && 'checkpointCreatedAt' in videos.data ? videos.data.checkpointCreatedAt : null
  const newCount = videos.data && 'newCount' in videos.data ? Number(videos.data.newCount) : 0

  useEffect(() => {
    if (unfilteredRecent && !videos.isPlaceholderData && page === 1 && checkpointVideoId != null && checkpointCreatedAt) {
      void observeWhatsNew(checkpointVideoId, checkpointCreatedAt).catch(() => undefined)
    }
  }, [page, checkpointVideoId, checkpointCreatedAt, videos.isPlaceholderData, unfilteredRecent])

  const goTo = (wanted: number) => {
    const next = new URLSearchParams(parameters)
    if (wanted === 1) next.delete('page')
    else next.set('page', String(wanted))
    setParameters(next)
    window.scrollTo({ top: 0 })
  }
  const here = location.pathname + location.search

  if (videos.isPending && !videos.data) {
    return <PageLoading label="Loading What’s new" />
  }

  if (videos.isError) {
    return <main className={styles.screen} aria-busy={videos.isFetching}>
      <h1>What&rsquo;s new</h1>
      <Loaded query={videos} of="the latest Videos">{() => null}</Loaded>
    </main>
  }

  return (
    <main className={styles.screen} aria-busy={videos.isFetching}>
      <div className={styles.heading}>
        <h1>What&rsquo;s new</h1>
        {total > 0 && <span className={styles.count}>{total.toLocaleString('en')} videos</span>}
        {newCount > 0 && (
          <span className={styles.newCount}>{newCount.toLocaleString('en')} new since your previous visit</span>
        )}
      </div>

      <p className={styles.lede}>
        {latest ? 'The latest release dates in your Catalogue.' : 'Recently added to prdb, including newly catalogued older Videos.'}
      </p>

      <div className={styles.discoveryControls}>
        <div className={styles.segmented} role="group" aria-label="Catalogue order">
          <button type="button" aria-pressed={!latest} onClick={() => change('order', null)}>Recently added</button>
          <button type="button" aria-pressed={latest} onClick={() => change('order', 'latest')}>Latest releases</button>
        </div>
        <div className={styles.quickFilters} role="group" aria-label="Catalogue filters">
          <button type="button" aria-pressed={favourites} onClick={() => change('favourites', favourites ? null : 'true')}>Favourites</button>
          <button type="button" aria-pressed={ready} onClick={() => change('ready', ready ? 'false' : null)}>Ready to download</button>
        </div>
      </div>
      {favourites && <p className={styles.filterHint}>Videos from your favourite Sites or Actors.</p>}
      <PageNotice page={page} pages={pages} pending={videos.isFetching} previousPage={videos.isPlaceholderData ? shownPage : undefined} unavailable={unavailable} />

      {unavailable ? null : total === 0 ? (
        <div className={styles.empty}>
          <strong>{favourites || ready ? 'No Videos match these filters.' : 'Your Catalogue is on its way.'}</strong>
          <p>{favourites || ready ? 'Try a broader view to see more Videos.' : 'Videos appear here as prdb syncs in the background.'}</p>
          {(favourites || ready) && <button type="button" onClick={() => {
            const next = new URLSearchParams(parameters)
            next.delete('favourites'); next.set('ready', 'false'); next.delete('page')
            setParameters(next)
          }}>Clear filters</button>}
        </div>
      ) : (
        <>
          <Grid
            videos={videos.data?.videos ?? []}
            onOpen={(video) => openPreview(video.prdbId)}
            action={(video) => (
              <CardActions
                includeSite
                video={video}
                returnTo={here}
              />
            )}
          />

          {pages > 1 && !unavailable && (
            <nav className={styles.pager}>
              <button type="button" onClick={() => goTo(page - 1)} disabled={videos.isFetching || shownPage <= 1}>
                Newer
              </button>
              <span className={styles.where}>
                Page {shownPage} of {pages}
              </span>
              <button type="button" onClick={() => goTo(page + 1)} disabled={videos.isFetching || shownPage >= pages}>
                Older
              </button>
            </nav>
          )}
        </>
      )}
      <PreviewOverlay returnTo={here} videos={videos.data?.videos ?? []} />
    </main>
  )
}
