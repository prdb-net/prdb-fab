import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useLocation, useSearchParams } from 'react-router'

import { listWhatsNew, observeWhatsNew } from '../api/client.ts'
import { Grid } from './Grid.tsx'
import { whatsNewKey } from './state.ts'
import styles from './WhatsNew.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { CardActions } from './CardActions.tsx'

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
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)

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
  const checkpointVideoId = videos.data?.checkpointVideoId
  const checkpointCreatedAt = videos.data?.checkpointCreatedAt

  useEffect(() => {
    if (page === 1 && checkpointVideoId != null && checkpointCreatedAt) {
      void observeWhatsNew(checkpointVideoId, checkpointCreatedAt).catch(() => undefined)
    }
  }, [page, checkpointVideoId, checkpointCreatedAt])

  const goTo = (wanted: number) => {
    setParameters(wanted === 1 ? {} : { page: String(wanted) })
    window.scrollTo({ top: 0 })
  }

  if (videos.isPending && !videos.data) {
    return <PageLoading label="Loading What’s new" />
  }

  return (
    <main className={styles.screen}>
      <div className={styles.heading}>
        <h1>What&rsquo;s new</h1>
        {total > 0 && <span className={styles.count}>{total} videos</span>}
        {Number(videos.data?.newCount ?? 0) > 0 && (
          <span className={styles.newCount}>{videos.data?.newCount} new since your previous visit</span>
        )}
      </div>

      <p className={styles.lede}>
        Browse the catalogue, then open a Video to see Releases already found by background
        discovery. Downloads start from a Video&rsquo;s Release page.
      </p>

      {total === 0 ? (
        <p className={styles.empty}>
          Nothing yet. prdb&rsquo;s catalogue is read in the background from the
          minute a key is saved, and this fills in as it arrives — there is
          nothing to press and nothing to wait in front of.
        </p>
      ) : (
        <>
          <Grid
            videos={videos.data?.videos ?? []}
            action={(video) => (
              <CardActions
                includeSite
                video={video}
                returnTo={location.pathname + location.search}
              />
            )}
          />

          {pages > 1 && (
            <nav className={styles.pager}>
              <button type="button" onClick={() => goTo(page - 1)} disabled={page <= 1}>
                Newer
              </button>
              <span className={styles.where}>
                Page {page} of {pages}
              </span>
              <button type="button" onClick={() => goTo(page + 1)} disabled={page >= pages}>
                Older
              </button>
            </nav>
          )}
        </>
      )}
    </main>
  )
}
