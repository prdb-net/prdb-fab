import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useParams, useSearchParams } from 'react-router'

import { listSites, readSite, setFavouriteSite, type SitePage, type SiteVideos } from '../api/client.ts'
import { releasePath, videoReleasePath } from '../release/routes.ts'
import { DirectoryView, VideoContextView } from './ContextBrowse.tsx'
import { sitesKey } from './state.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { PreferenceButton } from './PreferenceButton.tsx'

export function SitesScreen() {
  const { id } = useParams()
  const location = useLocation()
  const [parameters, setParameters] = useSearchParams()
  const search = parameters.get('search') ?? ''
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const scope = parameters.get('scope') === 'all' ? 'All' : 'Favourites'
  const answer = useQuery<SitePage | SiteVideos>({
    queryKey: sitesKey(id, search, page, scope),
    queryFn: () => (id ? readSite(id, search, page) : listSites(search, page, scope)),
  })

  if (answer.isPending) return <PageLoading label="Loading Sites" />
  if (answer.isError) return <main>That Site could not be read.</main>

  const setFilter = (value: string) => set(parameters, setParameters, { search: value })
  const goTo = (wanted: number) => set(parameters, setParameters, { page: String(wanted) })

  if (id) {
    const selected = answer.data as SiteVideos
    const videos = selected.videos
    const pages = Math.max(1, Math.ceil(Number(videos.total) / Number(videos.pageSize)))
    return (
      <VideoContextView
        title={selected.site.title}
        backTo="/sites"
        backLabel="All Sites"
        releaseAction={releasePath({ site: selected.site.prdbId }, location.pathname + location.search)}
        videos={videos.videos}
        search={search}
        page={Number(videos.page)}
        pages={pages}
        total={Number(videos.total)}
        setFilter={setFilter}
        goTo={goTo}
        videoAction={(video) => (
          <Link to={videoReleasePath(video.prdbId, location.pathname + location.search)}>
            Find releases for this Video
          </Link>
        )}
        contextAction={(
          <PreferenceButton
            active={selected.site.favourite}
            activeLabel="Unfavourite"
            inactiveLabel="Favourite"
            write={(desired) => setFavouriteSite(selected.site.prdbId, desired)}
          />
        )}
      />
    )
  }

  const directory = answer.data as SitePage
  const pages = Math.max(1, Math.ceil(Number(directory.total) / Number(directory.pageSize)))
  return (
    <DirectoryView
      title="Sites"
      noun="Site"
      items={directory.sites.map((site) => ({
        prdbId: site.prdbId,
        title: site.title,
        detail: site.network,
        videoCount: Number(site.videoCount),
        favourite: site.favourite,
        artworkPath: site.representativeVideoId
          ? `/api/artwork/${site.representativeVideoId}`
          : null,
        artworkLabel: site.representativeVideoId
          ? `Representative video artwork for ${site.title}`
          : `No representative video artwork for ${site.title}`,
        artworkCaption: site.representativeVideoId ? 'Representative Video' : undefined,
      }))}
      search={search}
      page={Number(directory.page)}
      pages={pages}
      total={Number(directory.total)}
      selectPath={(site) => `/sites/${site.prdbId}`}
      releasePath={(site) => releasePath({ site: site.prdbId }, location.pathname + location.search)}
      setFilter={setFilter}
      goTo={goTo}
      scope={scope}
      setScope={(nextScope) => {
        const next = new URLSearchParams(parameters)
        if (nextScope === 'All') next.set('scope', 'all')
        else next.delete('scope')
        next.delete('page')
        setParameters(next)
      }}
      toggleFavourite={(site) => (
        <PreferenceButton
          active={site.favourite}
          activeLabel="Unfavourite"
          inactiveLabel="Favourite"
          write={(desired) => setFavouriteSite(site.prdbId, desired)}
        />
      )}
    />
  )
}

function set(
  current: URLSearchParams,
  write: ReturnType<typeof useSearchParams>[1],
  change: { search?: string; page?: string },
) {
  const next = new URLSearchParams(current)
  if (change.search !== undefined) {
    if (change.search) next.set('search', change.search)
    else next.delete('search')
    next.delete('page')
  }
  if (change.page !== undefined) {
    if (Number(change.page) === 1) next.delete('page')
    else next.set('page', change.page)
  }
  write(next)
  window.scrollTo({ top: 0 })
}
