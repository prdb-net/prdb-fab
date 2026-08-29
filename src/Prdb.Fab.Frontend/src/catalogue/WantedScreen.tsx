import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useSearchParams } from 'react-router'

import { listWanted } from '../api/client.ts'
import { Grid } from './Grid.tsx'
import gridStyles from './Grid.module.css'
import { prdbVideoUrl } from './prdb.ts'
import { wantedKey } from './state.ts'
import { videoReleasePath } from '../release/routes.ts'
import styles from './Wanted.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'

/**
 * The wanted list, and where setting up ends.
 *
 * ADR 0010 always described this as the last step's destination, with the first
 * sync visibly running, because `VISION.md` measures onboarding by whether it
 * leads to a first download rather than by whether it completes. What stood
 * here until the catalogue existed was a page saying so; this replaces it.
 *
 * Wanting happens in prdb. `CONTEXT.md` defines a Wanted Video as one the user
 * has marked there and ADR 0007 makes that list the only source of intent, so
 * this surface reads and never writes — there is no way to add to it here, and
 * that is not a missing feature.
 */
export function WantedScreen() {
  // ADR 0036: where in the grid the user is, in the address.
  const [parameters, setParameters] = useSearchParams()
  const location = useLocation()
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)

  const wanted = useQuery({
    queryKey: wantedKey(page),
    queryFn: () => listWanted(page),
    placeholderData: (held) => held,
  })

  const total = Number(wanted.data?.total ?? 0)
  const pageSize = Number(wanted.data?.pageSize ?? 48)
  const pages = Math.max(1, Math.ceil(total / pageSize))

  const goTo = (to: number) => {
    setParameters(to === 1 ? {} : { page: String(to) })
    window.scrollTo({ top: 0 })
  }

  if (wanted.isPending && !wanted.data) {
    return <PageLoading label="Loading Wanted videos" />
  }

  return (
    <main className={styles.screen}>
      <div className={styles.heading}>
        <h1>Wanted</h1>
        {total > 0 && <span className={styles.count}>{total} videos</span>}
      </div>

      <p className={styles.lede}>
        What you have marked as wanted in prdb. Mark them there and they arrive
        here — this list is read, never written.
      </p>

      {total > 0 && (
        <div className={styles.guide}>
          <strong>To start a Download</strong>
          <span>
            Open a Video below. If discovery has found an eligible Release, the best one and
            its Download button appear at the top of the Release page.
          </span>
        </div>
      )}

      {wanted.data?.backfillRunning && (
        <p className={styles.working}>
          The first read of prdb&rsquo;s catalogue is still running in the
          background. Nothing is wrong and nothing is waiting on it; what is
          already here works, and the rest fills in.
        </p>
      )}

      {total === 0 ? (
        <div className={styles.empty}>{empty(wanted.data?.feedHasRun === true)}</div>
      ) : (
        <>
          <Grid
            videos={wanted.data?.videos ?? []}
            action={(video) => (
              <span className={gridStyles.actions}>
                <Link to={videoReleasePath(video.prdbId, location.pathname + location.search)}>
                  {video.downloadReady ? 'Download' : 'View releases'}
                </Link>
                <a
                  className={gridStyles.action}
                  href={prdbVideoUrl(String(video.prdbId))}
                  target="_blank"
                  rel="noreferrer"
                >
                  Open in prdb
                </a>
              </span>
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
