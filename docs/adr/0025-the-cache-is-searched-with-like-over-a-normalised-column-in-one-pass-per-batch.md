# The cache is searched with LIKE over a normalised column, in one pass per batch

The indexer cache is searched by title with an ordinary `LIKE` over a stored
normalised column, and there is no full-text index of any kind. The backwards
direction — a new pre-name or title reaching the rows written before it existed
— is a routine of its own in the bulk lane that takes every needle accumulated
since its last run and makes **one** pass over the cache, never one query per
needle. `POST /videos/identify` is still the only thing that identifies
anything; this decision is only about how the rows are found.

## Nobody types against this table

[ADR 0004](0004-the-stack.md) raised this question as a question about a person
waiting, and that premise no longer holds. There is no surface in the first
release where a person types free text that runs against release titles.
[ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)'s
release view is reached from a wanted video or a library entry and is ordered by
the release ranking: it shows the releases of *one* video, so its query selects
on the identified video and not on text at all. `VISION.md`'s "searchable by
title" belongs to the library grid, which is a view over catalogue videos.

So the whole of this table's title search is
[ADR 0023](0023-nothing-local-identifies-anything-and-a-pre-name-is-only-a-reason-to-ask.md)'s
screening, in both directions, running unattended in the bulk lane. That is what
sizes the decision, and `VISION.md`'s promise that the UI is fast because the
cache answers rather than the indexer is kept by an indexed lookup on the video,
which was never the hard case.

## Why there is no full-text index

The question was left open for a number, so it is answered with one. Measured
against 300 000 rows — three indexers at
[ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md)'s ceiling of
100 000 each — carrying synthetic titles shaped like scene release names, on
SQLite 3.49.1 as `Microsoft.Data.Sqlite` ships it:

| | plain `LIKE`, no index | FTS5, `trigram` |
|---|---|---|
| one query | 30–42 ms | 0–21 ms |
| 500 queries in a burst | ~17.3 s | 2.4 s |
| **one pass, 500 needles at once** | **1.7 s** | — |
| disk | 40 MiB | 89 MiB (**+119 %**) |
| writing one walk page of 100 | ~0.3 ms | ~28 ms |
| needle under three characters | 37 ms | 71 ms |

The index costs more disk than the table it indexes, on the largest and most
continuously written table this tool has, and it is then beaten by a single pass
that needs no index at all. The last row is the quiet one: `trigram` is the only
tokeniser that can express containment over a string of dots and scene tags at
all — the default splits a release name in ways nobody searching for a site name
would expect — and below three characters even it stops helping and scans
anyway. An index that silently withdraws at the short queries is worse than
none, because its cost is unconditional and its benefit is not.

Both burst figures matter more than the single-query ones. `VISION.md` promises
a UI that does not wait; it promises nothing about a bulk routine, and one
second or one minute there is equally unobserved.

## What the searchable text is

ADR 0023 fixed the comparison — lower case, separators collapsed to one, the
extension dropped, and the test is containment rather than equality. That
normalised form is **stored**, as a column on the release row written at upsert
and as a column beside every pre-name and every video title the catalogue holds.

The disk is the small reason: roughly 15 MiB on the release side, against the
48 MiB the index wanted. The real reason is that storing it makes the
normalisation exist **once**. The same function writes the needle and the
haystack, so the two cannot drift apart — and two normalisers drifting is
exactly the silently skipped row that ADR 0015 calls the most expensive failure
this cache has, arriving by a different door.

It is deliberately **not** the normalisation
[ADR 0024](0024-the-wanted-sweep-asks-with-a-title-from-a-reserved-share-of-the-budget.md)
builds `q` from. Those two look alike and answer to different masters: ADR
0024's form goes over the wire to be read by somebody else's tokeniser, and this
one stays here and is only ever compared with itself. Merging them would mean a
correction made for one indexer's search behaviour silently changing which
cached rows a pre-name reaches.

## One pass per batch, and the routine that makes it one

A new fact arrives from the catalogue side — a video becomes wanted, or a repair
read brings back a pre-name prdb has since added — and it has to reach the rows
that are `Unremarkable`, `SiteOnly` or `Unknown`. The obvious shape is to do it
where the fact lands, in the feed or the repair pass. That is rejected twice
over: it is one query per needle, which is the form the numbers above defeat,
and it puts a seconds-long scan in the sync lane where ADR 0014 paced the prdb
feeds.

