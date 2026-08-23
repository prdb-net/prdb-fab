# Artwork is cached by image id, fetched for what is pinned and on sight for the rest

One image per video — the same one
[ADR 0027](0027-the-sidecar-and-the-entry-image-are-overwritten-until-they-match-the-catalogue.md)
picks — stored under the image's own id. What is **pinned** is fetched by a
routine, because the library grid and filing both need it to be there already.
What is not is fetched **when a grid asks for it**, because the four other
browse surfaces range over a catalogue nobody scrolls all of.

The unpinned half is bounded by bytes and evicted; the pinned half is neither.
Nothing here passes the governor, and nothing here is in the backup.

## One image, and it is the one ADR 0027 chose

`VideoDetailDto.images` is an array. The cache holds **the first entry carrying
a non-null `url`**, and no other.

That is not a fresh choice — ADR 0027 made it for the entry image, on the ground
that prdb documents the array's order as stable but expressly **not** a ranking,
so the oldest is chosen because two runs choose the same one. Caching a
different image than the one filing writes would mean the grid and the library
disagreed about what a video looks like, and caching the whole array would
multiply the store by an unknown factor for images no surface displays. Five
grids show one image per card; filing copies one file.

So the cache is one file per video, and ADR 0027's sentence — *filing copies out
of this cache and never fetches* — costs nothing extra.

**The file is named by the image's id, not the video's.** ADR 0027 compares
images "by identity, not by bytes", because the repair pass already diffs
`images[]` and therefore knows when the chosen entry has become a different one.
Naming by image id makes that comparison free on this side too: a changed choice
is a different filename, the old file simply stops being referenced, and nothing
has to decide whether the bytes at `…/<video id>` are current.

