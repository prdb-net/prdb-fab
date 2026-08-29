import type { components } from './schema.d.ts'

// ADR 0036 and ADR 0040: plain fetch against types generated from the committed
// OpenAPI document. No client library — the contract is the document, and a
// response shape that changed without the types being regenerated is a red
// build here and a red build in CI rather than an empty column in the UI.

type Schema = components['schemas']

export type AccessState = Schema['AccessState']
export type OnboardingStep = Schema['OnboardingStep']
export type ChangePasswordVerdict = Schema['ChangePasswordVerdict']
export type SetPasswordVerdict = Schema['SetPasswordVerdict']
export type SignInVerdict = Schema['SignInVerdict']

export type OnboardingOutcome = Schema['OnboardingOutcome']
export type OnboardingVerdict = Schema['OnboardingVerdict']

export type ConnectionsState = Schema['ConnectionsState']
export type PrdbConnectionVerdict = Schema['PrdbConnectionVerdict']
export type SabnzbdCategory = Schema['SabnzbdCategory']
export type SabnzbdCategoriesVerdict = Schema['SabnzbdCategoriesVerdict']
export type SabnzbdConnectionVerdict = Schema['SabnzbdConnectionVerdict']
export type ConfiguredIndexer = Schema['ConfiguredIndexer']
export type IndexerConnectionVerdict = Schema['IndexerConnectionVerdict']
export type LibraryRootVerdict = Schema['LibraryRootVerdict']
export type AfterDownloadGateChoice = Schema['AfterDownloadGateChoice']
export type IdentificationSettingsState = Schema['IdentificationSettingsState']
export type IdentificationSettingsVerdict = Schema['IdentificationSettingsVerdict']
export type StatusState = Schema['StatusState']
export type RunNowVerdict = Schema['RunNowVerdict']

export type VideoCard = Schema['VideoCard']
export type VideoPage = Schema['VideoPage']
export type WantedList = Schema['WantedList']
export type SitePage = Schema['SitePage']
export type SiteVideos = Schema['SiteVideos']
export type ActorPage = Schema['ActorPage']
export type ActorVideos = Schema['ActorVideos']
export type ReleasePage = Schema['ReleasePage']
export type IdentificationState = Schema['IdentificationState']
export type ReleaseDiscoveryRoutine = Schema['ReleaseDiscoveryRoutine']
export type ReleaseDiscoveryRoutineKind = Schema['ReleaseDiscoveryRoutineKind']
export type ReleaseDiscoveryRunNowVerdict = Schema['ReleaseDiscoveryRunNowVerdict']
export type DownloadPreview = Schema['DownloadPreview']
export type DownloadVerdict = Schema['DownloadVerdict']
export type DownloadPage = Schema['DownloadPage']
export type DownloadState = Schema['DownloadState']
export type DownloadSelectionPreview = Schema['DownloadSelectionPreview']
export type DownloadSelectionVerdict = Schema['DownloadSelectionVerdict']
export type DownloadResetPreview = Schema['DownloadResetPreview']
export type DownloadResetVerdict = Schema['DownloadResetVerdict']
export type ArrivingFileReason = Schema['ArrivingFileReason']
export type ReviewQueuePage = Schema['ReviewQueuePage']
export type ReviewQueueEntry = Schema['ReviewQueueEntry']
export type ReviewQueueCount = Schema['ReviewQueueCount']
export type ReviewVideo = Schema['ReviewVideo']
export type ReviewVideoSearchPage = Schema['ReviewVideoSearchPage']
export type ReviewSelectionPreview = Schema['ReviewSelectionPreview']
export type ReviewSelectionVerdict = Schema['ReviewSelectionVerdict']
export type ReviewDecisionVerdict = Schema['ReviewDecisionVerdict']
export type LibraryPage = Schema['LibraryPage']
export type LibraryEntry = Schema['LibraryEntry']
export type LibrarySettingsState = Schema['LibrarySettingsState']
export type OperationLogPage = Schema['OperationLogPage']
export type ReportingSettingsState = Schema['ReportingSettingsState']

/**
 * ADR 0010: an unauthenticated request gets 401 and never a redirect, so this
 * is what the end of a session looks like from here. Its own type, because the
 * one page has to tell it apart from a request that genuinely failed: one sends
 * the viewer back to the sign-in form, the other is an error to show.
 */
export class NotSignedIn extends Error {
  constructor() {
    super('Not signed in.')
    this.name = 'NotSignedIn'
  }
}

