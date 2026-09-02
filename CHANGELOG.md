# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Entries describe what changed for someone *using* the tool. A refactor that
moves a thousand lines but changes no behaviour is not worth an entry; a renamed
setting or a different default is, however small the diff.

The version names the user-facing contract — the settings, the API the browser
is built against, the export format, the layout written into a library — and not
the database schema, which is migrated forward at startup and which nobody acts
on. Until the first release the version is `0.x`, and a minor bump may break
things; see SemVer's clause on initial development.

**Migrations only go forward.** Once a newer version has started against a data
directory, an older image finds a database it does not understand. Copy `/data`
before changing the tag — the backup file is deliberately not the whole of it.

## [Unreleased]

## [0.14.0] - 2026-09-02

This release makes acquisition choices and Download history easier to scan,
especially when a Video is already in the Library.

### Changed

- Catalogue cards across What's New, Search and Wanted now use the same compact,
  context-aware actions. Site and Actor browsing use the same visual language,
  while Sites show held Video counts and can be filtered to those represented in
  the Library.
- Release pages show held qualities before search and Download actions, describe
  consumed Releases as already used, and distinguish downloading another version
  from acquiring a Video that is not yet held.
- Downloads are presented as responsive cards with prominent states, failure
  messages and essential metadata. SABnzbd job IDs and the formatted stage-log
  JSON stay collapsed until their details control is opened.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.14.0 has started against it.

## [0.13.0] - 2026-08-30

This release makes the Catalogue's everyday decisions faster to reach and
clearer to scan, from finding a useful Video through starting its Download.

### Fixed

- `Open in prdb` links now use the application host at `app.prdb.net`.

### Changed

- Catalogue Search now defaults to Videos that are neither held nor being
  downloaded, newest Release first. The page exposes local acquisition-state
  filters and Release date, prdb recency, title and query-relevance sorts, all
  preserved in its URL.
- Manual Download buttons submit an eligible Release directly after the local
  plan check instead of asking for a second browser confirmation.
- Wanted cards now prioritize their Release action, expose removal as a compact
  state control, and collect less frequent external actions in an accessible
  overflow menu.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.13.0 has started against it.

## [0.12.1] - 2026-08-30

### Fixed

- Background prdb work held behind a reserved share now asks again when the
  reported hourly reset elapses. It no longer reuses the same stale remaining
  count for up to a full hour while newly available request slots go unused.

## [0.12.0] - 2026-08-30

This release makes recent availability a background guarantee instead of a
side effect of opening a Release page or starting a Manual Search.

### Added

- **A rolling 90-day Recent Window.** prdb Catalogue details and every enabled
  Indexer's recent Releases are filled in the background, resumed after a
  restart and completely proved again at least daily. Late Indexer visibility
  and temporary outages are therefore repaired by a later pass.
- **Observable coverage.** Status names incomplete or stale prdb and per-Indexer
  coverage as Gaps, while Release pages distinguish a prepared result from an
  initial window that is still filling.

### Changed

- Every Release inside the Recent Window is submitted to prdb Identification
  without depending on local Screening, Wanted state, pinning, Manual Search or
  a page visit. Recent answers and Catalogue details become due for revalidation
  after about 23 hours; prdb remains the only Identification authority.
- Recent Catalogue and Release rows are protected from count-based eviction.
  Older rows retain the existing pin and bounded-cache rules, so a cache may
  exceed its nominal ceiling when the current source volume requires it.
- Updated `Prdb.Sdk` to 0.13.0. Local development can now point the composed
  application at an HTTP loopback prdb stand-in through `Prdb:BaseUrl`; the SDK
  continues to require HTTPS for every non-loopback authenticated origin.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.12.0 has started against it.

## [0.11.0] - 2026-08-30

This release makes the primary manual use case explicit: choose a known Video,
ask enabled Indexers for it, inspect Identification, and submit an eligible
Release without hunting through operational controls.

### Added

- **A first-class Search destination.** Local title search leads directly from
  any Catalogue Video to its Release workspace; Video cards now name the
  Indexer-search action while Site and Actor actions truthfully name cached
  Release views.
- **Durable Manual Search.** A person can search all enabled Indexers or one
  selected Indexer for a Video. The request is stored before the response and
  the ordinary sync scheduler performs one bounded title query per Indexer
  within unreserved Daily Query Budget. Progress, deferral, failures, retries
  and Identification counts remain visible after refresh or restart.
