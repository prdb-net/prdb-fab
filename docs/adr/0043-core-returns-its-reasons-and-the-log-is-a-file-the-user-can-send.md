# Core returns its reasons, and the log is a file the user can send

Two halves of one question, and they turn out to be the same half. A rule that
refuses has to hand its reason back, because `Prdb.Fab.Core` cannot write one
down; and a reason that is a value rather than a sentence is the only kind a
test can read and a caller cannot flatten. The logging half then has a single
job: put what a person needs somewhere they can reach without a shell.

Serilog with a rolling file on the data volume, two sinks, and the diagnostic
knob [ADR 0034](0034-the-container-is-given-what-it-needs-before-it-starts-and-nothing-else.md)
already published, unchanged.

## A failure is an exception, unless a decision reads it

No result library, no `Result<T>` convention over every call. Exceptions carry
what is broken; an explicit outcome type exists only where a decision reads the
outcome. `prdb-ordeno` settled the same way without arguing it — six `throw new`
in its whole `Infrastructure` project against fifty-six `catch (Exception)`, and
its outcome types (`FilingRun`, `IdentificationRun`, `RefreshRun`) are results
of a run, in `Core`, rather than a wrapper around every method.

What [ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md)
actually requires is narrower and sharper than results everywhere, and it is
worth stating as a rule of its own:

> **A null, or an empty collection, may never be the answer to a question that
> has a *we could not ask* case.**

That ADR names the collapse and prices it: found, request failed, and genuinely
absent are three outcomes, and folding the second into the third turns an
unreachable SABnzbd into a pile of vanished downloads and a pile of consumed
releases. A signature returning `SabnzbdJob?` **is** that fold, written into the
type system where nobody will see it again. Three named cases cost three types
and no dependency, and every one of them is a place where the compiler will not
let the caller forget.

The rule generalises past SABnzbd without needing a second argument: the
indexer that answered with zero results and the indexer that timed out are the
same distinction, and [ADR 0041](0041-nothing-retries-inside-a-request-and-only-the-cdn-follows-a-redirect.md)
has already fixed that a timeout is case 2 rather than a genuine absence. Where
that ADR settled it for the transport, this settles it for the signature.

## Core does not log

`Prdb.Fab.Core` takes no logging package, which under
[ADR 0035](0035-core-holds-the-rules-infrastructure-holds-the-rows-and-the-filesystem.md)
means no `ILogger` there. The sibling project is the evidence rather than the
authority: across thirty-four `ILogger` declarations, **not one is in
`Prdb.Ordeno.Core`**, and its project file is empty but for a comment saying
that a reference there is how the rule stops being enforceable.
[ADR 0042](0042-nothing-reads-the-clock-directly-and-the-network-is-replaced-at-the-socket.md)'s
architecture tests already fail the build when a package reference appears in
`Core`, so this needs no new mechanism.

The consequence is the point, not the cost. A rule that cannot log must return
its reason, and a returned reason is a value: the test asserts on it, the
caller decides whether it is worth a line, and the same reason can reach the UI
where a log line never could. `Microsoft.Extensions.Logging.Abstractions` in
`Core` would not be a small exception to ADR 0035 — it would be the comfortable
way around that constraint, and the rules would start explaining themselves into
a stream nobody reads instead of answering their caller.

## Serilog, two sinks, and a knob that already exists

**One package reference:** `Serilog.AspNetCore` 10.0.0, which brings `Serilog`
4.3.0, `Serilog.Sinks.Console` 6.1.1, `Serilog.Sinks.File` 7.0.0 and
`Serilog.Settings.Configuration` 10.0.0. Every one of them is Apache-2.0.

### Why a library here, when three other decisions took none

[ADR 0041](0041-nothing-retries-inside-a-request-and-only-the-cdn-follows-a-redirect.md)
took no resilience library and ADR 0042 no mocking or assertion library, so
this needs saying plainly rather than being left to look inconsistent. The test
those decisions were applying is not *avoid dependencies*. It is two questions,
and a library has to pass both:

