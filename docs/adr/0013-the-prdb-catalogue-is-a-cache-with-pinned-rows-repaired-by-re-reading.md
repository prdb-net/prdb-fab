# The prdb catalogue is a cache with pinned rows, repaired by re-reading

Videos and sites are the only prdb entities with no change feed. Rather than
mirroring prdb's corpus, the tool holds a catalogue that is a cache: rows arrive
because something looked at them, rows stay because something local points at
them, and the rest is dropped. What is pinned is kept correct by re-reading it
on a request budget, because there is no feed to be told by.

## Why a cache and not a mirror

A mirror is the obvious shape and the API refuses it twice over. There is no
`/videos/changes`, so the only route to the corpus is paging `GET /videos` at
100 rows a request, and the document states no row count anywhere — the cost of
that drain cannot be derived before committing to it. Worse, a mirror is a
promise to keep every row correct, and correcting a video means re-reading it by
ID at 50 per request. A mirror of a corpus of unknown size therefore carries a
repair obligation of unknown size, forever, for rows nobody will ever look at.

The cache inverts both. The bootstrap is bounded because it is a page count, not
a corpus. The repair obligation is bounded because it covers what is pinned, and
what is pinned is proportional to what the user actually has and wants.

Three of the five browse surfaces need no catalogue at all: the wanted-list and
favourites feeds carry `videoTitle`, `siteTitle`, `videoReleaseDate` and
`imageUrl` in their own payloads. Only What's New and the library read the
catalogue, and the library reads only pinned rows.

## Why the filehash feed is not synced

`VISION.md` listed "the hashes that tie all of it to real files" as part of the
prdb sync. That was written before the API was surveyed and this decision
amends it.

The identification ladder runs server-side in `POST /videos/identify`, where
osHash is the first rung, and the whole request counts as one against the rate
limit for up to 200 files. Before a download there is no file and therefore
nothing to hash; after one, the tool asks the endpoint whose answer is
authoritative. A local hash mirror would duplicate — less well — what one
request already does, and it is the largest feed in the API by a wide margin, so
it would be the most expensive part of the bootstrap by far. It buys nothing the
first release needs.

`/video-user-images/changes` is likewise left out: user-submitted previews are
not what a library is filed on.

Five feeds remain: actors, video images, wanted videos, favourite sites,
favourite actors.

## Why repair is one pass and not two

Two holes look separate and close together. Video metadata edits have no feed at
all. Video images have a feed, but it is documented as never emitting a
`deleted` — image rows are hard-deleted and simply stop being returned, so a
removal is invisible.

`POST /videos/batch` answers both: it returns the current `VideoDetailDto`,
which carries the authoritative `images[]`. Diffing that against the local copy
finds the removed artwork; the rest of the payload finds the correction. One
pass, 50 videos a request, oldest-checked first.

The pass is steered by a request budget rather than by a cadence. The numeric
value of the rate limit is not in the API document at all and has to be read
from response headers at runtime, so a budget is the only control that can be
sized against a limit discovered while running.

## Why the images feed discards what it cannot place

The feed is global; the catalogue is a fraction of it. Keeping image rows for
videos the catalogue does not hold would make the image table a multiple of the
table it describes, for rows that may never be looked at.

Discarding costs nothing, because a catalogue row is never created without a
detail read: `VideoSummaryDto` from `GET /videos` carries no image field, so
every new row comes from `GET /videos/{id}` or `POST /videos/batch`, and both
bring `images[]` with them. The feed's job is what the document says it is —
delivering artwork that arrives days after the video.

## Why a broken sync does not stop automation

ADR 0007 makes the wanted list the only source of intent, so a stalled sync
means automation acting on yesterday's intent: fetching what was struck off,
missing what was added. The damage is asymmetric — a download too many costs
bandwidth, a download too few costs only delay, which is the same asymmetry ADR
0006 used to make the pre-download gate the looser one.

