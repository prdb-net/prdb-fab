# Nothing reads the clock directly, and the network is replaced at the socket

`prdb-ordeno`'s test tree is adopted with one upgrade and one departure: xUnit,
`Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing` for the host, real
SQLite, a real temporary directory, hand-written fakes, and architecture tests
that read project files rather than assemblies.
[ADR 0035](0035-core-holds-the-rules-infrastructure-holds-the-rows-and-the-filesystem.md)
had already fixed where those live and that a test project may reference the
host.

What is decided here is the clock, what stands in for four remote things, and —
as a deliverable rather than an afterthought — what is **not** tested.

## The test tree, and what is deliberately absent

**xUnit v3** (4.0.0), which is the one upgrade: a project starting now has no
reason to begin on the previous line, and nothing else in the tree depends on
the difference. `Microsoft.Extensions.TimeProvider.Testing` joins it for the
next section.

**No mocking library**, and this is the substantial half. ADR 0035 states the
rule it would undermine: a test project may drive the composition root but *may
not replace a service with a double to get past the wiring it exists to check*.
A framework that offers exactly that in one line sits against that rule, and the
line is short enough that nobody notices it being crossed. Hand-written fakes
like `prdb-ordeno`'s `FakePrdb` are longer and say what they are pretending to
be, in a place a reader can see.

**No assertion library.** The language is enough, and in a tree whose rules are
held by architecture tests reading source, every additional package is another
thing those tests cannot see.

## Nothing reads the clock directly

`TimeProvider` is injected, `TimeProvider.System` is registered, `FakeTimeProvider`
stands in for it in tests. That much is `prdb-ordeno`'s arrangement and is not
in question.

**The decision is the architecture test that forbids a direct
`DateTime.Now`/`DateTimeOffset.UtcNow`**, reading source the way ADR 0035's
filesystem test does — because a system clock, like `System.IO`, appears in no
`.csproj` and is therefore invisible to a test that reads references.

The evidence that it is needed is next door and worth recording rather than
asserting in the abstract. `prdb-ordeno` injects `TimeProvider` into every worker
and every service it has — and still calls `DateTimeOffset.UtcNow` directly in
one place, in `AccessEndpoints`. One site, in a project that holds the rule
deliberately. The rule does not survive on agreement.

What that buys is not tidiness. Several decisions here are *about* elapsed time
and are untestable without a clock that can be moved: ADR 0016's **no clock** —
nothing is declared stuck by elapsed time —
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)'s
liveness line and seven-day gate tallies,
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)'s backoff and
restart spread,
[ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)'s
idle ticks,
[ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)'s
rule that nothing in the review queue expires, and
[ADR 0029](0029-the-operation-log-records-one-act-per-video-file-and-nothing-reads-it-back.md)'s
log, ordered by time and pruned by nothing. Each of those is a claim that a
single direct clock call can quietly exempt itself from.

## The network is replaced at the socket, never at an interface

Fakes are `HttpMessageHandler`s. Then the SDK, the client each caller builds,
the timeout, the redirect rule and the mapping from a status code to a sentence
a person reads all run for real, and what is replaced is the network under them
— which is `prdb-ordeno`'s own reasoning for `FakePrdb` and the reason those
tests are worth having.

[ADR 0041](0041-nothing-retries-inside-a-request-and-only-the-cdn-follows-a-redirect.md)
makes this concrete rather than aspirational: there are exactly four named
transports, so there are exactly four places a fake goes in, and no argument
about where one belongs.

**Newznab is fed from recorded real responses; the other three are
hand-written.** This is the departure, and it is earned by a finding rather than
by preference: the Newznab research surveyed **five implementations** that differ
in error codes, in which fields they return, and in whether their advertised
caps mean anything. A hand-written XML fixture tests the implementation the
author imagined — which is the one the code already works against. prdb is one
server whose behaviour is known, SABnzbd is one application, and the CDN is bytes
with a content type; hand-writing those costs nothing and stays readable.

The three research documents under `.scratch/first-release-spec/research/` are
the seed, as the ticket said. What is recorded is the *shape* — status codes,
headers, XML skeletons — never anybody's key and never a real download URL,
which
[ADR 0037](0037-credentials-are-stored-in-the-clear-because-there-is-nowhere-to-put-a-key.md)
established carries a credential in its query string.