1. **Does it do something the platform does not?** A retry policy and an
   assertion helper do not; a bounded, rotating file on disk does — the platform
   ships console, debug and event-source providers and no file provider at all.
2. **Is its licence one that can be taken away?** This repository is MIT. A
   dependency that later moves to a commercial clause is not an annoyance here,
   it is a forced removal, and the last few years have supplied enough examples
   in this exact ecosystem to make it a standing criterion rather than a worry.

Serilog passes both. It is Apache-2.0 across the packages named above, verified
package by package rather than assumed from the ecosystem, and it is a .NET
Foundation project. Its commercial counterpart is a *separate server product*
rather than a paid tier of the library, which is the structure that makes a
relicence least likely: the library is the funnel to the product, and closing
the funnel would be against the interest that pays for it. That is an argument
about incentives, not a guarantee, which is why the second half of the criterion
matters more than the first — see *Consequences*.

### The rolling file, and the argument that carries it

Console **and** file, not one or the other. The console sink is not a
concession: `docker logs` is where the platform, the NAS UI and every existing
piece of ADR 0034's documentation already look, and stdout is what someone with
a log collector has already wired up.

The file earns its place on a fact ADR 0034 established for another reason. The
user mounts `/data` themselves — it is the one thing they provision, and there
is deliberately no `VOLUME` declaration to make it optional. So a log file at
`/data/logs/` is **reachable from the host's own file manager**: no shell, no
`docker logs --since`, no knowing that `docker` has a log driver. For an
unattended tool aimed at NAS owners, that is the difference between *send me
your log* being a sentence and being a support session. It also means the first
release needs no download-the-log endpoint, and does not get one: the mount
already answers it.

**Bounded, and the bound is the decision.** Ten megabytes per file, rolling
daily and on size, ten files retained — a hard ceiling near a hundred
megabytes. Unbounded is not an option worth naming, because the log would share
a filesystem with the SQLite database and would eventually be the thing that
filled it. A hundred megabytes is a rounding error beside
[ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md)'s
two-gibibyte artwork ceiling, and it holds weeks at the default level and days
with the log turned up, which is the window a support question actually spans.

**Unbuffered, and no async wrapper.** `Serilog.Sinks.Async` trades the tail of
the log for throughput. The tail is the part that matters: the lines
immediately before a crash are the reason the file exists, and this tool writes
tens of lines a minute, not thousands a second.

**Plain text in both sinks**, one template, UTC timestamps, the category
included. Compact JSON is available in the package that is already referenced
and is not switched on, because ADR 0018 put an observability stack out of
scope and there is therefore no machine reader to serve. The reader is a person
skimming a file they are about to attach to a message — which is also a person
who can see what is in it before they send it.

### The knob keeps the shape ADR 0034 published

ADR 0034 documented exactly one diagnostic control,
`Logging__LogLevel__Prdb.Fab=Debug`, and made the entrypoint `bash` specifically
so that a variable with a dotted category survives dash. That is a published
operational contract, and Serilog is configured around it rather than the other
way round: **the logger is built in code and reads its levels from
`Logging:LogLevel:*`**, not from a `Serilog` configuration section.

Binding `Serilog.Settings.Configuration` to its own section instead would rename
the knob to `Serilog__MinimumLevel__Override__Prdb.Fab` and leave the documented
variable silently doing nothing — which is precisely the failure ADR 0034 spent
its `bash` paragraph preventing: a setting that does not arrive, and nothing
says so.

One Serilog-specific trap is closed with it. Serilog applies the pipeline
minimum *before* per-source overrides, so a floor of Information swallows a
`Prdb.Fab=Debug` override entirely and the knob appears broken. The floor is
therefore the lowest level any override configures.

## Five levels, and the rule that keeps the log readable

