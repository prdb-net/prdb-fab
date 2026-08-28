import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router'

import { listActors, readActor, type ActorPage, type ActorVideos } from '../api/client.ts'
import { releasePath, videoReleasePath } from '../release/routes.ts'
import { DirectoryView, VideoContextView } from './ContextBrowse.tsx'
import { actorsKey } from './state.ts'

export function ActorsScreen() {
  const { id } = useParams()
  const [parameters, setParameters] = useSearchParams()
  const search = parameters.get('search') ?? ''
  const page = Math.max(1, Number(parameters.get('page') ?? '1') || 1)
  const answer = useQuery<ActorPage | ActorVideos>({
    queryKey: actorsKey(id, search, page),
    queryFn: () => (id ? readActor(id, search, page) : listActors(search, page)),
  })

  if (answer.isPending) return null
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
        releaseAction={releasePath({ actor: selected.actor.prdbId })}
        videos={videos.videos}
        search={search}
        page={Number(videos.page)}
        pages={pages}
        total={Number(videos.total)}
        setFilter={(value) => write(value, undefined)}
        goTo={(wanted) => write(undefined, wanted)}
        videoAction={(video) => (
          <Link to={videoReleasePath(video.prdbId)}>Find releases for this Video</Link>
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
      }))}
      search={search}
      page={Number(directory.page)}
      pages={pages}
      total={Number(directory.total)}
      selectPath={(actor) => `/actors/${actor.prdbId}`}
      releasePath={(actor) => releasePath({ actor: actor.prdbId })}
      setFilter={(value) => write(value, undefined)}
      goTo={(wanted) => write(undefined, wanted)}
    />
  )
}