async function json<T>(response: Response): Promise<T> {
  if (response.status === 401) {
    throw new NotSignedIn()
  }

  if (!response.ok) {
    // ADR 0040 makes a verdict a 200, so anything else is genuinely a failed
    // request rather than an answer the caller did not like.
    throw new Error(`${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
}

async function post<T>(path: string, body?: unknown): Promise<T> {
  return json<T>(
    await fetch(path, {
      method: 'POST',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  )
}

export async function readAccessState(): Promise<AccessState> {
  return json<AccessState>(await fetch('/api/access/state'))
}

export async function setPassword(password: string): Promise<SetPasswordVerdict> {
  return post<SetPasswordVerdict>('/api/access/password', { password })
}

export async function signIn(password: string): Promise<SignInVerdict> {
  return post<SignInVerdict>('/api/access/sign-in', { password })
}

/**
 * ADR 0010: the current password is asked for again, and every other session
 * ends. ADR 0020 puts the act on the Account route.
 */
export async function changePassword(
  current: string,
  next: string,
): Promise<ChangePasswordVerdict> {
  return post<ChangePasswordVerdict>('/api/access/change-password', { current, next })
}

export async function signOut(): Promise<void> {
  const response = await fetch('/api/access/sign-out', { method: 'POST' })

  if (!response.ok && response.status !== 401) {
    throw new Error(`${response.status} ${response.statusText}`)
  }
}

/**
 * ADR 0010's path: the step is answered, so the marker moves past it. What
 * keeps the two mandatory steps mandatory is the backend reading what is
 * stored, not this call being withheld.
 */
export async function takeOnboardingStep(step: OnboardingStep): Promise<OnboardingVerdict> {
  return post<OnboardingVerdict>('/api/onboarding/take', { step })
}

/** The step is passed by deliberately, and what is left behind is a Gap. */
export async function skipOnboardingStep(step: OnboardingStep): Promise<OnboardingVerdict> {
  return post<OnboardingVerdict>('/api/onboarding/skip', { step })
}

export async function readConnections(): Promise<ConnectionsState> {
  return json<ConnectionsState>(await fetch('/api/connections'))
}

export async function savePrdbKey(
  apiKey: string,
  confirmAnotherAccount: boolean,
): Promise<PrdbConnectionVerdict> {
  return post<PrdbConnectionVerdict>('/api/connections/prdb', { apiKey, confirmAnotherAccount })
}

/**
 * A read that carries a credential, which is why it is a POST: a key has no
 * business in an address bar or in anybody's access log.
 */
export async function readSabnzbdCategories(
  url: string,
  apiKey: string,
): Promise<SabnzbdCategoriesVerdict> {
  return post<SabnzbdCategoriesVerdict>('/api/connections/sabnzbd/categories', { url, apiKey })
}

export async function saveSabnzbd(connection: {
  url: string
  apiKey: string
  category: string
  downloadDirectory: string
}): Promise<SabnzbdConnectionVerdict> {
  return post<SabnzbdConnectionVerdict>('/api/connections/sabnzbd', connection)
}

export async function listIndexers(): Promise<ConfiguredIndexer[]> {
  return json<ConfiguredIndexer[]>(await fetch('/api/connections/indexers'))
}

export async function addIndexer(indexer: {
  name: string
  url: string
  apiKey: string
}): Promise<IndexerConnectionVerdict> {
  return post<IndexerConnectionVerdict>('/api/connections/indexers', indexer)
}

/** ADR 0020's indexer route: the same check, run again over a row that is there. */
export async function editIndexer(
  id: string,
  indexer: { name: string; url: string; apiKey: string },
): Promise<IndexerConnectionVerdict> {
  return post<IndexerConnectionVerdict>(`/api/connections/indexers/${segment(id)}`, indexer)
}

export async function saveLibraryRoot(path: string): Promise<LibraryRootVerdict> {
  return post<LibraryRootVerdict>('/api/connections/library-root', { path })
}

export async function readIdentificationSettings(): Promise<IdentificationSettingsState> {
  return json<IdentificationSettingsState>(await fetch('/api/settings/identification'))
}

export async function saveIdentificationSettings(
  afterDownload: AfterDownloadGateChoice,
): Promise<IdentificationSettingsVerdict> {
  return post<IdentificationSettingsVerdict>('/api/settings/identification', { afterDownload })
}

export async function readStatus(): Promise<StatusState> {
  return json<StatusState>(await fetch('/api/status'))
}

export async function runRoutineNow(name: string, target: string | null): Promise<RunNowVerdict> {
  return post<RunNowVerdict>('/api/status/run-now', { name, target })
}

export async function readReportingSettings(): Promise<ReportingSettingsState> {
  return json<ReportingSettingsState>(await fetch('/api/settings/reporting'))
}

export async function saveReportingSettings(
  reportFulfilments: boolean,
  reportConfirmedAssignments: boolean,
): Promise<ReportingSettingsState> {
  return post<ReportingSettingsState>('/api/settings/reporting', {
    reportFulfilments,
    reportConfirmedAssignments,
  })
}

/**
 * ADR 0013's What's New, read out of the catalogue. Nothing here reaches prdb:
 * the page is a query over what the sync routines have already written, which
 * is why a reload spends no request (ADR 0018).
 */
export async function listWhatsNew(page: number): Promise<VideoPage> {
  return json<VideoPage>(await fetch(`/api/catalogue/whats-new?page=${page}`))
}

/**
 * ADR 0007's only source of intent, read out of the catalogue. There is no
 * call beside this one that writes to it: wanting happens in prdb.
 */
export async function listWanted(page: number): Promise<WantedList> {
  return json<WantedList>(await fetch(`/api/catalogue/wanted?page=${page}`))
}

export async function listSites(search: string, page: number): Promise<SitePage> {
  return json<SitePage>(
    await fetch(`/api/catalogue/sites?${parameters({ search, page: String(page) })}`),
  )
}

export async function readSite(
  prdbId: string,
  search: string,
  page: number,
): Promise<SiteVideos> {
  return json<SiteVideos>(
    await fetch(`/api/catalogue/sites/${segment(prdbId)}?${parameters({ search, page: String(page) })}`),
  )
}

export async function listActors(search: string, page: number): Promise<ActorPage> {
  return json<ActorPage>(
    await fetch(`/api/catalogue/actors?${parameters({ search, page: String(page) })}`),
  )
}

export async function readActor(
  prdbId: string,
  search: string,
  page: number,
): Promise<ActorVideos> {
  return json<ActorVideos>(
    await fetch(`/api/catalogue/actors/${segment(prdbId)}?${parameters({ search, page: String(page) })}`),
  )
}

export async function listReleases(filters: {
  video?: string
  site?: string
  actor?: string
  state?: IdentificationState
  indexer?: string
  page: number
}): Promise<ReleasePage> {
  return json<ReleasePage>(
    await fetch(
      `/api/releases?${parameters({
        video: filters.video,
        site: filters.site,
        actor: filters.actor,
        state: filters.state,
        indexer: filters.indexer,
        page: String(filters.page),
      })}`,
    ),
  )
}

/** Reading these local schedule rows never starts discovery work. */
export async function readReleaseDiscoveryRoutines(): Promise<ReleaseDiscoveryRoutine[]> {
  return json<ReleaseDiscoveryRoutine[]>(await fetch('/api/releases/discovery-routines'))
}

/**
 * Makes the existing schedule row due. The lane still owns execution and all
 * of its ordinary Governor and query-budget limits.
 */
export async function runReleaseDiscoveryRoutine(
  routine: Pick<ReleaseDiscoveryRoutine, 'kind' | 'target'>,
): Promise<ReleaseDiscoveryRunNowVerdict> {
  return post<ReleaseDiscoveryRunNowVerdict>('/api/releases/discovery-routines/run-now', routine)
}

export async function previewReleaseDownload(
  releaseId: number | string,
  videoId: string,
): Promise<DownloadPreview> {
  return post<DownloadPreview>(`/api/releases/${segment(releaseId)}/download/preview`, { videoId })
}

export async function downloadRelease(
  releaseId: number | string,
  videoId: string,
  downloadId: string,
): Promise<DownloadVerdict> {
  return post<DownloadVerdict>(`/api/releases/${segment(releaseId)}/download`, { videoId, downloadId })
}

export async function listDownloads(filters: {
  state?: DownloadState
  indexer?: string
  page: number
}): Promise<DownloadPage> {
  return json<DownloadPage>(
    await fetch(
      `/api/downloads?${parameters({
        state: filters.state,
        indexer: filters.indexer,
        page: String(filters.page),
      })}`,
    ),
  )
}

export async function previewStopFollowing(
  downloadIds: string[],
): Promise<DownloadSelectionPreview> {
  return post<DownloadSelectionPreview>('/api/downloads/stop-following/preview', { downloadIds })
}

export async function stopFollowing(downloadIds: string[]): Promise<DownloadSelectionVerdict> {
  return post<DownloadSelectionVerdict>('/api/downloads/stop-following', { downloadIds })
}

export async function previewResetDownloads(videoId: string): Promise<DownloadResetPreview> {
  return post<DownloadResetPreview>(`/api/releases/video/${segment(videoId)}/reset-downloads/preview`)
}

export async function resetDownloads(
  videoId: string,
  downloadIds: string[],
): Promise<DownloadResetVerdict> {
  return post<DownloadResetVerdict>(`/api/releases/video/${segment(videoId)}/reset-downloads`, {
    downloadIds,
  })
}

export async function readReviewQueue(filters: {
  reason?: ArrivingFileReason
  download?: string
  page: number
}): Promise<ReviewQueuePage> {
  return json<ReviewQueuePage>(
    await fetch(`/api/review-queue?${parameters({
      reason: filters.reason,
      download: filters.download,
      page: String(filters.page),
    })}`),
  )
}

export async function readReviewQueueCount(): Promise<ReviewQueueCount> {
  return json<ReviewQueueCount>(await fetch('/api/review-queue/count'))
}

export async function searchReviewVideos(
  search: string,
  site?: string,
  page = 1,
): Promise<ReviewVideoSearchPage> {
  return json<ReviewVideoSearchPage>(
    await fetch(`/api/review-queue/videos?${parameters({ search, site, page: String(page) })}`),
  )
}

export async function previewReviewDelete(ids: string[]): Promise<ReviewSelectionPreview> {
  return post<ReviewSelectionPreview>('/api/review-queue/delete/preview', { arrivingFileIds: ids })
}

export async function deleteReviewEntries(ids: string[]): Promise<ReviewSelectionVerdict> {
  return post<ReviewSelectionVerdict>('/api/review-queue/delete', { arrivingFileIds: ids })
}

export async function dismissReviewEntries(ids: string[]): Promise<ReviewSelectionVerdict> {
  return post<ReviewSelectionVerdict>('/api/review-queue/dismiss', { arrivingFileIds: ids })
}

export async function fileReviewAs(id: string, videoId: string): Promise<ReviewDecisionVerdict> {
  return post<ReviewDecisionVerdict>(`/api/review-queue/${segment(id)}/file-as`, { videoId })
}

export async function replaceFromReview(id: string): Promise<ReviewDecisionVerdict> {
  return post<ReviewDecisionVerdict>(`/api/review-queue/${segment(id)}/replace`)
}

export async function fileOnlyCopyFromReview(id: string): Promise<ReviewDecisionVerdict> {
  return post<ReviewDecisionVerdict>(`/api/review-queue/${segment(id)}/file-as-only-copy`)
}

export async function readLibrary(filters: {
  search?: string
  site?: string
  actor?: string
  quality?: string
  page: number
}): Promise<LibraryPage> {
  return json<LibraryPage>(
    await fetch(`/api/library?${parameters({
      search: filters.search,
      site: filters.site,
      actor: filters.actor,
      quality: filters.quality,
      page: String(filters.page),
    })}`),
  )
}

export async function readLibraryEntry(videoId: string): Promise<LibraryEntry> {
  return json<LibraryEntry>(await fetch(`/api/library/${segment(videoId)}`))
}

export async function readOperationLog(filters: {
  act?: string
  search?: string
  page: number
}): Promise<OperationLogPage> {
  return json<OperationLogPage>(
    await fetch(`/api/operation-log?${parameters({
      act: filters.act,
      search: filters.search,
      page: String(filters.page),
    })}`),
  )
}

export async function readLibrarySettings(): Promise<LibrarySettingsState> {
  return json<LibrarySettingsState>(await fetch('/api/settings/library'))
}

export async function saveLibrarySettings(deleteLeftovers: boolean): Promise<LibrarySettingsState> {
  return post<LibrarySettingsState>('/api/settings/library', { deleteLeftovers })
}

/**
 * One value as a path segment. The ids are GUIDs today, so nothing is broken
 * without this — but several of them arrive from `useParams`, which is the
 * address bar, and the query parameters beside them already go through
 * `parameters()`. An address is built the same way wherever it is built.
 */
function segment(value: string | number): string {
  return encodeURIComponent(String(value))
}

function parameters(values: Record<string, string | undefined>): URLSearchParams {
  const answer = new URLSearchParams()

  for (const [name, value] of Object.entries(values)) {
    if (value) answer.set(name, value)
  }

  return answer
}
