import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useParams, useSearchParams } from 'react-router'

import { listActors, readActor, setFavouriteActor, type ActorPage, type ActorVideos } from '../api/client.ts'
import { releasePath, videoReleasePath } from '../release/routes.ts'
import { DirectoryView, VideoContextView } from './ContextBrowse.tsx'
import { actorsKey } from './state.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { PreferenceButton } from './PreferenceButton.tsx'

export function ActorsScreen() {
  const { id } = useParams()
  const location = useLocation()
  const [parameters, setParameters] = useSearchParams()
  const search = parameters.get('search') ?? ''
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const scope = parameters.get('scope') === 'all' ? 'All' : 'Favourites'
  const answer = useQuery<ActorPage | ActorVideos>({
    queryKey: actorsKey(id, search, page, scope),
    queryFn: () => (id ? readActor(id, search, page) : listActors(search, page, scope)),
  })

  if (answer.isPending) return <PageLoading label="Loading Actors" />
  if (answer.isError) return <main>That Actor could not be read.</main>

  const write = (searchValue: string | undefined, pageValue: number | undefined) => {
    const next = new URLSearchParams(parameters)
    if (searchValue !== undefined) {
      if (searchValue) next.set('search', searchValue)
      else next.delete('search')
      next.delete('page')
    }
    if (pageValue !== undefined) {
      if (pageValue === 1) next.delete('page')
      else next.set('page', String(pageValue))
    }
    setParameters(next)
    window.scrollTo({ top: 0 })
  }

  if (id) {
    const selected = answer.data as ActorVideos
    const videos = selected.videos
    const pages = Math.max(1, Math.ceil(Number(videos.total) / Number(videos.pageSize)))
    return (
      <VideoContextView
        title={selected.actor.title}
        backTo="/actors"
        backLabel="All Actors"
        releaseAction={releasePath({ actor: selected.actor.prdbId }, location.pathname + location.search)}
        videos={videos.videos}
        search={search}
        page={Number(videos.page)}
        pages={pages}
        total={Number(videos.total)}
        setFilter={(value) => write(value, undefined)}
        goTo={(wanted) => write(undefined, wanted)}
        videoAction={(video) => (
          <Link to={videoReleasePath(video.prdbId, location.pathname + location.search)}>
            Search Indexers
          </Link>
        )}
        contextAction={(
          <PreferenceButton
            active={selected.actor.favourite}
            activeLabel="Unfavourite"
            inactiveLabel="Favourite"
            write={(desired) => setFavouriteActor(selected.actor.prdbId, desired)}
          />
        )}
      />
    )
  }

  const directory = answer.data as ActorPage
  const pages = Math.max(1, Math.ceil(Number(directory.total) / Number(directory.pageSize)))
  return (
    <DirectoryView
      title="Actors"
      noun="Actor"
      items={directory.actors.map((actor) => ({
        prdbId: actor.prdbId,
        title: actor.name,
        videoCount: Number(actor.videoCount),
        favourite: actor.favourite,
        artworkPath: `/api/artwork/actors/${actor.prdbId}`,
      }))}
      search={search}
      page={Number(directory.page)}
      pages={pages}
      total={Number(directory.total)}
      selectPath={(actor) => `/actors/${actor.prdbId}`}
      releasePath={(actor) => releasePath({ actor: actor.prdbId }, location.pathname + location.search)}
      setFilter={(value) => write(value, undefined)}
      goTo={(wanted) => write(undefined, wanted)}
      scope={scope}
      setScope={(nextScope) => {
        const next = new URLSearchParams(parameters)
        if (nextScope === 'All') next.set('scope', 'all')
        else next.delete('scope')
        next.delete('page')
        setParameters(next)
      }}
      toggleFavourite={(actor) => (
        <PreferenceButton
          active={actor.favourite}
          activeLabel="Unfavourite"
          inactiveLabel="Favourite"
          write={(desired) => setFavouriteActor(actor.prdbId, desired)}
        />
      )}
    />
  )
}