- **Release-level manual Download choices.** Eligible Releases expose their
  existing preview-and-confirm Download action; every ineligible row now says
  why it cannot be submitted or links to the Video it was identified as.

### Changed

- A Manual Search result is provenance, not Identification evidence. New rows
  enter `Awaiting`, settled rows stay settled, and only prdb may attach a Video,
  Confidence and `matchedBy`.
- Recent Manual Search records are disposable, retained for seven days and
  excluded from Backup. They pin their Video and returned Releases only while
  retained.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.11.0 has started against it.

## [0.10.0] - 2026-08-29

This release turns the local catalogue into the front of the acquisition loop:
account preferences can be changed without leaving fab, and What's New exposes
the decisions that were previously hidden behind Release tables.

### Added

- **Wanted and Favourite actions backed by prdb.** Video, Actor and Site
  preferences are written through the governed SDK connection, projected
  locally after success, and expose retryable inline failures without sending
  the account key to the browser.
- **Favourite-first visual Actor and Site directories.** Both default to the
  favourite scope, preserve scope, search and page in the URL, and order by
  local Video count. Actor profile artwork uses fab's bounded cache; Sites use
  a clearly representative Video preview while canonical Site artwork remains
  an upstream API request.
- **A decision-ready What's New.** Cards show Wanted, outstanding Download,
  held Quality and Release availability, with Wanted, one-click best Release,
  Release inspection and Site actions. A server-side checkpoint reports what
  is new since the previous loaded visit across browsers.

### Changed

- A manual Video Download now durably records Wanted intent and its SABnzbd
  reservation before either account or Download submission can be written
  remotely. Pending work resumes after restart; an uncertain submission is
  never repeated blindly, and stale feed data cannot erase pending intent.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.10.0 has started against it.

## [0.9.0] - 2026-08-29

This release completes Wanted automation. Unordered permission rules can now
run the existing ranked Download path unattended while the same local safety
facts remain authoritative and inspectable.

### Added

- **Automation Rules over Wanted Videos.** Each rule allows one or more enabled
  Indexers and optional minimum and maximum Release sizes. No enabled rule is
  the off state. Enabling or changing a rule schedules a catch-up over cached
  matches, bounded by a configurable unfinished automatic Download cap that
  defaults to 20.
- **An active before-download Identification gate.** Exact, Strong and Probable
  remain the default named set; stricter fixed sets can be selected beside the
  existing after-download gate. A change queues reconsideration and never turns
  the settings request itself into a SABnzbd submission.
- **Durable automatic decisions and Origins.** The Release view and Status
  explain gates, size, disallowed Indexers, held Videos, Review Queue entries,
  the automatic cap, Retry Budget and exhausted Releases. Downloads and
  Operation Log entries show Person or Automation, including every permitting
  rule; copied rule names survive rule deletion.

### Changed

- Newly identified Releases, Videos newly entering Wanted, and rule or gate
  catch-up all feed one bounded background Decide work set. Automatic
  submission reuses the manual reservation, NZB retrieval, SABnzbd category
  validation, retry budget and idempotency path.
- Removing a Video from Wanted marks its unfinished automatic Download
  `Abandoned`, stops following it and prevents a retry. It never pauses, retries
  or deletes the SABnzbd job, and it leaves anything already filed untouched.
- A Video already held in the Library in any Quality is never automatically
  upgraded in this first release. Favourite Sites, favourite Actors and Quality
  parsed from Release names are not automation inputs.

### Documentation

- Added an operator-facing description of catch-up, every automatic safety
  bound, forward-only rule changes, durable Origin and the exact Wanted-removal
  boundary.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.9.0 has started against it.

## [0.8.0] - 2026-08-29

This release finishes the Reporting and Status slice. It adds separately
opted-in reporting to prdb and makes the complete local loop inspectable without
turning observation into more remote work.

### Added

- **Status follows the six-stage loop.** Sync from prdb, Sync from Indexers,
  Match, Decide, Download and File show their routine facts, work-set progress,
  seven-day named gate tallies, budgets and local liveness. Only repairable Gaps
  count in the headline; deliberate Brakes explain their choice and route to
  its owner. Cleared routine Gaps remain visible while their failed run remains
  in retained history.
- **Run now stays inside the scheduler.** Requests are accepted, deferred or
  refused visibly and cannot overlap. They never bypass the prdb Governor, an
  Indexer's Daily Query Budget, a permanent refusal or an empty work set.
