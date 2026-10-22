import { PageNotice } from '../ui/PageNotice.tsx'
import { readPage } from '../ui/paging.ts'
import { useQuery } from '@tanstack/react-query'
import { useLocation, useSearchParams } from 'react-router'

import { listWanted } from '../api/client.ts'
import { Grid } from './Grid.tsx'
import { wantedKey } from './state.ts'
import styles from './Wanted.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { CardActions } from './CardActions.tsx'
import { PreviewOverlay } from './Preview.tsx'
import { usePreview } from './preview.ts'
import { Loaded } from '../ui/Loaded.tsx'

/**
 * The wanted list, and where setting up ends.
 *
 * ADR 0010 always described this as the last step's destination, with the first
 * sync visibly running, because `VISION.md` measures onboarding by whether it
 * leads to a first download rather than by whether it completes. What stood
 * here until the catalogue existed was a page saying so; this replaces it.
 *
 * The list is prdb account state projected locally. ADR 0048 lets a person
 * change it here through the governed backend write path without exposing the
 * account key to the browser.
 */
export function WantedScreen() {
  // ADR 0036: where in the grid the user is, in the address.
  const [parameters, setParameters] = useSearchParams()
  const location = useLocation()
  const page = readPage(parameters)
  const { openPreview } = usePreview()
  const here = location.pathname + location.search

  const wanted = useQuery({
    queryKey: wantedKey(page),
    queryFn: () => listWanted(page),
    placeholderData: (held) => held,
  })

  const total = Number(wanted.data?.total ?? 0)
  const pageSize = Number(wanted.data?.pageSize ?? 24)
  const pages = Math.max(1, Math.ceil(total / pageSize))
  const shownPage = Number(wanted.data?.page ?? page)
  const unavailable = wanted.data !== undefined && !wanted.isError && !wanted.isPlaceholderData && page > pages

  const goTo = (to: number) => {
    const next = new URLSearchParams(parameters)
    if (to === 1) next.delete('page')
    else next.set('page', String(to))
    setParameters(next)
    window.scrollTo({ top: 0 })
  }

  if (wanted.isPending && !wanted.data) {
    return <PageLoading label="Loading Wanted videos" />
  }

  if (wanted.isError) {
    return <main className={styles.screen} aria-busy={wanted.isFetching}>
      <h1>Wanted</h1>
      <Loaded query={wanted} of="the Wanted list">{() => null}</Loaded>
    </main>
  }

  return (
    <main className={styles.screen} aria-busy={wanted.isFetching}>
      <div className={styles.heading}>
        <h1>Wanted</h1>
        {total > 0 && <span className={styles.count}>{total} videos</span>}
      </div>

      <p className={styles.lede}>
        Your prdb Wanted list. Changes made here are written back to the connected account.
      </p>

      {total > 0 && (
        <details className={styles.guide}>
          <summary>How Wanted works</summary>
          <p>
            Videos you already have are marked <strong>In Library</strong>. For the rest,
            Download starts the best eligible Release at your preferred Quality or below,
            and Search opens the Release page to inspect or find one.
          </p>
        </details>
      )}

      {wanted.data?.recentWindowFilling && (
        <p className={styles.working}>
          A complete read of prdb&rsquo;s 90-day Recent Window is running in the
          background. What is already here works, and the rest fills in.
        </p>
      )}

      <PageNotice page={page} pages={pages} pending={wanted.isFetching} previousPage={wanted.isPlaceholderData ? shownPage : undefined} unavailable={unavailable} />

      {unavailable ? null : total === 0 ? (
        <div className={styles.empty}>{empty(wanted.data?.feedHasRun === true)}</div>
      ) : (
        <>
          <Grid
            videos={wanted.data?.videos ?? []}
            onOpen={(video) => openPreview(video.prdbId)}
            action={(video) => (
              <CardActions
                video={video}
                returnTo={here}
              />
            )}
          />

          {pages > 1 && !unavailable && (
            <nav className={styles.pager}>
              <button type="button" onClick={() => goTo(page - 1)} disabled={wanted.isFetching || shownPage <= 1}>
                Newer
              </button>
              <span className={styles.where}>
                Page {shownPage} of {pages}
              </span>
              <button type="button" onClick={() => goTo(page + 1)} disabled={wanted.isFetching || shownPage >= pages}>
                Older
              </button>
            </nav>
          )}
        </>
      )}
      <PreviewOverlay returnTo={here} videos={wanted.data?.videos ?? []} />
    </main>
  )
}

/**
 * Two empty lists that look identical and are not. One is an account with
 * nothing marked; the other is a list that has not been read yet, on an
 * installation whose key was saved a minute ago. Telling somebody there is
 * nothing on their list when it simply has not arrived is the difference
 * between a tool that is working and a tool that looks broken.
 */
function empty(feedHasRun: boolean) {
  return feedHasRun ? (
    <>
      <p>Nothing is on your wanted list.</p>
      <p>
        Mark a video as wanted in prdb and it appears here on the next read,
        which is a few minutes away rather than a restart.
      </p>
    </>
  ) : (
    <>
      <p>Your wanted list has not been read yet.</p>
      <p>
        The first read happens shortly after the key is saved. Nothing here is
        waiting on you.
      </p>
    </>
  )
}
