# A routine with a work set is due when the set is not empty

Six routines arrived after
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md) fixed the
schedule, and every one of them was given a lane and no cadence, each time for
the same stated reason: what makes them due is the arrival of rows rather than a
clock.

There is no second mechanism. They keep ADR 0014's row and its cadence, and the
cadence means something different for them: **how often to look, not how often
to act.** A run is bounded and yields its lane; a routine whose set is still not
empty is immediately due again. Nothing in the family stores a position any
more, and the status page reads a set size rather than a clock, because an
empty tick is not a run.

## The six, and what each one's set is

| Routine | Lane | Work set |
|---|---|---|
| Screening ([ADR 0023](0023-nothing-local-identifies-anything-and-a-pre-name-is-only-a-reason-to-ask.md)) | bulk | cached releases nothing has looked at |
| Backwards search ([ADR 0025](0025-the-cache-is-searched-with-like-over-a-normalised-column-in-one-pass-per-batch.md)) | bulk | pre-names and titles not yet searched backwards |
| Release identification (ADR 0023) | sync | releases `Awaiting` |
| Identify arriving files ([ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md)) | sync | arriving files `AwaitingIdentification` |
| File (ADR 0026) | file | arriving files `AwaitingFiling` |
| Artwork ([ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md)) | bulk | pinned videos with no cached image; the cache over its ceiling |

Every one of them is a query over a state, which is the shape ADR 0026
established for the filing chain and
[ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md)
chose first, for the outstanding set: *a container restart resumes by
definition rather than by ADR 0014's resumable-position mechanism.*

## Why the poll is the mechanism and a signal is not

The attractive alternative is to wake the lane from whatever wrote the row —
collecting hands a file to the identify routine, a repair read hands a new
pre-name to the backwards search. It is rejected, and not on complexity.

**A signal has to survive a restart, and it cannot.** A row written a
millisecond before the container went down has no signal waiting for it
anywhere, so the tool would still have to ask every work set on startup — which
is the poll, present anyway. Once it is present as the floor, the signal is an
optimisation that can only make things arrive sooner, and its failure mode is
the worst one available here: a lost signal is a routine that never runs again
for a row that is genuinely waiting, and nothing would ever notice, because the
row is in exactly the state a healthy idle tool also has rows in.

So the lane ticks and asks. The question it asks is an indexed `COUNT` over a
state column that exists for other reasons — the review queue count already
demands one
([ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)),
and ADR 0018 already draws the unidentified share of the indexer cache — so the
idle cost of the whole family is six counts per tick and no allocation beyond
them.

**Nothing is added to make it faster.** An in-process nudge that merely skipped
the remaining wait would be correct and is still refused: it is a second path to
a decision the tool makes anyway, it is untestable in the case that matters
(the restart), and the latency it saves is bounded by the tick.

## The cadence is an idle tick, and a run is bounded

The two halves that make one row shape do for both kinds of routine.

**When the set is empty, the routine sleeps for its cadence.** Fixed numbers, as
everywhere in ADR 0014: **10 s** in the sync lane, **10 s** in the file lane,
**30 s** in the bulk lane. Sync is the shortest because ADR 0014's scarcity
order already puts `POST /videos/identify` for an arrived file first — something
is waiting on it. Bulk is the longest because everything in it is a backfill's
relative and nobody is watching.

**When the set is not empty, the run is bounded and the lane is given back.**
Then the routine is immediately due again, so the lane round-robins between its
routines rather than letting one drain to completion. The bounds are each
routine's own, and each is the natural unit of its work:

- **Screening** — 1000 releases.
- **Backwards search** — the whole batch of needles, because ADR 0025's entire
  argument is that one pass over 500 needles costs what one over five costs.
  Bounding it by needles would be bounding the thing that is free.
- **Both identifications** — one request, which is at most 200 rows and is
  therefore bounded already by the endpoint.
- **Artwork** — 50 images.
- **File** — one arriving file. This one cannot yield: a cross-filesystem copy
  is not resumable mid-stream, and the lane holds nothing else.

This is head-of-line blocking solved *inside* a lane, which ADR 0014's lanes
solve only *between* lanes. Without it the artwork routine backfilling five
thousand images would hold the bulk lane against screening for as long as it
took, and the lane that exists to keep slow work off the fast path would have a
fast path of its own to block.

**The idle ticks are not settings**, for half of ADR 0014's reason. Its argument
against intervals was that they let a user break a rate limit they cannot see,
and a tick costs no budget at all — but its other argument stands untouched:
these are controls with no correct value, and the only thing a user could
achieve by moving one is to make an indexed count run more often.

## No routine in this family keeps a position

Five of the six never had one. The sixth did, and it can be expressed as a state
like the others.