- **Two independent Reporting channels.** Both default off. Fulfilments report
  the held state and truthfully rounded-down quality of locally held Wanted
  Videos; Confirmed Assignments report only exact file-to-Video decisions made
  by a person in the Review Queue. Delivery is governed, bounded, account-scoped
  and idempotent.
### Documentation

- Added a precise outbound-data and Reporting description, and updated the
  operator guide to distinguish Gaps from Brakes and the complete manual loop
  from Download automation, which remains unavailable.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.8.0 has started against it.

## [0.7.1] - 2026-08-29

This patch release repairs NZB fetching against a real Newznab compatibility
case and makes the complete path from a wanted Video to a confirmed Download
visible without hunting through cached results or operational controls.

### Changed

- **A download-ready path is now visible from the catalogue to submission.**
  Video cards mark Releases that pass the real ranking and retry-budget rules,
  their action says `Download`, and the best eligible Release has a prominent
  action at the top of the Video's Release page. Cached-result and empty states
  now explain what background discovery has (and has not) found, while manual
  discovery controls sit in a secondary disclosure.

### Fixed

- **An HTTPS Indexer can now supply an HTTP NZB enclosure without blocking the
  Download.** Some Newznab services redirect that enclosure to the same host
  over HTTPS. prdb-fab now upgrades the first request itself, keeping the API
  key off plaintext HTTP while continuing to refuse redirects and HTTP
  enclosures on a different host.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.7.1 has started against it.

## [0.7.0] - 2026-08-29

This release tightens the desktop-first workspace after an end-to-end UI and
repository review. Loading, navigation, tables and actions now remain legible
across desktop and mobile, while several unattended failure modes are contained
instead of stopping work or consuming unbounded resources.

### Added

- **Visible loading states throughout the workspace.** A waking or slow host no
  longer presents a blank document while access state or page data is being
  read.
- **Responsive table alternatives and mobile-safe actions.** Dense operational
  views expose their important facts without horizontal page overflow, and
  touch targets remain usable at narrow widths.

### Changed

- Release and detail links preserve the page and filter context they came from,
  so returning to a catalogue or library view no longer starts that search
  again.
- Artwork is fetched only as cards approach the viewport and stale artwork URLs
  are invalidated when their catalogue revision changes.
- Password changes share the installation-wide password-attempt limit with
  sign-in. Too many wrong current-password attempts pause further checks for up
  to five minutes instead of repeatedly running the password hash.

### Removed

- The development-only Walking Skeleton page and API have been retired. Its
  sample database table and scheduled routine are removed by the forward
  migration; they never held application data.

### Fixed

- **A site or title beginning with a dot no longer files into a hidden
  directory.** Names are stripped of leading dots so that a directory is not
  invisible to a media server's scanner, but the stripping ran once — so a name
  like `. .Example` lost its first dot, then the space behind it, and arrived
  on disk as `.Example` after all. A site that sanitised to nothing but a dot
  was worse: the site level vanished from the path entirely and the entry was
  filed directly under the library root. Names are now trimmed until nothing is
  left to trim. Entries already filed keep the paths they have; ADR 0017 records
  a filed path rather than recomputing it.
- **A lane no longer stops for good when something around a routine fails.** An
  exception from a routine was already that run's failure, but one from the work
  around it — reading what is due, recording what happened, opening a database
  connection — ended the lane's loop instead. The lane was then gone until the
  container restarted, with no failure, no Gap and nothing on any page saying
  so. Such a turn is now logged and the lane keeps turning.
- **A video whose container claims an impossible duration no longer jams
  filing.** ffprobe reports what the file claims, and a corrupt one can claim a
  number no counter holds; reading it threw, which failed the filing routine on
  every attempt and left the file stuck in front of everything behind it. The
  duration is now read as unknown, which is what it is.
- **An indexer answer larger than 32 MB is refused rather than buffered.** The
  indexer walk runs unattended, and an answer that size is not the thing being
  asked for. Artwork already had this ceiling; searches, capabilities and NZB
  downloads now have it too.
- **A page number past the end shows an empty page.** A number large enough to
  overflow the offset silently answered with the first page while reporting the
  number that had been asked for.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.7.0 has started against it.

## [0.6.0] - 2026-08-29

Release discovery can now be inspected and prompted from the browser, and the
application navigation has been reorganised for a desktop workspace without
turning the mobile view into an overflowing version of it.

### Added

- **Release discovery controls on every Releases view.** The page shows the
  last completion or failure of each enabled Indexer's Wanted Sweep and the
  global Screening, Backwards Screening and Release Identification routines.
  *Run now* makes one routine due immediately while preserving its lane, the
  Governor and every Indexer query-budget limit.
