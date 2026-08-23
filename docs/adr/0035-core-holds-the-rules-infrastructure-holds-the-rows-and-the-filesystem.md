# Core holds the rules, Infrastructure holds the rows and the filesystem

Four projects, one reference direction, and one rule the compiler enforces:
nothing outside `Prdb.Fab.Infrastructure` reaches a socket, a database or a
file. `prdb-ordeno` sliced its `src` the same way before its first feature (its
ADR 0012), and that shape is adopted rather than re-derived. This decision argues the three places where this tool's
shape could have changed it — the schedule, the governor and the file lane —
and finds that none of them does.

```
src/Prdb.Fab.Core             the rules; no I/O, no package reference,
                              no project reference
src/Prdb.Fab.Infrastructure   EF Core and SQLite, the filesystem, Prdb.Sdk,
                              Prdb.Hashing, SABnzbd, Newznab, ffprobe
src/Prdb.Fab.Host             ASP.NET Core: HTTP, authentication, static assets,
                              the lane workers, composition
src/Prdb.Fab.Frontend         React

tests/Prdb.Fab.Core.Tests
tests/Prdb.Fab.Infrastructure.Tests
tests/Prdb.Fab.Host.Tests
```

`Core` ← `Infrastructure` ← `Host`. Nothing under `src/` references `Host`; a
test project may. The solution is `Prdb.Fab.slnx`, and what the frontend is
built with is [ticket 02](../../.scratch/build-foundation/issues/02-what-the-frontend-is-built-with.md).

## Why the boundary is worth having before there is code to constrain

Boundaries drawn before there is code are guesses, and guessed boundaries get
worked around. `prdb-ordeno` overrode that for one reason, and the reason is
stronger here: `VISION.md` makes files irreplaceable, and this tool moves and
deletes them **unattended**, which that one never does. In a single project an
HTTP endpoint that calls `File.Move` is one line away and nothing but review
stands between the code and that line.

The first release has no dry run — `VISION.md` gives the preview and the undo to
scan directories, which come later — so the boundary cannot be justified the way
`prdb-ordeno` justified it, as the thing that makes "ask a write path what it
would do" the same code path as doing it. It is justified by the operation log
instead, which **is** in the first release
([ADR 0029](0029-the-operation-log-records-one-act-per-video-file-and-nothing-reads-it-back.md)):
one entry per video file moved, relabelled, replaced or deleted, naming who
acted and why. An act that can happen from anywhere is an act the log can miss
from anywhere. Funnelling every one of them through one project is what makes
the log's completeness a property of the layout rather than of everyone
remembering.

## The three places this tool could have differed, and did not

### The schedule is not a project

[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md) and
[ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)
put four lanes, some twenty routines, scarcity order, backoff, work sets and
bounded runs in one place, and half the tool runs inside them. That looks like a
fifth project and is not one, because the schedule is not one kind of thing. It
splits cleanly along the boundary that already exists:

- **`Core`** — what a routine is, its lane, whether its cadence is a clock or an
  idle tick, the due-ness rule, the backoff arithmetic and its cap, the
  three-failure Gap threshold, ADR 0032's run bounds, ADR 0014's scarcity order
  and its restart spread. Every one of these is a function of values, and every
  one is testable without a database, a clock or a socket. They are also the
  ones that would otherwise never be tested at all, because their failure mode
  is a schedule that runs and quietly does the wrong thing.
- **`Infrastructure`** — each routine's work set, which
  [ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)
  makes an indexed `COUNT` over a state column, and each routine's work, which
  is by definition a request or a write.
- **`Host`** — the lane workers, thin, as `prdb-ordeno` keeps its own.

A fifth project would have to contain all three of those or split them anyway.
It contains them only if the dependency rule is loosened, which is the one thing
this decision exists to keep.

### The governor is a Core decision applied in Infrastructure

ADR 0014 makes every prdb request pass the governor, including one a person
triggered
([ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)),
and ADR 0030's artwork fetch pass nothing at all. So it is neither a client's
private business nor a rule that reaches for a socket, and the ticket was right
that it sits awkwardly. It sits on the boundary rather than across it: **the
decision — defer or send, given the last rate limit read and the request's
scarcity class — is `Core`; reading the limit off the response and holding the
connection is `Infrastructure`.**

That is a placement and not a mechanism.
[Ticket 04](../../.scratch/build-foundation/issues/04-how-a-lane-is-implemented.md)
chooses the mechanism, and this decision constrains it in one way: the governor
may not live in `Host`, because a search a person triggers must reach it by the
same route a routine does, and a policy applied at call sites is one a call site
can be added without.

### The file lane is where the boundary pays for itself