Files live under the tool's data directory at `artwork/<first two hex of the
id>/<id>`, because a single directory holding a hundred thousand files is one
nobody can list, back up by hand, or delete from safely.

## Two triggers, because there are two populations

[ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)
makes five surfaces artwork grids, and
[ADR 0013](0013-the-prdb-catalogue-is-a-cache-with-pinned-rows-repaired-by-re-reading.md)
draws the seam they fall on. They do not fall on it evenly.

**Pinned videos are fetched by a routine.** The library grid shows one image per
held video, and ADR 0027 requires a held video's image to be on disk so that
filing has something to copy. Neither of those tolerates *fetch it when someone
looks*: the first would show a grid of blanks on a fresh restore, and the second
would put a network read inside the file lane, which ADR 0026 built to wait on
nothing.

So: a routine in the **bulk lane**, whose work set is every pinned catalogue
video whose chosen image is not in the cache. A query over a state, keeping no
position, which is the shape ADR 0026 established and the reason this needs none
of ADR 0014's resumable-position machinery. It runs at the bulk lane's short
cadence and takes **newly pinned videos first**, which is what puts a freshly
downloaded video's image on disk within a minute or two of ADR 0026 pinning the
catalogue row — comfortably inside the hours a cross-filesystem copy takes. That
is a consequence rather than a promise: ADR 0027 already fixed what happens if
the image is not there, and it is nothing.

**Unpinned videos are fetched when a grid asks.** What's New, Sites, Actors and
Wanted range over videos prdb knows about, which is a much larger and much less
predictable set than what is held. Prefetching them means fetching the artwork
of the entire catalogue up to its row ceiling for pictures nobody will scroll
to.

The grid asks the tool for the image, never the CDN; the tool serves the cached
file, or fetches it, stores it, and serves it. This is precisely the sentence
`VISION.md` uses to justify caching at all — "a grid of thumbnails that fetches
on every scroll is a grid nobody scrolls" — read as what it says: the *second*
scroll is free, which is the property being bought.

## Why this does not pass the governor, and why that is safe

An image URL is an absolute URL prdb hands out in its own payload, and fetching
it is a `GET` against a CDN, not a call on the documented API. It is not made
through `Prdb.Sdk`, it carries no API key, and it does not appear in the
rate-limit headers ADR 0013 reads the budget from. Putting it under
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)'s governor
would spend a budget the request does not consume, and would make a library grid
compete with identification for prdb requests.

`VISION.md`'s rule that prdb's public API is the only door is untouched: the URL
came through that door.

The bound that does apply is bandwidth and patience, so three limits stand in
the governor's place, all constants:

- **A small fixed concurrency** for the routine, so a backfill of a few thousand
  images does not saturate the line the downloader is on.
- **A short timeout** on the display path. A slow CDN serves the no-artwork tile
  rather than holding a grid open.
- **A per-file size ceiling and a content check** before the bytes are kept —
  the bounded download, the image check and the temporary-name-then-rename that
  ADR 0027 noted already exist on this side of the line.

This does not contradict ADR 0018's *refreshing never causes work*. What that
rule protects is the governor and the indexers' daily query budgets: a person
must not be able to spend a scarce budget by pressing reload. Nothing here
touches either, and ADR 0022 already established that work a person asked for is
not the same as a page reading itself.

## What bounds it, and what it may not evict

**A byte ceiling over the unpinned part only, and it is a constant: 2 GiB.**

Bytes rather than rows, which is where this departs from ADR 0013's and
ADR 0015's choice of counts, and the departure has a reason rather than being an
oversight. Those two ADRs chose counts because a *duration* implies an
unpredictable amount of disk. Here the disposable thing **is** disk, images vary
in size by an order of magnitude, and a row count would therefore be the
unpredictable number. The test both ADRs applied — a number that can be written
in the documentation and held in the head — is passed better by the byte figure.
At the few hundred kilobytes prdb's images run to, 2 GiB is several thousand
browse-grid videos, which is far more than anyone scrolls between restarts.

**Pinned images are not counted and are never evicted.** They are the library
grid and the source filing copies from; evicting one would mean a held video
with no picture and a repair pass fetching it back. So the ceiling bounds
exactly the half `VISION.md` calls disposable, and the other half is
proportional to what the user actually has — the same shape ADR 0013 gave the
catalogue.

**Eviction is least-recently-served first**, run by the same routine when the
ceiling is exceeded. Unpinning does not delete anything: it makes a file
evictable, and the next pass may or may not take it.

**It is not a setting.** ADR 0020 admits a control where the answer lives
outside anything the tool can observe, and the honest reading here is that the
only thing the setting would change is how often a browse grid re-fetches a
thumbnail. That is ADR 0014's control with no correct value, and the half it
would bound is the half that is disposable by design. Documented as a number,
like ADR 0015's hundred thousand rows.

Unlike ADR 0015, **there is no Gap when the ceiling cannot be held**. That ADR
refuses to evict a release nobody has looked at, because a dropped release is a
wanted video never found; here the worst case is a picture fetched twice.

## Nothing here is in the backup

ADR 0009's test is *cannot be fetched again*, and `VISION.md` names cached
artwork explicitly among the things that are disposable by design, beside the
indexer cache. Both halves fail the test — including the pinned half, which the
routine refills from prdb the moment a restored installation has a key.

The consequence is worth stating rather than leaving to be discovered: a
restored installation shows a library grid that fills in over the following
minutes, and a video filed during that window gets no entry image until
ADR 0027's repair pass comes round. Both are states that ADR already handles,
and neither is a Gap.

## What a dead URL leaves behind

ADR 0013 requires the cache to tolerate its URL going away, since an unpinned
row is never repaired and its artwork may be dead before the row is evicted.

The rule is: **mark it once, never retry on a schedule.** A fetch that returns
404 or gone writes an unavailable mark on the image row; the grid draws the
no-artwork tile, and no routine and no display asks again.

- For a **pinned** video, ADR 0013's repair pass is the authority. It re-reads
  `images[]`, and the mark is corrected when the pass replaces the choice with
  another image or finds none — which is a change with a replacement, exactly
  the case ADR 0027 already writes for.
- For an **unpinned** video, nothing repairs it, so the mark stands until the row
  is evicted. That is ADR 0013's accepted cost — "a missing image on a browse
  grid" — written down rather than restated as an error.

A transport failure is **not** a dead URL and leaves no mark: it is the same
distinction ADR 0016 draws between a request that failed and an id that was
genuinely absent, and collapsing the two would turn one flaky minute into a
grid of permanent blanks.

Where the entry image is already in the library and the URL dies, the file on
disk stays — ADR 0027 decided that, and this changes nothing about it.

## Considered options

**Cache every image in `images[]`.** Rejected: no surface displays a second
image, and the store becomes a multiple of the catalogue for rows nobody looks
at — the same argument ADR 0013 used to make the images feed discard what it
cannot place.

**Name the file by video id.** Rejected: it re-introduces the question ADR 0027
answered by identity, forcing either a stored marker saying which image the
bytes are, or a byte comparison. The image id is already unique and already
diffed.

**Prefetch the whole catalogue's artwork.** Rejected under *two triggers*: it
fetches up to the catalogue's row ceiling of images to serve grids that show a
page at a time, and it is the single largest bandwidth cost the tool could
choose to incur unasked.

**Fetch everything lazily, including pinned videos.** Rejected: the library grid
of a restored installation would be blank until scrolled, and filing would find
an empty cache exactly when it needs one — turning ADR 0027's rare fallback into
the normal case.

**Fold the fetch into ADR 0013's repair pass**, as ADR 0027 folded the sidecar
rewrite in. Rejected, and the asymmetry is the point: the repair pass is steered
by a **prdb request budget**, and this work spends no prdb request at all.
Attaching a free local job to a scarce budget would make artwork arrive at the
speed of the rate limit for no reason. A sixth routine is the cost, and it is
the one place paying it is right.

**Put the ceiling behind a setting.** Rejected under *what bounds it*: it
controls only how often a thumbnail is re-fetched, and ADR 0020's test does not
admit it.

**A row count instead of a byte ceiling.** Rejected: images vary by an order of
magnitude, so a count says nothing about disk, which is the resource being
bounded. The consistency with ADR 0013 and ADR 0015 is surface-level — both
chose the unit that is predictable for what *they* bound, and this does the
same.

**Retry a dead URL on a slow timer.** Rejected: prdb hard-deletes image rows
(ADR 0013), so a 404 is normally permanent, and the pinned case has a real
repair path already. A timer would spend requests to rediscover a fact the
repair pass establishes properly.

**Serve the library grid from the entry images on disk and cache nothing for
held videos.** Rejected by ADR 0027 already, and restated because it looks like
free disk: the library is the one directory the user is invited to modify, and
the other four grids need the cache regardless.

## Consequences

- `CONTEXT.md` gains **Artwork Cache**, distinguished from **Catalogue** (rows,
  not bytes) and from **Entry Image** (an output in the library that nothing
  reads back). ADR 0027's *Avoid* list on Entry Image already keeps the two
  words apart.
- **ADR 0014's table gains a sixth routine**, in the bulk lane: fetch artwork
  for pinned catalogue videos, and evict the unpinned part down to the ceiling.
  It is the routine ADR 0027 declined to add, added here for the different job
  it does. Like the five before it, its cadence belongs with the open pacing
  question, and like them its work set is a query over a state.
- The data model gains nothing but **two nullable columns on the existing image
  row** — whether the bytes are cached and whether the URL was found dead — plus
  a last-served stamp for eviction order. None of it is exported.
- **The bytes are not in the backup**, and a restore refills them in the
  background. ADR 0009's list of what a backup holds is unchanged; this is one
  more thing on the disposable side of it.
- **The display path can fetch.** That is the first time a page request may do
  network I/O, and the timeout and the fallback tile are what keep it from being
  the first time a page request can hang. It spends no prdb budget, so ADR 0018's
  rule is intact.
- **ADR 0027's one requirement is met**: a held video's image is on disk, put
  there by a routine that runs ahead of filing rather than by filing itself.
- The map's artwork-caching fog patch is closed. ADR 0027 bounded it to the
  cache; this settles the cache.