| Level | What is written there |
|---|---|
| **Information** | something changed: a run that did work, a download submitted, a file filed, a Gap raised, a setting altered |
| **Warning** | a failure the tool is handling itself and will retry |
| **Error** | something a person must fix, or an act that was given up on |
| **Debug** | why a run did **nothing** — the case ADR 0034 names as the hard one |
| **Critical** | the process cannot continue |

And the rule without which the levels do not help:

> **At Information a routine that did nothing is silent, and nothing is logged
> per item.**

`prdb-ordeno` already writes this way without stating it — its Information lines
are acts and settings changes, and where a run reports volume it reports it once
(*Asked prdb about {Count} files.*) rather than once per file. Here the rule has
to be written down, because the shape is different:
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md) has some twenty
routines across four lanes and the fastest ticks every five seconds. A line per
tick is thousands an hour, the rolling file above holds hours instead of weeks,
and the log stops being readable exactly when it is needed.
[ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)
already says an empty tick is not a run; this says it is not a log line either.

**HTTP requests are one line each, at Debug.** Serilog's request logging
replaces ASP.NET Core's several lines per request with one, which is worth
having, but not at Information:
[ADR 0036](0036-the-frontend-takes-two-dependencies-and-the-address-bar-holds-what-is-linkable.md)
put TanStack Query in front of a status page that polls, so an Information-level
request line is a steady drip for as long as a browser tab is open. At Debug it
also folds ADR 0034's second documented instruction — turn `Microsoft.AspNetCore`
up to see whether requests arrive — into the one knob that was already there.

## The run log, the stream, and what goes in neither

They overlap and are not the same thing, and the line between them is the
reader, not the content.

**A person is expected to act on rows, not on the stream.** ADR 0018 computes
Gaps and Brakes at read time out of rows that already exist, so a condition that
lives only in `docker logs` is invisible to the one page `VISION.md` sells as
the replacement for checking by hand. Anything asking to be fixed reaches a row
first, and the log line is the copy.

**The stream carries what the row has no room for**: ADR 0016's directory
listing of the nearest existing ancestor when a mapped path does not resolve,
the `stage_log`, which request failed and against which of ADR 0041's four
transports.

**The run log is neither.** Fifty rows per routine, three outcomes — succeeded,
failed and, per [ADR 0038](0038-a-lane-is-one-worker-and-the-routine-row-is-the-only-truth.md),
interrupted — read in the UI, not exported. It answers *did the thing run*. It
does not answer *what went wrong*, and it is not widened to.

**And what goes in neither.** Keys, the backup passphrase and the password are
never logged, at any level, Debug included, and never inside an exception
message. The rule that enforces it is not a filter list but a place:

> **A URL is never logged whole.** What is written is ADR 0041's transport name
> and the host; never the query string.

[ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md) records that a
download URL carries the indexer key, and ADR 0041 forbids that transport from
following redirects for the same reason. Redaction therefore happens where the
URL is built — inside the four named transports — rather than at each of the
places it might be printed, because the second kind of rule is one that is
eventually forgotten at one call site.

The rolling file promotes this from hygiene to a load-bearing property. A log
that only ever existed in a container's stdout leaked to whoever had the host. A
log the tool invites people to attach to a support message leaks to strangers,
and it is this decision that invites them.

## A lane never dies

The lane worker catches everything. An exception escaping a **run** is recorded
as that run's *failed* outcome, with its type and message, and the loop
continues — which is `prdb-ordeno`'s pattern, where the runner logs and swallows
so that one bad scan is one bad scan, and the worker catches only
`OperationCanceledException` to say it is shutting down.

The reason to prefer that here is not robustness in the abstract. A dead lane is
not one of the conditions ADR 0018 can draw: it has no row, raises no Gap, and
presents as a tool where nothing happens and nothing is wrong. Recording the
escape as a failed run puts it inside a mechanism that already exists — three
consecutive failures are ADR 0014's Gap — so the exception reaches the status
page without a new condition being invented for it.