[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md)'s rules —
verify a copy by size and `osHash` computed fresh on both sides, write the video
file last, record the intended path before touching disk — are decisions and not
plumbing, and this is the one place where getting the split wrong would be
expensive.

**The order is `Core`'s.** It produces the steps of a filing: create the entry
directory, write the sidecar, write the entry image, relabel an already filed
file where [ADR 0017](0017-the-filed-path-is-computed-once-and-then-recorded-rather-than-recomputed.md)
requires it, and the video file last. Across a filesystem: copy to
`.filing-<download id>.part`, verify, rename, delete the source, and only then
`Filed`. Each of those is a rule with a stated reason, and each is checkable
without a disk.

**Every step is `Infrastructure`'s to perform**, including the `osHash`, which
comes from `Prdb.Hashing` and never from `Core`. `Core` says *which two values
must agree*; it never computes one.

The recovery rule ADR 0026 states — if the intended path holds our bytes the
source is deleted and the row becomes `Filed`, if it holds nothing the move
starts over — is the same shape and splits the same way, which is what makes
[ADR 0034](0034-the-container-is-given-what-it-needs-before-it-starts-and-nothing-else.md)'s
ten-second stop safe in a place a test can reach.

## The schema is a persistence model, and Core sees projections

The entities are one class per table from
[ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md),
in `src/Prdb.Fab.Infrastructure/Persistence/`, beside the `DbContext`, the
migrations and the design-time factory. **`Core` never sees a row.** It is
handed narrow records named for what the rule reads.

The tempting alternative was to take ADR 0033's headline literally — the schema
is the glossary made physical, so one type per term, in `Core`, with EF
configuring it from `Infrastructure`. It is rejected on three counts:

**What looks like a mapping layer is not one.** `prdb-ordeno`'s
`FiledCopy(VideoId, Directory, FileName, QualityLabel)` is a four-field
projection of a ten-column row, not a parallel copy of it. There is nothing kept
in step by hand, and the cost people fear when they hear "two models" is the
cost of a 1:1 mirror, which this is not.

**Twenty-four tables, and fewer than half carry a rule.** `FeedCursor`,
`RoutineRun`, `IdentificationOutcome`, `CatalogueImage` and their relatives have
no decision that reads them. Entities in `Core` would put every one of them
there regardless, and a `Core` that is half a schema dump has stopped saying
what the tool decides.

**An EF entity is mutable.** A rule handed one can write to it, and the only
thing standing against that would be a convention. The whole point of the slice
is that the compiler holds the line, and a boundary that holds for the
filesystem and not for the rows is one that will be explained away exactly once.

The price is real and worth stating: a glossary term then has two physical names
— the table and the projection — and `prdb-ordeno` has already drifted there,
where `FiledVideo` and `FiledCopy` describe one thing under two names that no
longer read as the same word. So the rule from ADR 0033 extends: **a projection
in `Core` that shadows a glossary term may not take a word from that term's
`_Avoid_` list either.** The mechanical check reaches both projects.

ADR 0033's export class and account class are declared where the table is, since
both are properties of tables. The rule they serve — an exported row references a
non-exported one only through an outside authority's identifier — is a property
of the schema as a whole rather than of any class, and
[ticket 08](../../.scratch/build-foundation/issues/08-what-is-tested-and-how-time-is-read.md)
already claims it as the one thing worth asserting mechanically.

## The filesystem, and the one departure from `prdb-ordeno`

`Core` may use `System.IO.Path` and nothing else from `System.IO`. Path
arithmetic is string arithmetic, and ADR 0017's filed path is computed in `Core`
from what prdb said. `File`, `Directory`, `FileStream`, `FileInfo` and
`DirectoryInfo` are `Infrastructure`'s alone.

`prdb-ordeno`'s architecture test reads project files rather than compiled
assemblies, because a declared but unused reference compiles away and is exactly
the one that slips through. That reasoning holds and the test is adopted — but
it cannot see this rule, because `System.IO` is the base class library and
appears in no `.csproj`. So the test **also reads source**, and that is the only
departure this decision makes.

It matters more than it sounds. The reference rule and the filesystem rule are
the two halves of the same boundary, and enforcing one while merely agreeing the
other is how the agreed half is discovered to have been broken for a year.

## Core declares no package references, and therefore does not log

`prdb-ordeno` holds this and it is adopted, with one thing said out loud rather
than discovered: it means no `ILogger` in `Core`.

Nothing forces the reference. `Confidence` is the case that looked like it
would: [ADR 0006](0006-acting-alone-needs-a-named-video-and-an-allowed-confidence.md)
names the six values directly and requires them to be matched as a set rather
than compared, and `Prdb.Sdk` returns the field as a plain `int?` with no enum —
so `Core` owns the named set and `Infrastructure` translates the number into it,
which is what that ADR wanted anyway.