## The filesystem is real, and one thing about it is admitted rather than faked

ADR 0035 already put `Prdb.Fab.Infrastructure.Tests` against real SQLite and a
real temporary directory, and
[ADR 0039](0039-sqlite-is-opened-in-wal-and-its-pragmas-are-set-on-every-connection.md)
confirmed there is nothing else worth running SQLite against. What was open is
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md)'s
cross-filesystem path, which cannot be provoked on one volume.

**The expensive branch is called directly.** ADR 0035 puts the choice — rename
or copy — in `Core` as a rule, and both executions in `Infrastructure`. So a test
invokes the copy path with two directories on one volume and exercises what
actually matters: verification by size and a fresh `osHash` on both sides, the
`.filing-<download id>.part` intermediate, the video file written last, and
ADR 0026's recovery after an interruption — the rule that makes
[ADR 0034](0034-the-container-is-given-what-it-needs-before-it-starts-and-nothing-else.md)'s
ten-second stop safe.

**What is not tested is that `File.Move` across devices actually fails.** That is
a property of the kernel, not of this code. Mounting a loop device in CI to
observe it would test an operating system and would tie the build to a privilege
it otherwise never needs.

## What is not tested, written down

The ticket asked for this and it is a deliverable, not a caveat. A suite that
never says what it declines to cover accretes coverage for its own sake and
still leaves nobody able to say what is unguarded.

- **`EXDEV` itself**, as above.
- **prdb, indexers, SABnzbd and the CDN over a real socket.** A test that talks
  to prdb fails when a subscription lapses, and one that talks to an indexer
  spends somebody's daily budget (ADR 0024).
- **ffprobe's output for real files.**
  [ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)
  reads a file once and lets what it reads decide nothing, so the parsing is
  worth testing and the media is not.
- **The frontend, beyond what its own build checks.** ADR 0040 makes a changed
  response shape a compile error through generated types, which is the check
  that pays; a component test tree is a second product.
- **That the image runs.** That is ticket 10's CI, not a test.

## The one property asserted mechanically

[ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md)
fixes the export boundary as a rule — an exported row references a non-exported
one only through an outside authority's identifier — and ADR 0035 already claimed
it for this ticket as the one thing worth checking by machine rather than by
review.

It joins the architecture tests, alongside the dependency direction, the
`System.IO` rule, the clock, and ADR 0033's `_Avoid_` check over both a table and
its `Core` projection. All of them share a property: they are rules whose
violation compiles, runs, and looks fine.

## Considered options

**xUnit v2, as `prdb-ordeno` has it.** Rejected weakly — there is no reason to
start on the previous line, and no reason beyond that to move.

**A mocking library.** Rejected under ADR 0035's rule against doubling out the
wiring a test exists to check.

**Fakes at an interface instead of at the socket.** Rejected: the SDK, the
timeout, the redirect rule and the status-code mapping would then all be
untested, and those are where the failures live.

**Hand-written Newznab fixtures.** Rejected on the research's five
implementations: the fixture would encode the assumption the code already shares.

**Recording prdb and SABnzbd too, for consistency.** Rejected: consistency is not
the goal, and a recorded fixture is harder to read and to amend than a
hand-written one when the behaviour it captures is not in dispute.

**A loop device in CI for `EXDEV`.** Rejected under *what is not tested*: it
tests the kernel and needs a privilege the build otherwise does not.

## Consequences

- **`CONTEXT.md` is unchanged.** A test framework is an artefact.
- **`Directory.Packages.props` gains xUnit v3, the test SDK, the MVC testing
  package and `Microsoft.Extensions.TimeProvider.Testing`** — and deliberately
  no mocking or assertion package, which is worth a comment where somebody would
  otherwise add one.
- **The architecture tests are now five rules**, all of the same kind: violations
  that compile and run.
- **A clock call outside `TimeProvider` fails the build**, which is what turns
  ADR 0016's *no clock* from a sentence into a property.
- **Recorded Newznab responses are an asset the repository carries**, and they
  carry no credential.
- **Ticket 10 inherits the run**: what CI executes, and in what order, is that
  ticket's — this one decides only what exists to be run.
