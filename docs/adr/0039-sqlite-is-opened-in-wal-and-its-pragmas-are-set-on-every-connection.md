# SQLite is opened in WAL, and its pragmas are set on every connection

Measured rather than argued, in the shape
[ADR 0025](0025-the-cache-is-searched-with-like-over-a-normalised-column-in-one-pass-per-batch.md)
established for this project. The prototype is in
[`prototypes/05-sqlite-settings/`](../../prototypes/05-sqlite-settings/), with
every number and how it was taken; only the ones that decide something appear
here.

The database is opened with **`journal_mode=WAL`**, **`synchronous=NORMAL`**,
**`busy_timeout=5000`** and **`foreign_keys=ON`**. One writer at a time is left
to SQLite. Migrations run at startup, before the listener and before the lanes.

## WAL is not a tuning choice

[ADR 0004](0004-the-stack.md) chose SQLite for a single-user tool and stated one
writer at a time as the consequence. What it could not know is that
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)
would put a page in front of a person that polls every five seconds, and that
ADR 0025 would put a pass over the whole indexer cache in the bulk lane beside
it.

With a writer inserting walk pages, the bulk lane scanning, and a reader drawing
the full status page, over twelve seconds:

| journal mode | status pages drawn | p50 | max |
|---|---|---|---|
| `delete` (SQLite's default) | **1** | **13 951 ms** | 13 951 ms |
| `wal` | 455 | 0.90 ms | 2.18 ms |

In the default mode the page completed **once**. That is not a slow page, it is
a broken one, and ADR 0018's whole argument — that "is anything broken" is
computed at read time out of rows that already exist — rests on the read being
free. It is free only under WAL.

**`synchronous=NORMAL` is chosen without a measurement to show for it**, and
that is worth saying plainly: on the SSD this was measured on, `FULL` and
`NORMAL` are indistinguishable (0.88 ms against 0.90 ms). The difference is
fsyncs per commit, and `VISION.md`'s user runs this on a NAS, which is where
fsyncs are charged for. The durability given up is bounded and safe here: under
WAL, `NORMAL` can lose the last transactions to a power cut but cannot corrupt
the database, and
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md)'s recovery
already treats a lost final write as the ordinary case — a row left in `Filing`
is resolved by looking at the intended path, whichever way the crash left it.

## One writer at a time is SQLite's job, not the application's

Two lanes writing as fast as they can for eight seconds committed 8 017
transactions with **zero** `SQLITE_BUSY`. Nothing in the application had to
serialise them.

So there is no write lock, no semaphore and no single-writer service. What
enforces ADR 0004's rule is ADR 0004's rule: **no transaction spans an HTTP
call.** Priced, a transaction held open for three seconds blocks the other lane
for 2 735 ms — WAL does not help, because WAL separates readers from writers and
not writers from each other. The rule is the mechanism, and this is what it
costs when it is broken.

**`busy_timeout` is set because it lowers the tail, not because it prevents
errors.** With it at zero the worst case was 1 052 ms; at 100 ms or above, 24
ms. The waiting happens either way — the question is whether SQLite blocks or
`Microsoft.Data.Sqlite` retries, and blocking is thirty times cheaper. 5 000 ms
is chosen over 100 ms because it also covers the transaction nobody is supposed
to hold.

**There is a second timeout, and it matters more than it looks.**
`Default Timeout` in the connection string is the *command* timeout, defaults to
30 s, and retries a busy database on its own — which is why the paragraph above
saw no errors at `busy_timeout=0`. Cut it to one second and `SQLITE_BUSY`
appears. It stays at its default, and the reason is
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md): a lock error
surfaces to a routine as a failure, and three of those raise a **Gap** for a
database that was merely busy. Neither timeout is a performance setting; both
are there so that contention never reaches the status page as a fault.

## Every pragma but one is a property of the connection

This is the finding the prototype existed to produce, and it is easy to get
wrong in a way that works in testing.

`journal_mode` is stored in the database file and is set once, ever.
`synchronous`, `busy_timeout` and `foreign_keys` are properties of a
**connection**, and a connection opened with none of them reports `synchronous=FULL`,
`busy_timeout=0`, and nothing of what was set on the connection before it.

Worse for anybody reasoning about it: a **pooled** connection is handed back as
it was left. Set `busy_timeout=9999`, close it, open another, and it is still
9999. So the application can neither assume its pragmas are set nor assume they
are not — which leaves exactly one honest option: **set them on every open**, as
part of opening rather than as a step somebody remembers. They are idempotent
and cost nothing.

EF Core configures none of this. Its own connection reports the same bare
defaults, and `UseSqlite` has no opinion about any of them.

