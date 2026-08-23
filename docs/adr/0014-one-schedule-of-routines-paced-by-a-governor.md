# One schedule of routines, paced by a governor

Everything the tool does on its own is a **routine** in one table, with its own
cadence, its own resumable position and its own record of when it last
succeeded. Cadences are fixed numbers rather than numbers derived from the rate
limit, and a **governor** defers a prdb request when the budget is short. The
schedule covers more than the sync: matching, repair, artwork, reporting and
identification are routines too, because "is anything broken" is a question
about all of them.

## Why fixed cadences and not derived ones

The numeric value of prdb's `hourly.limit` is in no document. It is read from
the response headers at runtime, which invites a schedule that sizes itself
against whatever the key turns out to allow. That is rejected. A derived
schedule makes two installations on the same plan behave differently, and makes
"why did it not poll" unanswerable — the tool would be reasoning about a number
nobody can see. A fixed table plus a governor degrades visibly and identically
everywhere.

The idle profile is about nine requests an hour:

| Routine | Cadence |
|---|---|
| What's New (`GET /videos`, `CreatedAfter`) | 15 min |
| `/videos/images/changes` | 30 min |
| `/wanted-videos/changes`, `/favorite-sites/changes`, `/favorite-actors/changes` | 60 min |
| `/actors/changes` | 6 h |
| `GET /sites` with `If-None-Match` | 24 h |
| Repair (`POST /videos/batch`) | budget, not cadence — see below |
| Indexer walk, per indexer | 15 min |
| Wanted sweep, per indexer | 15 min |
| SABnzbd, while anything is outstanding | 5 s |
| SABnzbd reachability, while nothing is | 5 min |

The five feeds are the five ADR 0013 left standing. Repair keeps the shape that
ADR left it — steered by a budget — and the number is now fixed: it may spend
whatever holds hourly usage under half of `hourly.limit`, and at least one
request per run so a small plan still makes progress.

## Why a wanted sweep exists at all

An indexer walk sees only what is new. A video wanted for three years was posted
long ago and sorts hundreds of pages down, and no implementation can order
results by when it was indexed, so the walk will never reach it — not slowly,
never. Without a second routine that searches for wanted videos by name, the
tool cannot find anything that predates its installation, which is most of what
a wanted list holds.

It sweeps the least-recently-searched wanted videos, five per run per indexer.
That is twenty an hour, so a list of five hundred comes round daily, and it is
far and away the most expensive thing the tool does against an indexer — which
is what the per-indexer daily query budget exists to bound. `VISION.md`'s
promise that indexers are not searched live survives: the scheduler searches,
the cache answers, and the user waits for neither.

## Why intervals are not settings

An interval is a control with no correct value, and every number above follows
from a budget the tool reads for itself. Exposing them invites a user to break
their own rate limit and then report it as a bug. Two controls remain, both
because the tool genuinely cannot know the answer: a **daily query budget per
indexer**, since Newznab quotas belong to the user's account and three of the
five surveyed implementations report nothing at all, and enable/disable per
indexer, which `VISION.md` already promised.

## Why three lanes

One worker would put a five-second obligation behind a repair pass that runs for
minutes. Routines run in three serial lanes instead — **live** (SABnzbd polling
and reachability), **sync** (prdb feeds, indexer walks, the wanted sweep) and
**bulk** (backfills, repair, restore verification). Each lane runs one routine at
a time, so each stays predictable. ADR 0004's single writer is untouched: writes
are short and batched, and no transaction spans an HTTP call.

(*Amended by
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md), which adds
a fourth lane, **file**, holding filing alone. The argument above is made
against minutes; a cross-filesystem copy of a 40 GB release is hours, and the
bulk lane holds collecting, so filing there would stop the tool noticing that
any other download had finished for as long as one large move ran. That ADR also
adds two routines — identifying arriving files (sync) and filing (file) — whose
cadences it deliberately does not fix, because what makes each of them due is
the arrival of rows rather than a clock.*)

## Consequences

- **Backoff and Gap are different mechanisms.** Backoff is the routine's own
  interval doubled per failure, capped at an hour, reset on success; a `429`
  with `Retry-After` overrides it exactly, and prdb's fail-closed `503` is
  ordinary backoff. A **Gap** is raised after three consecutive failures and
  carries the age of the last success, which is the job ADR 0013 gave it. A
  permanent refusal — prdb `403` or `401`, an indexer rejecting its key — is a
  Gap at once and stops the routine, since retrying a settled answer buys
  nothing.
- **A plan too small for the schedule is a named condition, not starvation.**
  If the discovered limit cannot carry the idle profile, load is shed in a fixed
  documented order — actors to 24 h, images and What's New to 60 min, repair to
  its minimum — and a Gap says the plan does not carry the schedule. Without
  this the governor would defer everything forever while nothing ever failed:
  the same silent-failure shape ADR 0006, 0007, 0008 and 0010 each contributed
  one of to the sync status page.
- **Scarcity has a fixed order of precedence**: `POST /videos/identify` first,
  because a file that has arrived is waiting on it to be filed; then writes,
  which are rare and which ADR 0013 already queues; then the user feeds, What's
  New, images, actors, sites; then repair.
- **A restart spreads its overdue routines** across the smaller of their own
  interval and five minutes, so an update does not fire every routine at prdb
  and every indexer at once. The live lane is exempt and starts immediately,
  because a download in flight must be picked up at once. A routine that died
  mid-run resumes from its position.
- **An indexer walk that hits its paging ceiling before reaching its watermark
  knows the window it missed**, and creates a one-shot catch-up routine over
  exactly that window using `maxage=`, which filters on the field the results
  are sorted by. It is not a Gap — nothing is broken, it is merely unfinished,
  the distinction ADR 0013 drew for the backfill. This is only schedulable
  because it is a row rather than a log line.
- **Bootstrap is not a state of the application.** The first walk of an indexer,
  the What's New backfill and the actors drain are one-shot routines that carry
  a position and retire when done, running beside the recurring ones from the
  first minute rather than before them.
- **Running something now is not a second path.** "Run now" sets the routine's
  due time to now and nothing else, so a forced run still passes the governor
  and is deferred under pressure like any other — otherwise the one control
  holding the rate limit would be the one control a person can bypass.
- **The schema gains two tables**: a routine, holding cadence, position, lane,
  last success, last failure and consecutive failures; and a run log capped at
  the last fifty runs per routine. Neither is exported — ADR 0009 takes only
  what cannot be fetched again, and a run log refetches itself by running.
