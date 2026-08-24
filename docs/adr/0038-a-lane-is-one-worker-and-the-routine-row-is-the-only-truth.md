# A lane is one worker, and the routine row is the only truth about what is due

Four decisions, and deliberately no more. What a lane *is* had to be settled
because the alternatives are expensive to unwind; how a lane is *written* is the
skeleton's work, and this decision says which parts of
[ticket 04](../../.scratch/build-foundation/issues/04-how-a-lane-is-implemented.md)
it is deliberately leaving to it.

[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md) and
[ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)
specified lanes and routines completely as behaviour and not at all as
mechanism.
[ADR 0035](0035-core-holds-the-rules-infrastructure-holds-the-rows-and-the-filesystem.md)
placed the pieces — the due-ness rule, the backoff arithmetic and the run bounds
in `Core`, the work sets and the work in `Infrastructure`, the lane workers thin
in `Host` — and named this the ticket that chooses what a lane is at runtime.

## A lane is a worker of its own, not a lock around many

One hosted service per lane: one class, four registrations, each parameterised
with its lane. Its loop reads the routines of that lane, picks the one that is
due, runs it, and repeats.

The alternative was a hosted service per routine — around twenty — with a
semaphore per lane enforcing that only one runs at a time, which is the shape
`prdb-ordeno` uses for its `LibraryGate`. It is rejected because
**ADR 0032's round-robin cannot be expressed in it.** That ADR requires a lane
to alternate between its routines rather than let one drain, and a semaphore
grants entry to whoever arrives first: it can make runs serial, but it cannot
make them take turns. Choosing which routine goes next is a decision somebody
has to make, and there has to be somewhere for it to be made.

With one worker per lane there is nothing to lock, because there is only one
thread of execution in the lane. *One routine at a time* stops being a promise
the code keeps and becomes a property of the structure.

`CONTEXT.md` already pointed here and it is worth noting rather than claiming as
proof: **Lane** is defined as one of the queues the routines are divided
between, and its `_Avoid_` list carries *Worker, Thread, Pool, Channel*. The
worker carries the lane; it is not the lane.

## The routine row is the only truth about what is due

The lane reads its routines from the database on every pass and writes the
outcome back. It holds no schedule in memory.

This is what makes ADR 0014's *run now* honest. That ADR defines it as setting
the routine's due time to now **and nothing else**, so that a forced run still
passes the governor like any other. A schedule living in memory would need the
write *and* something to tell the schedule about it — which is the second path
that definition exists to refuse. It also keeps the status page
([ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md))
reading the same row the lane reads, rather than a copy of it.

It is ADR 0032's own argument one level out. That ADR removed the resumable
position from six routines because *a position is a second place the truth
lives, and it can disagree with the rows*. An in-memory schedule is the same
thing for the same reason.

The price is a small read per lane per pass over a table of around twenty rows,
which is stated rather than waved at.

## An interrupted run is neither a success nor a failure

ADR 0034 relies on the application receiving `SIGTERM` as PID 1 and on ten
seconds being enough, which is true only because
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md)'s recovery
rule reads the intended path off the row. The runtime side of that is what a
cancellation does to the bookkeeping.

A cancelled run **does not touch backoff and does not touch the consecutive
failure count.** Reading an interrupted run as a failure would mean that
updating the container three times raises a Gap on an installation where nothing
is wrong — precisely the false alarm ADR 0018 spent its argument preventing, and
it would fire on the one action every user performs regularly.

It is **recorded in the run log as a third outcome**, because a filing that ran
for three hours and was interrupted by a restart is what somebody goes to that
log to find, and the alternative is that long runs vanish without trace. This is
distinct from ADR 0032's empty tick, which is not recorded at all: an empty tick
is a routine that had nothing to do, and an interrupted run is a routine that
was doing something.

## Before onboarding, a missing setting is a Brake

The lanes start with the application and run from the first minute. A routine
that cannot run because a setting only a person can supply is missing reports a
**Brake** — never a failure, so never backoff and never a Gap.

ADR 0014 states that bootstrap is not a state of the application, and a gate
holding the lanes shut until onboarding completes would be exactly that state.
Treating it as a failure instead is worse: three ticks after a fresh install,
before anybody has typed a prdb key, the status page would show Gaps on a tool
whose only condition is that it has not been set up yet.

`CONTEXT.md` already holds the term. A **Brake** is something held back by
configuration, never a Gap, and this is the same category as an indexer somebody
switched off — which is what
[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)'s
onboarding is, seen from the loop's side.

## What this decision deliberately does not settle

The rest of ticket 04 is mechanism, and mechanism is decided by writing it. It
belongs to [ticket 11](../../.scratch/build-foundation/issues/11-the-walking-skeleton.md),
with one exception noted below:

- **How the loop waits** when nothing is due, and at what resolution due-ness is
  read. The four numbers that matter are already fixed by ADR 0014 and ADR 0032;
  this is the clock they are read against.
- **How a row finds its code.** A routine's name is a value; a routine's row
  also carries a target, since the indexer walk and the wanted sweep exist once
  per indexer and the one-shot routines are created at runtime.
- **What a run returns**, and therefore how ADR 0032's *an empty tick is not a
  run* is expressed without being said twice.
- **The governor's mechanism.** ADR 0035 placed it and pointed here; it moves on
  to [ticket 07](../../.scratch/build-foundation/issues/07-how-outbound-http-is-done.md),
  which is where outbound HTTP is settled and therefore where it belongs. One
  constraint travels with it, from ADR 0026: the governor may not be a wait
  inside a lane, because a routine waiting on it holds a lane in order to do
  nothing.

**Nothing needs a claim on a row.** ADR 0026's chain moves an arriving file
through states that each belong to exactly one lane, so ownership follows the
state and two lanes cannot hold one row. A claim column would be a second
truth about who holds what, and it would have to be cleaned up after a crash.

## Considered options

**A hosted service per routine, with a semaphore per lane.** Rejected under *a
lane is a worker of its own*: a semaphore serialises but does not take turns,
and ADR 0032 requires taking turns.

**One hosted service starting four tasks.** Rejected: it is the chosen shape
with the per-lane failure isolation given away and nothing bought.

**A schedule held in memory, written out for the status page.** Rejected under
*the routine row is the only truth*: it makes *run now* two acts instead of one,
and it is the second-place-the-truth-lives that ADR 0032 removed.

**Read an interrupted run as a failure.** Rejected: every container update would
move the failure count towards a Gap on a healthy installation.

**Hold the lanes shut until onboarding completes.** Rejected: ADR 0014 says
bootstrap is not a state of the application, and this would make it one.

## Consequences

- **`CONTEXT.md` is unchanged.** A hosted service is an artefact rather than a
  concept the language needed, and **Lane**, **Routine**, **Work Set**,
  **Brake** and **Gap** already say everything this decision uses.
- **ADR 0035's placement holds**, and the one thing it left open here — what a
  lane is at runtime — is now fixed. The governor's mechanism moves to ticket 07
  rather than being answered here.
- **The map's remaining tickets are decided at library-and-contract depth**, and
  the mechanism under them is the skeleton's. This decision is the first one
  taken that way, and it is why ticket 04 resolves in four points rather than
  nine.
- **This is the floor, not the ceiling.** A lane worker is small enough that
  moving it is cheap, and the first routine that fights this shape is the reason
  to revisit it.
