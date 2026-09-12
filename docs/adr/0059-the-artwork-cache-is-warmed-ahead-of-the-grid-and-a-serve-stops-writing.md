# The artwork cache is warmed ahead of the grid, and a serve stops writing

The unpinned half of the Catalogue's artwork is fetched before anybody looks at
it, newest release first, until it reaches six of an eight-gigabyte ceiling. A
serve writes its `LastServedAt` no more than once an hour rather than once per
tile per grid. A browser may keep an image for a year, and may remember for a
week that prdb has no image at all — which is now a different answer from *the
CDN did not reply*. The write-ahead log is checkpointed daily by a routine of
its own.

This reverses one sentence of
[ADR 0030](0030-artwork-is-cached-locally-under-a-ceiling-and-a-dead-url-is-marked-once.md)
— *everything unpinned is fetched when a grid asks* — and raises the ceiling it
chose. Everything else that decision settled stands: one file per image named by
the image's id, the pinned half outside the ceiling, a dead URL marked once and
never asked about again, the small fixed concurrency and the per-file size stop.

## What was measured

On a database built from this model, at ~47 000 Catalogue Videos, ~840 Sites and
~323 000 Actors, on ordinary spinning disks. Clicking Search, Sites or What's New
took 5-6 s for Sites and 15 s or more for Search before the grid appeared.

**The database was not the bottleneck.** Every query behind the browse grids
returns in milliseconds:

| query | time |
| --- | --- |
| Sites grid, full projection, ordered by video count | 0.06 s |
| Actors grid, ordered by credit count over 323k rows | 0.18 s |
| Catalogue Search default, Available and newest first | 0.05 s |

**The cache was effectively empty.** 798 of 69 093 images were on disk — 1.2 %,
112 MB against a two-gigabyte ceiling. ADR 0030's routine warms *pinned* Videos
only, so a twenty-four-tile grid meant ~24 live CDN fetches, averaging ~135 KB
each: ~3.4 MB the server had to fetch, write to disk and then serve before the
grid was complete.

**Every artwork request wrote to the database.** `ArtworkCache.ServeAsync`
updated `LastServedAt` on every serve, cached or not — 24 writes per grid, and
on an uncached image a fetch, a file write and a second row write besides.

**The disk is a spinning-HDD RAID, and that is the design target rather than
an accident.** Such a disk shows 30-45 ms read and write latency. ADR 0004 gives
SQLite a single writer, so those ~24 writes serialise against it — and against
the routines that write continuously, the repair pass re-reading 50-500 Videos
every ~30 s among them. That serialisation on slow storage under concurrent write
load is what turned a grid that should take about a second into fifteen.

The write-ahead log had grown to ~37 MB, which is a file nothing had ever
emptied.

## Why the answer is not a faster disk

The deployment target is ordinary spinning disks. An installation of this tool
is a machine somebody already has, and the thing it is full of is video files;
whatever is fast on it is not where the database lives. So every fix below has
to hold on a disk with 40 ms latency by design rather than by luck, and *move
the database to an SSD* is not one of them.

## Why the unpinned half is warmed after all

ADR 0030 refused this in one sentence, and the sentence had a reason: *What's
New, Sites, Actors and Wanted range over a catalogue nobody scrolls all of.* It
is still true that nobody scrolls all of it. What it did not account for is that
everybody scrolls the *front* of it, and that the front is the same front every
time — the four surfaces that matter are ordered by release date or by
popularity, and both orders are stable between one visit and the next. A cache
that fills only behind a click is a cache that is cold exactly where every click
lands.

The property ADR 0030 was buying — *the second scroll is free* — held. It was
the wrong property: the tool is used by one person, who scrolls the first page
of What's New after not looking at it for two days, and that scroll was never
the second one.

Newest release first, because that is the order What's New and Catalogue
Search's default come back in, so a warm pass reaches the first page of both
before anything else. Videos without a release date sort last; they are the ones
no browse surface puts on a first page either. The Actors grid gets the same
treatment from the other end of its own order, bounded at the two thousand its
first eighty pages hold — a Catalogue of this size holds several times more
Actors than the whole ceiling would fit, so there the front is a front rather
than a set.

## Why the ceiling is eight gigabytes

Two was chosen when nothing had measured what a Catalogue weighs. ~69 000 images
at ~135 KB is ~9.3 GB, so two held about a sixth of one and the other five
sixths went to the CDN on every visit. Eight holds very nearly all of it.

The figure is still not a setting, for ADR 0020's reason: the only thing it
changes is how often a browse grid re-fetches a thumbnail, and the tool can
observe that better than a person can. What changed is not the argument but the
number the argument was applied to.

Eight gigabytes of thumbnails sits beside a library of video files. The ratio is
what makes this cheap — an installation holding a hundred Videos is holding far
more than eight gigabytes of them — and it is why the pinned half stays outside
the ceiling exactly as ADR 0030 left it.