`BackgroundServiceExceptionBehavior.StopHost` stays at its .NET default, which
takes the host down when an exception escapes `ExecuteAsync` itself. Under the
rule above that can only fire for a defect in the lane's own scaffolding rather
than in any routine, and a container that exits is visible where a lane that
quietly stopped is not.

## Considered options

**No logging library: the platform's console provider alone.** This was the
recommendation until the support case was weighed properly. Rejected: the
platform ships no file provider, and `docker logs` is a poor answer for the
audience this tool has. A NAS owner who mounted `/data` in a web UI has a file
manager and no shell, and the difference between attaching a file and
reproducing a problem over messages is most of what support costs.

**Serilog configured from a `Serilog` configuration section**, the usual
arrangement. Rejected above: it renames the one knob ADR 0034 published and
leaves the old name silently inert.

**Compact JSON to the file sink.** Rejected for the first release: ADR 0018 put
metrics, tracing and dashboards out of scope, so there is no machine reader, and
JSON is worse for the one reader there is. The formatter is already in the
referenced package, so this is a switch rather than a decision to revisit.

**`Serilog.Sinks.Seq`, or any network sink.** Rejected as the observability
stack ADR 0018 declined, and it would put an outbound HTTP client outside
ADR 0041's four named transports.

**A download-the-log endpoint in the UI.** Rejected as unnecessary rather than
wrong: `/data` is mounted by definition, so the file is already reachable. Worth
revisiting only if that stops being true.

**A `Result<T>` convention across the codebase**, with or without a library.
Rejected: it makes every signature carry a failure case, most of which have
none, and the one place the distinction is load-bearing is better served by
three named outcomes that say what they are. The no-null rule above catches what
the convention was for at a fraction of the noise.

**`Microsoft.Extensions.Logging.Abstractions` in `Core`.** Rejected under
ADR 0035, and rejected again on its own merit above: the constraint is what
forces reasons to be values.

## Consequences

- **The dependency criterion is now written down, and it is not
  dependency-aversion.** A library is taken when it does something the platform
  does not *and* its licence is one that cannot be withdrawn under this
  repository's MIT terms. Both halves are checked at the moment of taking, and
  the licence of every transitively referenced package is checked with it, not
  assumed from the top-level one. This criterion reaches back over ADR 0041 and
  ADR 0042, whose libraries were declined on the first half alone.
- **ADR 0034 gains a line in *What grows on the data volume*** — the log
  directory, bounded near a hundred megabytes — and its *Turning the log up*
  section is unchanged, which was the constraint this decision was written
  around. The log is not in the backup, for
  [ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md)'s
  standing reason: it is not something that cannot be fetched again, it is
  something that regenerates by running.
- **The data volume gains `logs/` beside the database.** It is created by the
  application after the entrypoint has dropped privileges, so it belongs to the
  run user and follows the umask like everything else ADR 0034 governs.
- **`Prdb.Fab.Core` keeps its empty project file**, and ADR 0042's architecture
  test that fails the build on a package reference there is the mechanism. No
  new test is needed for this decision.
- **Three named outcomes enter `Core`** for ADR 0016's poll, declared beside the
  interface `Infrastructure` implements. Whether the indexer and prdb reuse the
  shape or name their own is the skeleton's, but the no-null rule applies to
  all of them.
- **`CONTEXT.md` is unchanged.** Logging levels, sinks and retention are
  construction, and ADR 0034 already established that build and runtime
  artefacts are not concepts the language needs.
- **Left to [ticket 11](../../.scratch/build-foundation/issues/11-the-walking-skeleton.md):**
  the message template, how the pipeline floor is computed from the configured
  overrides, where the outcome types sit in the namespace, and how the lane's
  catch is written. All of them are cheaper to get right by writing them.