ADR 0025 gave the backwards search "its position as the point in the stream of
new pre-names and titles it has already passed over the cache". That is a needle
having been used or not, which is a **state on the needle** — a flag beside the
normalised pre-name and the normalised video title that ADR 0025 already stores
— rather than an offset into a stream that has no stable order to be an offset
into. A new pre-name arrives unsearched; the pass clears the flag on everything
it took; a crash mid-pass leaves the flags set and the batch is simply taken
again, which is safe because ADR 0025 established that the pass only writes
states onto rows it has just read.

The gain is not tidiness. A position is a second place the truth lives, and it
can disagree with the rows — a needle added while a pass was running would sit
behind the position and never be searched, silently, which is the exact failure
[ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md) refused when
it chose a state per row over a timestamp for the inward cursor: *batched writes
would let a clock skip a row silently, and a skipped row is a wanted video never
found.* The same argument, one layer up.

ADR 0014's resumable-position machinery is untouched and keeps the work it was
built for: the one-shot routines — an indexer's first walk, the What's New
backfill, the actors drain, a catch-up window. Those page a remote endpoint
whose results are not rows we hold yet, so there is no state to query and a
position is the only thing there is.

## An empty tick is not a run, which is what the status page has to be told

This is the load-bearing consequence, and it is easy to get wrong in a way that
only shows up on a quiet installation.

A routine that ticks and finds nothing **has not run**: it did not succeed and
it did not fail. So its *last success* ages forever on a tool that is working
perfectly and simply has nothing to file. Reading it as ADR 0018 reads every
other routine — "when it last succeeded" — would put a week-old timestamp under
*File* on an installation whose only fault is that the user has not added
anything to their wanted list.

Recording an empty tick as a success is the tempting fix and is worse: it makes
*last success* mean "the process is alive", which is not a fact about the loop,
and it would hide a routine that genuinely stopped behind a clock that keeps
moving.

So a work-set routine reports **two facts and not one**, which is
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)'s own
headline shape applied one level down:

1. **The size of the work set.** Zero is the healthy idle state.
2. **When an item last completed.** Read as elapsed time, judged by nobody —
   ADR 0016's *no clock*, which ADR 0018 already applied to the page.

That ADR had reached for this once without generalising it: *Match* is already
drawn as "when identification last ran, **and how much of the indexer cache is
still unidentified**". This makes that the rule for all six.

**An empty work set is never a Gap and never a Brake.** Nothing is broken, and
nothing is being withheld by configuration — it is the tool having caught up.
That is the same finding ADR 0022 recorded for a full review queue, arrived at
from the opposite direction: there, work waiting on a person is not a fault;
here, no work at all is not one either.

ADR 0014's **backoff and its Gap after three consecutive failures are unchanged
and apply as written**, because a work-set routine's run fails when its item
fails. An unwritable library therefore backs the file lane off to an hour and
raises a Gap, which is exactly ADR 0026's installation condition reaching the
page by the route it was always going to reach it by.

## What the file lane needs and the others do not

ADR 0026 gave filing a lane of its own because a cross-filesystem copy of 40 GB
is hours. A lane whose single item takes hours is different in two ways that the
rule above does not cover.

**A run in progress has to be visible while it is running.** With one item in
four hours there is nothing to report from completed runs, and the two facts
above would read as *1 waiting, last completed four hours ago* — which is what a
stalled lane also looks like. Nothing new is needed to fix it: ADR 0026 writes
the **intended path onto the row when it enters `Filing`**, before anything on
disk is touched, so the page reads that row and says what is being filed and
since when. No new state, no progress percentage — ADR 0026 refused a progress
display for work that always finishes, and this is a name and a timestamp rather
than a bar.

**A failed item sorts to the back of the work set.** Without it, one item that
always fails is retried at the head forever and everything behind it never
files. ADR 0026 requires an installation condition to be retried forever, and
that is right for the condition and wrong as a queue discipline — where the
condition is genuinely installation-wide every item fails and the Gap fires
anyway, so the ordering costs nothing in the case it cannot help and saves the
lane in the case it can.

It costs one column, and it goes on the **work-set row and not on the routine**:
when this item was last attempted. That is the only column this decision adds
anywhere.

## Ordering inside a set belongs to the routine, not to the table

Three orderings already exist and they have nothing in common: ADR 0030 wants
newly pinned videos first, ADR 0023 wants releases whose reason came from the
wanted sweep first because someone is waiting on those, and
[ADR 0024](0024-the-wanted-sweep-asks-with-a-title-from-a-reserved-share-of-the-budget.md)
orders by a last-searched stamp per pair.

So ordering is a property of each routine's own query. The routine table learns
nothing about it. The one rule they share is the one above — an item attempted
and failed sorts after items never attempted — which is a tiebreak rather than
an ordering and does not disturb any of the three.

