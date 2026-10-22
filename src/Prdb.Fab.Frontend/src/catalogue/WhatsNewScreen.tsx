import { PageNotice } from '../ui/PageNotice.tsx'
import { readPage } from '../ui/paging.ts'
import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useLocation, useSearchParams } from 'react-router'

import { listWhatsNew, observeWhatsNew } from '../api/client.ts'
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

  const videos = useQuery({
    queryKey: whatsNewKey(page),
    queryFn: () => listWhatsNew(page),
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
  const checkpointVideoId = videos.data?.checkpointVideoId
  const checkpointCreatedAt = videos.data?.checkpointCreatedAt

  useEffect(() => {
    if (!videos.isPlaceholderData && page === 1 && checkpointVideoId != null && checkpointCreatedAt) {
      void observeWhatsNew(checkpointVideoId, checkpointCreatedAt).catch(() => undefined)
    }
  }, [page, checkpointVideoId, checkpointCreatedAt, videos.isPlaceholderData])

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
        {total > 0 && <span className={styles.count}>{total} videos</span>}
        {Number(videos.data?.newCount ?? 0) > 0 && (
          <span className={styles.newCount}>{videos.data?.newCount} new since your previous visit</span>
        )}
      </div>

      <p className={styles.lede}>
        Browse the catalogue and open a Video to inspect it. Download starts the best
        eligible Release at your preferred Quality or below; Search opens its Release page.
      </p>

      <PageNotice page={page} pages={pages} pending={videos.isFetching} previousPage={videos.isPlaceholderData ? shownPage : undefined} unavailable={unavailable} />

      {unavailable ? null : total === 0 ? (
        <p className={styles.empty}>
          Nothing yet. prdb&rsquo;s catalogue is read in the background from the
          minute a key is saved, and this fills in as it arrives — there is
          nothing to press and nothing to wait in front of.
        </p>
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
