import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useParams, useSearchParams } from 'react-router'

import {
  listActors,
  loadLatestActorVideos,
  readActor,
  setFavouriteActor,
  type ActorPage,
  type ActorVideos,
} from '../api/client.ts'
import { releasePath, videoReleasePath } from '../release/routes.ts'
import { DirectoryView, VideoContextView } from './ContextBrowse.tsx'
import { actorsKey } from './state.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import { PreferenceButton } from './PreferenceButton.tsx'
import { CachedArtwork } from './Grid.tsx'
import styles from './ContextBrowse.module.css'

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
    refetchInterval: (query) => {
      const data = query.state.data
      return id && data && 'actor' in data && data.actor.videoLoad?.active ? 1500 : false
    },
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
        contextDetail={<ActorProfileDetails actor={selected.actor} />}
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
          iconOnly
          write={(desired) => setFavouriteActor(actor.prdbId, desired)}
        />
      )}
    />
  )
}

function ActorProfileDetails({ actor }: { actor: ActorVideos['actor'] }) {
  const queries = useQueryClient()
  const load = useMutation({
    mutationFn: () => loadLatestActorVideos(actor.prdbId),
    onSuccess: () => queries.invalidateQueries({ queryKey: ['catalogue', 'actors', actor.prdbId] }),
  })
  const aliases = actor.aliases?.map((alias) => alias.name).join(', ')
  const facts = [
    ['Also known as', aliases],
    ['Gender', actor.gender],
    ['Born', describeBirthday(actor.birthday, actor.birthdayType, actor.birthplace)],
    ['Died', actor.deathday],
    ['Nationality', actor.nationality],
    ['Ethnicity', actor.ethnicity],
    ['Career', describeCareer(actor.careerStart, actor.careerEnd)],
    ['Hair', actor.haircolor],
    ['Eyes', actor.eyecolor],
    ['Height', actor.heightCm == null ? null : `${actor.heightCm} cm`],
    ['Bra size', actor.braSize],
    ['Measurements', describeMeasurements(actor.waistSizeCm, actor.hipSizeCm)],
    ['Breast type', actor.breastType],
    ['Tattoos', actor.tattoos],
    ['Piercings', actor.piercings],
  ].filter((entry): entry is [string, string] => Boolean(entry[1]))
  const videoLoad = actor.videoLoad

  return (
    <section className={styles.actorProfile}>
      <CachedArtwork
        path={`/api/artwork/actors/${actor.prdbId}`}
        title={actor.title}
        frameClassName={styles.actorPortrait}
        imageClassName={styles.actorPortraitImage}
        absentClassName={styles.directoryAbsent}
      />
      <div className={styles.actorProfileBody}>
        {facts.length > 0 && (
          <dl className={styles.actorFacts}>
            {facts.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
          </dl>
        )}
        {actor.bios?.map((bio) => <p className={styles.actorBio} key={bio.prdbId}>{bio.text}</p>)}
        {actor.links && actor.links.length > 0 && (
          <nav className={styles.actorLinks} aria-label="Actor links">
            {actor.links.map((link) => (
              <a href={link.url} key={`${link.site}:${link.url}`} rel="noreferrer" target="_blank">{link.site}</a>
            ))}
          </nav>
        )}
        <div className={styles.actorLoad}>
          <button disabled={load.isPending || videoLoad?.active} onClick={() => load.mutate()} type="button">
            {videoLoad?.active ? 'Loading latest Videos…' : load.isPending ? 'Starting…' : 'Load latest 500 Videos'}
          </button>
          {videoLoad?.active && <span>{Number(videoLoad.videosSeen)} of up to 500 read</span>}
          {!videoLoad?.active && videoLoad?.completedAt && (
            <span>Last loaded {new Date(videoLoad.completedAt).toLocaleString()} · {Number(videoLoad.videosSeen)} Videos</span>
          )}
          {load.isError && <span role="alert">{load.error.message}</span>}
        </div>
      </div>
    </section>
  )
}

function describeBirthday(date: string | null, precision: string | null, place: string | null) {
  return [date && precision ? `${date} (${precision})` : date, place].filter(Boolean).join(' · ') || null
}

function describeCareer(start: number | string | null, end: number | string | null) {
  if (start == null && end == null) return null
  return `${start ?? '?'}–${end ?? 'present'}`
}

function describeMeasurements(waist: number | string | null, hips: number | string | null) {
  if (waist == null && hips == null) return null
  return [`Waist ${waist == null ? '?' : `${waist} cm`}`, `Hips ${hips == null ? '?' : `${hips} cm`}`].join(' · ')
}