Automation therefore continues on the last known state and a Gap says how old
that state is. A tool that stands still whenever prdb hiccups is a tool people
check by hand, which defeats running it unattended. Fulfilment reports are the
exception, because they are writes: they queue and wait.

## Considered options

**Mirroring the corpus.** Rejected above: unbounded bootstrap, unbounded repair.

**Holding nothing but pinned rows, everything else live.** The cheapest store,
and it makes What's New — the landing page — a network round trip on every
visit, with a second request for artwork because the list DTO carries none. The
catalogue costs little and removes that.

**A time-based eviction rule** ("drop what has not been looked at for 30 days").
Rejected for a count. Nobody can predict how much disk a duration implies,
because nobody knows how many videos prdb adds per day; a maximum row count is a
number that can be written in the documentation and held in the head.

**A per-video freshness indicator in the UI.** Rejected because every catalogue
video is potentially stale at every moment — there is no feed — so a badge on
everything says nothing. The Gap is reserved for a sync that is actually broken.

**Backfilling What's New by a date window.** Rejected for a page count, for the
same reason as the eviction rule: a window of days has an unpredictable cost, a
window of pages has a stated ceiling.

## Consequences

- Onboarding does not wait for the first backfill. It runs in the background and
  carries its own resumable position, exactly as a feed carries a cursor, so a
  restart continues rather than restarting. While it runs, that is a fact for
  the sync status page and explicitly **not** a Gap: nothing is broken, it is
  merely unfinished.
- `CreatedAfter` is documented as strictly exclusive, so a high-water mark set
  to the last `createdAtUtc` seen would permanently lose every video sharing
  that timestamp — precisely the bulk-import case. The mark is set back by an
  overlap window and every result is applied as an idempotent upsert, the same
  rule the change-feed cursors already need.
- Pinning is not a stored column. (*Amended by
  [ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md):
  this decision speaks of a pin reason per video row, and the schema stores
  neither the pin nor the reason. Both are computed from what points at the row,
  which is how `CONTEXT.md` already defines **Pinned** — a stored flag would have
  six writers and no reader that would notice a mistake, and eviction reads it
  over the candidates it walks rather than over the whole table. What is pinned
  and what may not be evicted is untouched.*)
- An unpinned catalogue row is never repaired, so its artwork URL may be dead
  before the row is evicted. A missing image on a browse grid is the accepted
  cost; a pinned row is never in that state.
- Sites are replaced wholesale from `GET /sites` with `If-None-Match`, since the
  full list fits one request. Site rows are never deleted, only marked as no
  longer offered: a library entry must still name the site its filed path was
  built from under ADR 0005. A cache hit answers `200` with a body instead of
  `304`, which the document calls expected and which must not be read as a
  change.
- A key belonging to a different prdb account drops the user half of the local
  data — wanted list, favourites, and those three cursors — and keeps the
  catalogue, which belongs to no account. Library entries stay pinned; ADR 0010
  already settled that the change is confirmed rather than blocked, and that the
  record of what was reported survives — which
  [ADR 0019](0019-fulfilment-understates-the-quality-and-is-retracted-only-by-a-person.md)
  amends to *survives, scoped to the account it was made under*, since that
  record turned out to be a suppression key rather than a duplicate guard.
- Candidate videos of an open review queue entry are pinned, so eviction cannot
  empty a choice the user has not made yet. The pin ends when the entry is
  decided: the chosen video is pinned by its library entry, the rest fall back
  into the catalogue.
- A catalogue row stores everything the detail read returned, including
  pre-names, but references actors rather than copying them, since the actors
  feed already holds them whole. Whether pre-names are used to recognise a
  release is a separate question; withholding them would have answered it by
  default.
- Ticket 13 sets the numbers. This decision fixes the shapes — a budget for
  repair, a page ceiling for backfill, an overlap for the high-water mark — and
  leaves every value to the ticket that owns polling.