Whether a rule needs to say something into a log is
[ticket 09](../../.scratch/build-foundation/issues/09-how-a-failure-is-expressed-and-logged.md)'s
question. If the answer needs `Microsoft.Extensions.Logging.Abstractions` in
`Core`, that is a departure for that ticket to argue. It is not pre-empted here,
in either direction.

## Tests may reference the host

`prdb-ordeno` amended its own rule for this (its ADR 0015) and the argument
transfers with a stronger case attached. What
[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)
promises — that the setup path closes the moment a password exists and
everything else is behind the cookie — is not a property of any class. It is a
property of how the application is composed, and a test that recreates that
composition proves its copy is right, which is worse than no test because it
reads like one.

So: no project under `src/` references `Prdb.Fab.Host`; a test project may, and
may not replace a service with a double to get past the wiring it exists to
check. `Prdb.Fab.Core.Tests` carries the architecture tests.
`Prdb.Fab.Infrastructure.Tests` runs against real SQLite and a real temporary
directory, because a half-finished cross-device copy is not a thing a mock can
have. What is actually tested, and how the clock is read, is ticket 08.

## The build files

Adopted from `prdb-ordeno` and treated as write-path hygiene rather than style,
which is how that project describes the first two:

- `Directory.Build.props` — `net10.0`, `Nullable` enabled, `ImplicitUsings`
  enabled, `LangVersion` latest, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`.
- `Directory.Packages.props` — central package management on, every version
  declared once. Which packages are in it follows from tickets 02, 05, 07, 08
  and 10.
- `global.json` — the SDK pinned, `rollForward: latestFeature`, no prerelease,
  which is what [ADR 0004](0004-the-stack.md) means when it names `dotnet build`
  and `dotnet test` as the verification commands.

## Considered options

**A fifth project for the schedule.** Rejected under *the schedule is not a
project*: it would have to hold rules, queries and a host loop together, which
it can only do by loosening the dependency rule this decision exists to keep.

**A separate `Persistence` project beside `Infrastructure`.** Rejected as
`prdb-ordeno` rejected it, and the reason survives twenty-four tables: the
database and the filesystem are both adapters behind the same kind of interface,
and splitting them buys a project boundary for a distinction nothing needs. It
becomes arguable if migrations grow tooling of their own.

**Entities in `Core`, EF configuring them from `Infrastructure`.** The literal
reading of ADR 0033, and the one this decision was closest to taking. Rejected
under *the schema is a persistence model*: half the tables carry no rule, an EF
entity is mutable, and the boundary would hold for files and not for rows.

**One project, split at the first feature** — the advice this would follow in
most repositories. Rejected under *why the boundary is worth having*: the tool
deletes files unattended and the operation log has to be complete, and deciding
that a rule matters while leaving its violation one line away is the kind of gap
found in a bug report.

**Keep the architecture test to project references, as `prdb-ordeno` has it.**
Rejected under *the one departure*: it cannot see the filesystem rule at all,
and half an enforced boundary is a boundary nobody is checking.

**Let `Core` reference `Prdb.Sdk`,** so identification results need no
translation. Rejected: `VISION.md` makes the SDK the only door to prdb, not a
type system the domain speaks, and the one place it looked necessary —
confidence — turns out to be an `int?` that ADR 0006 wanted named locally
regardless.

## Consequences

- **A reference in the wrong direction fails the build**, and so does a `File`
  or `Directory` call outside `Infrastructure`. Both are checked in
  `Prdb.Fab.Core.Tests`, one by reading project files and one by reading source.
- **`Core` has no `ILogger`**, which ticket 09 inherits as an open question
  rather than a settled prohibition.
- **A glossary term has two physical names** — the table and, where a rule reads
  it, the projection. The `_Avoid_` check from ADR 0033 now covers both, which
  is the only defence against the drift `prdb-ordeno` already shows.
- **Ticket 04 is constrained and not answered**: the governor's decision is
  `Core`, its socket is `Infrastructure`, and it may not be in `Host`. What a
  lane *is* at runtime remains that ticket's.
- **Ticket 05 is constrained and not answered**: the `DbContext`, the migrations
  and the design-time factory are in `Infrastructure/Persistence`. How the
  database is opened, and where migrations run, remain that ticket's.
- **`CONTEXT.md` is unchanged**, for ADR 0034's reason: a project is an
  artefact, not a concept the language needed.
- **The boundaries may still be wrong.** Moving a type between projects is cheap
  while the code is small, so this is revisited at the first feature that fights
  it. The failure to avoid is quietly bending the dependency rule to keep the
  diagram intact.
- **A fifth project is a decision, not a habit.** This layout is the floor.