- **A responsive application shell.** Desktop uses a persistent sidebar grouped
  into Discover and Fetch & build. Mobile uses direct destinations for What's
  New, Wanted, Downloads and Library plus a complete More sheet for Sites,
  Actors, Review Queue, Operation Log and Settings.

### Changed

- The active destination is visible throughout the application, Settings stays
  at the stable lower edge of the desktop sidebar, and the Review Queue count
  remains present in both layouts without giving an empty queue warning weight.
- The mobile navigation sheet traps keyboard focus, closes with Escape or its
  backdrop, and prevents the page behind it from scrolling while it is open.

### Fixed

- A Completed Download on the Releases view no longer says that Filing is
  absent; it now correctly says that the Download is waiting for collection.

### Operational boundary

- *Run now* schedules existing work; it never runs a routine inside the browser
  request and never bypasses pacing or an Indexer's Daily Query Budget.
- SABnzbd retry and delete remain under the person's control. Download
  automation and fulfilment reporting remain absent.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.6.0 has started against it.

## [0.5.0] - 2026-08-29

Completed Downloads now cross the filesystem boundary into a visible,
repairable Library. Every unsafe outcome stops in a Review Queue rather than
being hidden or guessed through.

### Added

- **Collection and resumable Filing.** Supported Video Files are found
  recursively, probed once, identified through prdb, and filed into the
  Jellyfin Movies layout. Same-filesystem moves rename; cross-filesystem moves
  copy to a hidden destination-side temporary file, flush, compare every byte,
  rename, and only then delete the source.
- **Review Queue with exactly two universal exits.** Dismiss leaves a file
  untouched and Delete rechecks its path and size. Unidentified, Duplicate and
  Entry Missing additionally offer only File as, Replace and File as only copy.
  A File as choice records a durable Confirmed Assignment; Replace is performed
  and resumed by the serial File lane.
- **Library and Library Entry surfaces.** The held-only grid groups every
  Quality of a Video into one card and filters by title, Site, Actor and
  Quality. An entry shows recorded paths, file probe facts, prdb's non-deciding
  Consensus Runtime and its own operation history.
- **Operation Log**, a newest-first, paged audit surface for Filed, Replaced,
  Deleted and Tidied acts, searchable by path or file name.
- **Library settings.** The Library root can be checked again and fixed
  leftover deletion can be switched off without a restart.

### Changed

- Catalogue repair now refreshes changed `movie.nfo` and chosen cached
  `fanart.jpg` for held entries, without recomputing or renaming a recorded
  Video File path.
- Leftover deletion is enabled by default for both new and upgraded
  installations. It removes only `.nfo`, `.par2`, `.sfv`, `.srr`, `.url`,
  `.txt`, `.jpg` and `.png` from directory-shaped storage after every Video
  File has reached a decision; unknown files and single-file parents remain.

### Operational boundary

- prdb-fab owns the Library paths it records and the sidecar and image beside
  them. It does not scan arbitrary Library directories or rename old paths when
  catalogue metadata changes.
- SABnzbd is still written only through the initial `addfile`; retry and delete
  remain a person's responsibility. Automation and fulfilment reporting remain
  absent.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.5.0 has started against it.

## [0.4.0] - 2026-08-28

Manual acquisition now reaches SABnzbd and remains visible after the click.
The tool records each attempt before submission, follows the job, and advances
through the ranked Releases within a three-attempt budget.

### Added

- **Confirmed Release submission.** An eligible Release on a Video can fetch
  its NZB and submit it to the configured SABnzbd category. The local Download
  reservation is durable before the remote write, and a lost answer is
  recovered only by one exact submitted-name match rather than by blindly
  submitting the same NZB again.
- **SABnzbd following every five seconds.** The poll asks the queue first and
  history only for missing job ids. Request failures preserve an unknown state;
  three consecutive successful absences report a likely deleted job. SABnzbd's
  status, failure message and stage log remain visible without translated text
  being used as control flow.
- **Automatic ranked retries with a three-Download budget per Video.** Rejected,
  failed, unusable, vanished, abandoned and empty Downloads all consume their
  Release and one attempt, then select the next ranked unconsumed Release. A
  spent budget and no eligible Releases left are shown as different outcomes.
- **Downloads**, a local-only, newest-first table with State and Indexer filters,
  SABnzbd evidence, origin and outstanding-since time. Its only action is a
  confirmed multi-selection *Stop following*.