## What is untouched

- **The lanes.** ADR 0014's three plus ADR 0026's fourth, serial as they were.
  Nothing here priorities between lanes.
- **Scarcity precedence.** ADR 0014's fixed order over prdb requests stands, and
  ADR 0026's placement of arriving-file identification at the head of it is what
  the 10 s sync tick serves.
- **The governor.** Every prdb request from these routines passes it. A deferral
  is still not a state (ADR 0026): the row stays where it is, and the routine
  meets it again on the next tick.
- **"Run now."** ADR 0014 defines it as setting the due time to now, and that
  works unchanged: on a work-set routine it forces an immediate re-check, and
  with an empty set it completes as a no-op that says so. One control, not two.
- **The restart spread.** ADR 0014 spreads overdue routines across the smaller
  of their interval and five minutes so an update does not fire everything at
  once. It does not apply here — a work set is the truth immediately after a
  restart, and the lanes being serial is what bounds the burst.

## Considered options

**A second kind of routine row, woken by work rather than by a clock.**
Rejected: it splits ADR 0014's one table, which is the table ADR 0018 reads to
draw the whole page, and it buys nothing — the cadence column already means
something sensible for these six once it is read as an idle tick.

**A wake signal from the writer of the row.** Rejected under *why the poll is
the mechanism*: it cannot survive a restart, so the poll exists anyway, and its
failure mode is a row that waits forever in a state that looks healthy.

**A durable outbox the writer appends to and the routine drains.** The version
of the signal that does survive a restart. Rejected: it is a second table
carrying exactly the information the state column already carries, and keeping
the two in step is a new class of bug in return for saving up to thirty seconds.

**Let a run drain its set to completion instead of yielding.** Rejected: the
artwork backfill would hold the bulk lane against screening for hours, which is
the head-of-line blocking ADR 0026 argued the file lane out of the bulk lane to
avoid, reproduced one level down.

**Record an empty tick as a success.** Rejected under *an empty tick is not a
run*: it redefines *last success* as a liveness check and hides a stopped
routine behind a moving clock.

**Show *last success* for these six and accept that it ages.** Rejected: it puts
a week-old timestamp under a stage that is working, which is precisely the
false alarm ADR 0018 spent its argument preventing.

**Raise a Gap when a work set has been non-empty for some time.** Rejected: it
is a clock declaring a stall, which ADR 0016 refused for downloads and ADR 0018
refused for the page. A set that is not draining is either failing — in which
case ADR 0014's Gap already fires — or deferred by the governor, which ADR 0014
already reports.

**Keep the backwards search's position.** Rejected under *no routine in this
family keeps a position*: a needle added during a pass would sit behind the
position and never be searched, which is ADR 0015's silently skipped row one
layer up.

**Make the idle ticks settings.** Rejected: ADR 0014's second argument survives
even though its first does not. There is no correct value and no observable
effect beyond how often a count runs.

**Give the file lane a progress display.** Rejected, as ADR 0026 rejected it:
what is shown is a name and a *since*, which is the same thing ADR 0016 shows
for an outstanding download and for the same reason — the person sees the stall,
and the tool does not claim it.

## Consequences

- `CONTEXT.md` gains **Work Set**, and **Routine** is sharpened: it already said
  a restart continues a routine "either from a position it carries or from a
  work set it can ask for again", and this decision settles which routines are
  which and makes the second the normal case.
- **ADR 0014 is amended.** Its cadence column carries two meanings, its
  resumable position narrows to the one-shot routines, and its table gains the
  six with their idle ticks. Backoff, the Gap threshold, the scarcity order,
  *run now* and the restart spread are all unchanged.
- **ADR 0018 is amended.** A work-set routine is drawn as two facts, the set
  size and the last completed item, and an empty set is neither a Gap nor a
  Brake. Its *Match* line becomes an instance of the rule rather than an
  exception to it.
- **ADR 0025 is amended.** The backwards search's position becomes a state on
  the needle, which is where that ADR's own argument about batches was already
  pointing.
- **ADR 0026 is amended** twice: the file lane's run is visible from the
  `Filing` row it already writes, and a failed arriving file sorts to the back
  of the work set.
- **The data model gains one column** — when a work-set item was last attempted
  — and a flag beside each normalised needle, which
  [ticket 32](../../.scratch/first-release-spec/issues/32-what-the-data-model-is.md)
  now inherits. It **removes** the resumable position from three routines that
  the fog patch had been carrying as ones that keep one.
- **The idle cost of the whole family is six indexed counts per tick.** Stated
  as a number rather than as a reassurance, because it is the price of refusing
  the signal.
- Nothing here is a setting, and nothing here is a surface.