## Why the warm pass stops at six

Because otherwise it fights the sweep. Eviction is
least-recently-*served* first and a file that was warmed and not yet looked at
has no serving stamp at all, so it sorts first. A pass that filled to the
ceiling would have its own newest work dropped by the sweep in the same turn,
and fetch it again on the next one — a loop that does nothing but write to a
slow disk.

Two gigabytes of headroom is ~15 000 images, which is more than anybody serves
between two passes of a routine that runs every thirty seconds. So the budget is
checked once per turn rather than per image: a window is a hundred images, and
the overshoot that allows is two orders of magnitude inside the gap.

## Why the serving stamp is throttled rather than dropped

The stamp is what makes eviction least-recently-served rather than arbitrary,
and ADR 0030's argument for that — the bytes are disposable, so drop the ones
nobody is looking at — is unchanged. What is wrong is only its resolution. A
stamp that drives a sweep over a cache measured in days does not need to be
accurate to the second, and the price of that accuracy was a write in the click
path, twenty-four of them per grid, against a single writer on a disk where a
write costs 40 ms.

An hour. A second look at the same grid now writes nothing at all, which is the
case the lag was actually reported about.

Batching in memory was the alternative and was refused: a queue of pending
stamps is a second place the truth lives, lost on restart, and it would buy
nothing the throttle does not — the first serve still writes, and the repeats
are what there were two dozen of.

## Why a browser may keep an image for a year

ADR 0030 already said the horizon is safe at any length: the bytes under one
address change only when prdb publishes a different first image, at which point
the picture is stale rather than wrong. It then chose a day, which is safe and
buys nothing. A person who opens this tool every two or three days has an empty
browser cache every time, so every visit re-fetched every tile *through the
server* — the one place a fetch costs a file read on the slow disk.

A year, and `immutable` with it, so that a reload is not a revalidation either.
The failure this admits is exactly the one ADR 0030 already accepted, now with a
longer tail: a first image replaced upstream shows the old picture to a browser
that has it, until that browser drops it. For a CDN image already published,
that is close to hypothetical.

## Why an absence is now two answers

`ServeAsync` returned `null` for three different things: prdb publishes no image
for this Video, the URL was found dead, and the CDN did not answer inside the
artwork transport's timeout. To a grid they are one thing — draw the no-artwork
tile — which is why they were one value.

To a browser they are not. The first two are the Catalogue's answer and they
apply to thousands of Videos at a time on a Catalogue this size; the third is
about this minute. Under one horizon either the tiles keep asking about Videos
that will never have a picture, or one bad minute is remembered for a week. So
the cache now says which it is, and the route gives the settled answer a week
and the unsettled one the five minutes it always had.

A week rather than forever, because ADR 0013's repair pass may yet read an image
onto a row that had none. This is the same distinction ADR 0030 draws between a
dead URL and a transport failure, and ADR 0016 between a request that failed and
an id that was genuinely absent — carried one layer further out.

## Why the write-ahead log gets a routine

ADR 0039 opened the database in WAL and said nothing about emptying the log.
SQLite's own answer is `wal_autocheckpoint`, which tries every thousand pages
and gives up silently whenever a reader is in the way — and in a tool whose four
lanes read continuously, something usually is. The log measured ~37 MB, which is
a file every read walks past.

`TRUNCATE` rather than `PASSIVE`, because the size is the point: a passive
checkpoint copies the frames back and leaves the file as large as it found it.
It waits on the readers rather than skipping them, which ADR 0039's
`busy_timeout` already makes the shape of every other write here; when the wait
runs out the log stays as it was and the next day's turn tries again. Nothing is
lost either way, which is why this needs no failure of its own.

Lowering `wal_autocheckpoint` was the alternative and would have made it worse:
more checkpoint attempts against the same readers, each one more writing on the
disk that is already the constraint.

## Consequences

- **ADR 0030 is amended in one sentence.** The unpinned half is warmed rather
  than only served. Its ceiling, its file layout, its dead-URL rule, its
  concurrency and its size stop are unchanged, and the pinned half still goes
  first in every pass.
- **ADR 0039 is continued rather than amended.** The four pragmas stand exactly
  as they are. What is added is a routine, not a setting on a connection.
- **The first warm of an empty cache takes about six hours** — a hundred images
  every thirty seconds — and holds no lane while it does. It costs no prdb
  budget, because an image URL is a `GET` against a CDN carrying no key, so
  ADR 0018's rule that refreshing never causes work is still intact.
- **A data volume grows by up to eight gigabytes of thumbnails.** It is
  disposable in the sense ADR 0030 fixed: nothing in it is exported, and
  ADR 0009's backup does not carry it.
- **`CONTEXT.md` is unchanged.** A cache horizon, a checkpoint and a warm budget
  are construction rather than concepts, which ADR 0034 already settled the
  language needs no term for.
- **Nothing here is a surface and nothing here is a setting.**