- **Per-Video acquisition history on Releases.** The page shows attempts used,
  consumed Releases, the next ranked choice, and a confirmed reset that deletes
  only that Video's local Download history.

### Operational boundary

- **The only SABnzbd write is the initial `addfile`.** prdb-fab never calls
  SABnzbd retry or delete. Stopping following, resetting local history and every
  automatic failure decision leave SABnzbd's queue and history untouched.
- **Installation trouble spends nothing.** An unreachable or globally paused
  SABnzbd raises a Gap; it is not treated as a broken Release. A failed poll
  neither increments nor resets absence evidence.
- **Completed files are not collected in this version.** They remain in
  SABnzbd's configured Download Directory. Filing and any move into the library
  root arrive in a later version.

**Before updating:** copy `/data`. Migrations only go forward, so an older image
cannot use a data directory after 0.4.0 has started against it.

## [0.3.0] - 2026-08-28

Release discovery is now visible. The tool continuously extends a bounded
cache from each enabled Indexer, asks prdb what screened Release names belong
to, and presents the answers without downloading anything.

### Added

- **Release discovery from all four catalogue surfaces.** What's New, Wanted,
  Sites and Actors now open one shared Releases table for a selected Video,
  Site or Actor. Its context, filters and page are in the address, and every
  read comes from the local Catalogue and Indexer Cache rather than causing a
  browser request to contact an Indexer.
- **Sites and Actors as browse surfaces.** Both directories are locally
  searchable, lead to the shared artwork grid for their Videos, and can open
  Release discovery for the whole selection or for one Video.
- **The full Identification answer beside every cached Release:** Indexer,
  size, first seen, Identification State, Confidence and `matchedBy`. An
  ambiguous answer lists its Candidates without choosing one; a Site-Only Match
  names its Site and explicitly names no Video.
- **A continuously extended, bounded Indexer Cache.** Each enabled Indexer gets
  a resumable 90-day first walk, a recurring walk for what appears next, and a
  ceiling of 100,000 Releases. Examined rows nothing points at are evicted
  oldest first; unseen and still-wanted Releases are protected.
- **The Wanted Sweep**, which searches each Indexer directly for older Wanted
  Videos that cannot appear in the newest feed. It shares each Indexer's Daily
  Query Budget without letting the walk consume its reserved portion, and its
  search result is only a reason to ask prdb — never evidence of identity.

### Changed

- **Release discovery is the exact boundary of this version.** The UI states
  where the action would otherwise be that no NZB is fetched and nothing is
  written to SABnzbd. Downloads and filing remain absent rather than being
  implied by a row that can now be found and identified.

## [0.2.1] - 2026-08-27

### Fixed

- **Nothing was being read from prdb except the newest videos and the site
  list.** All five change feeds — the wanted list, both favourites, artwork and
  actors — failed on their very first request, so a fresh installation showed an
  empty wanted list and never learned about a video acquiring artwork. prdb
  requires a `since` on every feed request and refuses one without it; this
  tool expressed *start from the beginning* by sending no `since` at all, which
  is what prdb's own API document says is allowed. It now sends the beginning of
  time, which excludes nothing and is accepted. Feeds that had already been
  failing recover on their next run, with nothing to reset and nothing lost.

## [0.2.0] - 2026-08-27

The tool now knows what prdb knows. Setting up ends on your wanted list instead
of on a page apologising for not having one, and there are two things to look
at. Still nothing is searched for, downloaded or filed — that is the next
release, and this one is what it will be built on.

### Added

- **A local copy of the part of prdb you point it at**, kept up to date on its
  own: the videos prdb publishes, the sites and actors behind them, your wanted
  list and your favourites. It starts the minute a key is saved, reads backwards
  into what prdb published before your installation existed, and then keeps up
  with what is new. Nothing waits for it and there is no progress bar to sit in
  front of.
- **What's new**, which is where the tool now lands: prdb's newest videos as a
  grid, in prdb's own order. Where you are in it is in the address, so a page can
  be linked and a reload comes back to it.
- **Your wanted list**, as the same grid over what you have marked in prdb, most
  recently wanted first, with a link out to prdb on every card. Wanting happens
  in prdb: this reads that list and never writes to it. An empty list says which
  kind of empty it is — nothing marked, or not read yet.
- **Artwork**, one picture per video, kept on disk so that scrolling a grid does
  not fetch the same thumbnails again. Pictures for videos you want are fetched
  ahead of you looking; everything else is fetched the first time it is on
  screen. Bounded at 2 GiB, and what you want is never dropped to hold that.