## Migrations run at startup, before the listener and before the lanes

ADR 0004 requires migrations at startup and a failing one to stop the tool
rather than run against a schema it does not understand. Two things now sit
either side of that moment.
[ADR 0038](0038-a-lane-is-one-worker-and-the-routine-row-is-the-only-truth.md)
starts the lanes with the application and reads routine rows on every pass, and
ADR 0018's page reads rows at request time. Both would run against a
half-migrated schema if the order were left to chance.

So migration is not a hosted service and not a lazy first-request check: it
completes, synchronously, before anything is registered to run and before the
listener accepts. `journal_mode=WAL` is set in the same place and for the same
reason — it is a property of the file, so it belongs where the file is prepared.

[ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md)
requires a derived column to be recomputed by the migration that changes its
derivation, which is a statement about what a migration contains and is
unaffected by where it runs.

## What EF Core is given

A **short-lived context**: one per request, one per lane run, from the scope
[ADR 0038](0038-a-lane-is-one-worker-and-the-routine-row-is-the-only-truth.md)
opens around a run. Nothing holds one across a lane's idle time, which is what
would turn a lane into a connection somebody has to think about.

**Read paths do not track.** 200 rows cost 0.51 ms untracked against 1.15 ms
tracked — small in itself, but tracking on a read path is a mutable row escaping
into code that only meant to look at it, and
[ADR 0035](0035-core-holds-the-rules-infrastructure-holds-the-rows-and-the-filesystem.md)
already spent an argument on entities being mutable. This is that argument's
cheap half.

The idle cost of ADR 0032's whole work-set family — six indexed counts — is
**0.34 ms per tick** at 300 000 releases. Through EF Core a count costs 0.22 ms
against raw ADO.NET's 0.05 ms. Both are noise against a ten-second tick, so
nothing here is written in SQL to save time.

## One package is pinned rather than inherited

`Microsoft.Data.Sqlite` 10.0.0 resolves `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11,
which carries a high-severity advisory (`NU1903`, GHSA-2m69-gcr7-jv3q) and
bundles SQLite 3.49.1. Pinning that package explicitly to 3.0.5 clears the
warning and moves SQLite to 3.53.4.

It goes in `Directory.Packages.props` as a deliberate entry, with a comment
saying why it is there, because a transitive dependency pinned without a reason
is one somebody removes while tidying.

## Considered options

**Leave the journal mode at SQLite's default.** Rejected under *WAL is not a
tuning choice*: the status page drew once in twelve seconds.

**`synchronous=FULL`.** Rejected on the NAS argument, not on the measurement,
which shows nothing either way on an SSD. Reopens if anybody sees a corrupt
database, since that is the only symptom that would make the trade wrong.

**A single-writer service or a write lock in the application.** Rejected: two
lanes writing flat out produced no errors at all, so it would be a mechanism
guarding against something that did not happen — and it would hide the case that
does matter, a transaction held across a call, behind a queue instead of
surfacing it.

**Set the pragmas once, at startup.** Rejected under *every pragma but one is a
property of the connection*: they are per-connection, and pooling makes their
state unknowable from the outside.

**Shrink `Default Timeout` so contention fails fast.** Rejected: a fast failure
here is a routine failure, and three of them are a Gap for a database that was
busy.

**A covering index on `normalised_title`** to speed up ADR 0025's pass.
Measured because the pass turned out to hold a read open for seconds: 31 %
faster for 34 % more disk on the largest table in the schema. That is a worse
trade than the FTS5 one ADR 0025 already rejected, so it confirms that decision
rather than reopening it.

## Consequences

- **`CONTEXT.md` is unchanged.** A journal mode is not a concept the language
  needed.
- **ADR 0004's single-writer consequence is confirmed and given its mechanism**:
  SQLite serialises, and the no-transaction-across-a-call rule is what keeps that
  cheap. It now has a number — 2 735 ms — for what breaking it costs.
- **ADR 0018's page is affordable**, which it was not under the default journal
  mode. This is the decision that ADR's argument depends on.
- **ADR 0032's six counts per tick are confirmed as free**: 0.34 ms.
- **`Directory.Packages.props` gains a deliberate entry** for
  `SQLitePCLRaw.bundle_e_sqlite3`, which ticket 01 left to the tickets that
  choose packages.
- **Ticket 08 inherits a smaller question than it had**: the prototype ran
  against real SQLite throughout, and nothing here is testable against anything
  else.
- **The numbers are from an SSD.** The ratios transfer to a NAS; the absolutes do
  not. The one decision that leans on the difference is `synchronous=NORMAL`, and
  it is flagged there rather than buried here.
