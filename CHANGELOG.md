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