- **It paces itself against your prdb plan**, read off prdb's own answers rather
  than configured. A plan too small for the schedule is said out loud in the log
  and answered by asking for less — the actors feed drops to daily, the images
  feed and what's new to hourly — rather than by quietly falling behind.

### Changed

- **Setting up ends on your wanted list**, with the first read of prdb already
  running behind it. The placeholder page that stood there is gone, along with
  its address.
- **A prdb key belonging to a different account replaces what belongs to the old
  one** — the wanted list, the favourites, and where their feeds had got to —
  and keeps the catalogue, which belongs to no account. You are asked before it
  happens, and told what will go.

### Fixed

- **The library root no longer warns about a copy that would be a rename.** The
  check asked which *mount* each path was reached through rather than which
  filesystem it is on, so a container given its downloads and its library as two
  bind mounts of one filesystem — the arrangement `docs/running-in-docker.md`
  recommends — was told that filing would copy every video and delete the
  original. It now compares the device the kernel reports, and the warning
  appears only when the two really are on different filesystems.

## [0.1.0] - 2026-08-27

The first release, and deliberately a small one: you can set a password,
configure the tool, and correct what you configured. The loop does not run —
nothing is synced, searched, downloaded, identified or filed, and a finished
setup is a tool that is ready and idle. What this release is for is the setup
path and the connections, tried against the real services on real hardware.

New, and the whole of what a user sees:

- **A password**, set at the first run and never shipped as a default. One
  field, no user name, and the form that sets it is offered only while no
  password exists — so whoever reaches the tool first claims it. A sign-in is
  rate-limited, and the session behind it survives a restart and can be ended.
  Over plain `http` the password travels in the clear, which
  `docs/running-in-docker.md` says plainly rather than papering over with cookie
  flags.
- **A guided setup** that asks for the prdb API key, SABnzbd, the indexers and
  the library root, in that order. Each step is stored the moment it is
  answered, so closing the tab costs nothing and a restarted container resumes
  on the same step. Setting up finishes and does not come back.
- **Every connection is checked against the real service before it is
  accepted**, and one that fails is not stored — no *continue anyway*. prdb
  answers with four different verdicts rather than one message; SABnzbd is
  checked with a call that actually carries the key; an indexer is checked with
  a real search, because most of them answer a capabilities call to anybody. The
  path from SABnzbd's finished folder into this container is verified rather
  than collected.
- **SABnzbd and the indexers can be skipped**, with what that costs said at the
  moment you skip it — and what is missing is recorded rather than forgotten.
  The prdb key and the library root cannot be skipped.
- **Settings**, at `/settings`: the same forms again, wrapped in *save*, with a
  route per connection and one per indexer. Keys are write-only — the field is
  empty with a marker saying one is set, and saving it empty keeps the stored
  key. Nothing needs a restart.
- **Changing the password** ends every other session at once, and asks for the
  current one to do it.
- **`FAB_RESET_PASSWORD=true`** clears the password and every session on the
  next start and leaves every other credential standing, for when the password
  is lost and the browser cannot help. It has to be removed again afterwards,
  and the log says so.

Underneath it, and what the rest is being built on:

- **Four projects and one reference direction**, with `Core` holding the rules
  and reaching nothing. Five architecture tests fail the build on a violation
  that would otherwise compile and run: a dependency in `Core`, a library
  depending on the host, `Core` touching the filesystem, anything reading the
  clock without `TimeProvider`, and a whole URL in a log message.
- **SQLite opened in WAL**, with `synchronous`, `busy_timeout` and
  `foreign_keys` set on every connection rather than once at startup, because
  the pool hands a connection back in whatever state it was left in.
- **A schedule**: a routine row that is the only truth about what is due, one
  worker per lane, a run log capped at fifty runs per routine, and three
  outcomes — succeeded, failed, and interrupted by a restart. A tick that found
  nothing to do is not a run and is not recorded.
- **Logging to two places at once**: the container's output and a rolling file
  under `/data/logs/`, capped near a hundred megabytes, so that sending a log is
  copying a file. `Logging__LogLevel__Prdb.Fab=Debug` turns the tool's own
  reasoning on. The first line names the version that produced the log.
- **An API the frontend's types are generated from**, committed, with CI failing
  when the two drift apart.
- **An image for linux/amd64 and linux/arm64** that drops to `PUID:PGID`, leaves
  the rest of the media alone, and stops when it is asked to. CI starts it on
  both architectures rather than only building it.