So it is a **routine of its own in the bulk lane**, beside screening. It takes
the needles accumulated since its last run and makes one pass, which is how a
wanted-list import of five hundred videos costs one scan rather than five
hundred. That also gives it the resumable position
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md) requires of
every routine, and it is the property that makes the choice safe as the tool is
used: five hundred needles cost what five cost, so the batch form does not
degrade as the wanted list grows, while a query per needle degrades linearly in
exactly the thing a user is expected to add to.

Nothing has to survive a row being evicted mid-pass. ADR 0015 never evicts a row
nothing has looked at, the pass only writes states onto rows it has just read,
and a row that falls between the two is re-entered as new by a later walk under
that ADR's upsert. There is no view showing a stale result, because by *nobody
types against this table* there is no view.

## The catalogue's title search is a different problem

The library grid and the browse surfaces search **videos**, and that is where a
person really does type. It gets an ordinary `LIKE` over the video title and
shares nothing with the above — not the index, which neither has, and
particularly not the normalised column. A few thousand pinned rows are fast
under any technique, their titles are prose rather than punctuation, and
inheriting the normalisation ADR 0023 defined for a cost filter would put it
somewhere it has no business being. Two problems that only look alike.

## Considered options

**An FTS5 index with the `trigram` tokeniser.** The only full-text option that
can express ADR 0023's containment, and genuinely seven times faster per query.
Rejected under *why there is no full-text index* on what it costs to get that:
more disk than the table itself, a two-order write amplification on the table
ADR 0015 fills continuously, and a fallback to a full scan below three
characters.

**An FTS5 index with the default tokeniser.** Cheaper than `trigram` and unable
to answer the question — it tokenises a release name on its punctuation, so a
containment test over a site name written the way the scene writes it does not
survive the split.

**A `LIKE 'prefix%'` over an indexed column.** The one shape a B-tree could
actually serve. Rejected because ADR 0023's test is containment, and an indexer
puts the site, the date and the performer in front of everything a pre-name
would match — anchoring at the start finds almost nothing.

**Doing the backwards search inline where the new fact lands.** Rejected under
*one pass per batch* — one query per needle in the wrong lane.

**One normalisation shared with ADR 0024's sweep query.** Rejected under *what
the searchable text is*: same shape, different masters, and coupling them makes
an outbound-search fix reach inward.

**Deferring the question again, to be settled against real data.** The reading
ADR 0004 invites. Rejected because it is now measurable without real data: the
size of the search space is fixed by ADR 0015 rather than discovered, and the
answer at that ceiling is not close enough for a measurement against real titles
to move it.

## Consequences

- **ADR 0004's open question is closed rather than deferred**: searching the
  cache by title does not need SQLite's full-text search, and the build carries
  no FTS5 table.
- **The release row gains a normalised title column**, written at upsert beside
  the title ADR 0015 already overwrites. The catalogue side gains the same
  column beside each pre-name and each video title. None of it is exported —
  both the cache and the catalogue refill themselves — and all of it is derived,
  so a change to the normalisation is a migration that recomputes rather than a
  fact that can be lost.
- **ADR 0014 gains a third routine from ADR 0023's family**: the backwards
  search, in the bulk lane, carrying its position as the point in the stream of
  new pre-names and titles it has already passed over the cache. Its cadence
  belongs with the schedule rather than here. (*Amended by
  [ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md):
  the position becomes a **state on the needle** — a flag beside the normalised
  pre-name and title this decision already stores — because a needle added while
  a pass was running would sit behind a position and never be searched, which is
  ADR 0015's silently skipped row one layer up. The batch argument above is what
  makes it safe: the pass writes only onto rows it has just read, so a crash
  leaves the flags set and the batch is simply taken again.*)
- **A `COUNT` or a lookup on the identified video is what the release view
  costs**, so that column is indexed and the view never touches title text.
- The two burst figures are the ones to re-measure if this is ever revisited.
  Nothing else in the table would change the decision.
