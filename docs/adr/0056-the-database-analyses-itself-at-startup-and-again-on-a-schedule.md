# The database analyses itself at startup, and again on a schedule

`PRAGMA optimize` runs once per start, in `DatabaseMigrator.PrepareAsync` after
the migrations and before the listener. A full `ANALYZE` runs from a maintenance
routine on the schedule, monthly. `PRAGMA analysis_limit` is never set.

This continues [ADR 0039](0039-sqlite-is-opened-in-wal-and-its-pragmas-are-set-on-every-connection.md),
which decided how the database is opened and said nothing about what the query
planner knows once it is.

## What was found

Nothing in this project has ever run `ANALYZE` or `PRAGMA optimize`. The
database therefore ships and runs with an empty `sqlite_stat1`, and every join
order SQLite picks is picked from its built-in guesses rather than from the
data.

That is mostly invisible, and it was found only because
[ADR 0055](0055-the-library-is-ordered-by-what-arrived-and-the-order-is-the-persons-to-change.md)
measured a grid query that a missing statistic makes five times slower. At
1,000,000 Catalogue Videos over 20,000 Library Entries:

| title-ordered grid | no statistics | analysed |
|---|---|---|
| first page | 199.3 ms | **40.9 ms** |
| last page (417) | 233.1 ms | **73.9 ms** |

With statistics the planner lets the small table drive the join. Without them it
scans the Catalogue and sorts. The fix costs no disk and no write time, which
makes it the cheapest measured improvement in the schema — and it had never been
applied because nothing had ever asked the question.

## Why statistics at startup, and why `PRAGMA optimize`

| | never analysed | analysed already |
|---|---|---|
| `PRAGMA optimize`, 200k rows | 5.6 – 10.9 ms | **0.0 ms** |
| `PRAGMA optimize`, 1M rows | 23.7 – 26.6 ms | **0.0 ms** |
| full `ANALYZE`, 200k rows | 37 ms | 31 ms |
| full `ANALYZE`, 1M rows | 166 ms | 157 ms |

`PRAGMA optimize` costs nothing on every start after the first, produces plans
and times indistinguishable from a full `ANALYZE`, and does not need a
connection that has run queries first — a fresh connection in `PrepareAsync`
analyses everything when `sqlite_stat1` is empty.

It also heals the first-run problem by itself. A new installation has empty
tables, `ANALYZE` writes nothing for them, and the next start with real data in
place fills them in. Nothing has to detect that moment.

A full `ANALYZE` at every start would work too — 166 ms is not noticeable beside
the ~1.3 s `MigrateAsync` already costs on an up-to-date database — but it pays
that cost forever in exchange for nothing, since repeating it is not cheaper
than the first time.

## Why a routine as well

`PRAGMA optimize` does not refresh statistics as data grows. Measured at both
scales, after inserting 30 % more Catalogue Videos, it proposes analysing
nothing: SQLite's internal threshold sits far above that.

For a while this costs nothing, because a 30 % drift costs nothing:

| 1M → 1.3M Videos | analysed | +30 %, not re-analysed |
|---|---|---|
| title first page | 39.3 ms | 40.5 ms |
| title last page | 70.7 ms | 72.9 ms |

The join-order decision is not marginal — it only has to know that
`library_entry` is very much smaller than `catalogue_video`, and that ratio
holds however both grow.

Statistics wrong by *orders of magnitude* are a different matter, and that is
the state an installation reaches on its own: it starts nearly empty, records
that, and grows into hundreds of thousands of rows while `sqlite_stat1` still
says two hundred. Measured by rewriting `sqlite_stat1` to what a young install
would have recorded, against 1.3M real rows:

| 1M rows | current statistics | `sqlite_stat1` says 200 |
|---|---|---|
| title-ordered grid | 39.3 ms | 40.6 ms |
| **Site-filtered grid** | **5.4 ms** | **39.8 ms** |

So the surface that grossly stale statistics damage is not the one this started
from. The title order is immune; the *filtered* grid pays 6-7×. That is an
argument for renewing statistics occasionally, and against renewing them often.

Monthly, in a lane where latency is nobody's concern, at 166 ms per run.

## Why `analysis_limit` is not set

`PRAGMA analysis_limit=400` is roughly ten times cheaper than a full `ANALYZE`
— 18 ms at a million rows — and gives identical grid plans. It was still
rejected: it records wrong selectivity for low-cardinality columns. At 1M rows
it wrote `TitleSearchedBackwards = 401` where the truth is 500,000.

That is the column [ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)'s
backwards-search work set filters on. Cheap statistics would buy 150 ms on a
monthly routine by misinforming the planner about a query that runs constantly,
which is the wrong trade in the wrong direction.

One trap to leave written down, because it was hit while measuring this:
`analysis_limit` is a per-connection pragma and the pool hands connections back
with it still set. A "full" `ANALYZE` measured 12 ms once, because a pooled
connection was still carrying `analysis_limit=400` from an earlier caller. This
is the same class of mistake ADR 0039 documents for the other pragmas, and the
same answer applies: it belongs in `SqlitePragmas`, applied to the connection in
hand, or nowhere.

## Considered options

**Leaving it alone.** Defensible right up until it was measured — nothing was
visibly broken. Rejected because 199 ms against 41 ms on a page a person waits
for is not a tuning detail, and the correction is free.

**A full `ANALYZE` at every startup.** Rejected for paying 166 ms forever to buy
what `PRAGMA optimize` gives for 0.0 ms after the first start.

**`PRAGMA optimize` alone.** Rejected because it ignores exactly the growth an
installation actually undergoes, which is the case that damages the filtered
grid.

**A routine alone, with nothing at startup.** Rejected because a fresh
installation would then run with no statistics until the routine first came due,
and because the startup call is free.

**`PRAGMA optimize` after every batch of Catalogue writes.** Rejected as the
same call at a worse moment: it measures as a no-op after growth, so it would
run constantly and change nothing.

## Consequences

- `PrepareAsync` gains one call after `MigrateAsync`, on its own connection,
  and a failure there is logged and swallowed. Statistics are an optimisation,
  and a database that cannot be analysed can still be served.
- The maintenance routine is an ordinary routine under
  [ADR 0038](0038-a-lane-is-one-worker-and-the-routine-row-is-the-only-truth.md)
  — one row, one worker, its own due time — and belongs in the bulk lane, where
  ADR 0025's cache pass already lives.
- `ANALYZE` holds a write lock for its duration. Under ADR 0039's WAL that does
  not block readers, which is why 166 ms in a background lane is uninteresting.
- The measurements above leave several tables empty and `ANALYZE` walks every
  index in the file, so a busy installation pays in proportion to its total
  indexed rows. Extrapolating the linear part gives ~0.8 s at 5M Catalogue rows.
  Nothing in the plausible range makes the startup call noticeable; the routine
  is where any growth in the full run lands, and that is the point of it being a
  routine.
