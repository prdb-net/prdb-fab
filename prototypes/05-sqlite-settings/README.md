# Prototype: how SQLite is opened and written

Throwaway code, kept as the primary source behind ticket 05 and the ADR it
produced. It is not part of the build and nothing references it.

```
dotnet run -c Release          # seeds once, then runs every section
dotnet run -c Release -- seed  # rebuild the reference database only
```

Sizes come from the decisions rather than from taste: 300 000 releases (three
indexers at ADR 0015's 100 000 ceiling), 500 needles (ADR 0025), six indexed
counts per tick (ADR 0032), a status page read every five seconds (ADR 0018).
The seeded database is 89 MiB.

Measured on a 12-core desktop with a local SSD, .NET 10.0.111, EF Core 10.0.0,
`Microsoft.Data.Sqlite` 10.0.0, SQLite 3.53.4. **A NAS is slower and the ratios
are what transfer, not the absolute numbers.**

## A. Idle cost of one tick — ADR 0032's six indexed counts

| | p50 | p95 | max |
|---|---|---|---|
| `journal=delete synchronous=full` | 0.36 ms | 0.38 ms | 0.45 ms |
| `journal=wal synchronous=normal` | 0.34 ms | 0.51 ms | 0.61 ms |

ADR 0032 priced the work-set family at "six indexed counts per tick". That is
a third of a millisecond, every ten seconds. It does not matter what else is
decided here.

## B. Status page while the sync lane writes and the bulk lane scans

Writer inserting walk pages of 100 rows, scanner running ADR 0025's pass in a
loop, reader doing a full status page. Twelve seconds each.

| | samples | p50 | p95 | max |
|---|---|---|---|---|
| `journal=delete synchronous=full` | **1** | **13 951 ms** | 13 951 ms | 13 951 ms |
| `journal=wal synchronous=full` | 455 | 0.88 ms | 1.56 ms | 2.10 ms |
| `journal=wal synchronous=normal` | 455 | 0.90 ms | 1.33 ms | 2.18 ms |

This is the whole decision. In SQLite's default journal mode the status page
completed **once in twelve seconds**. Under WAL it is under a millisecond and
never above 2.2 ms.

`synchronous=full` and `normal` are indistinguishable here — on an SSD. The
difference is fsyncs, which is what a NAS charges for.

## C. A write transaction held open for 3 s — what ADR 0004 forbids

| | p50 | p95 / max |
|---|---|---|
| `delete` reader | 2.35 ms | 3.14 ms |
| `delete` other writer | 10.95 ms | **2 746 ms** |
| `wal` reader | 2.31 ms | 3.53 ms |
| `wal` other writer | 1.68 ms | **2 735 ms** |

WAL does not rescue this. A transaction held across a call blocks the other
lane for as long as it is held, in both modes — readers are fine, the other
writer waits the full duration. This is ADR 0004's rule priced.

## D. Two lanes writing at once, by `busy_timeout`

Eight seconds, two threads inserting walk pages.

| `busy_timeout` | committed | `SQLITE_BUSY` | p50 | p95 | max |
|---|---|---|---|---|---|
| 0 ms | 12 175 | 0 | 0.38 ms | 0.95 ms | **1 052 ms** |
| 100 ms | 7 807 | 0 | 1.95 ms | 4.06 ms | 23.6 ms |
| 5000 ms | 8 017 | 0 | 1.90 ms | 3.92 ms | 37.0 ms |

No errors in any configuration — but with `busy_timeout=0` the worst case is a
second, because the waiting is then done by `Microsoft.Data.Sqlite` retrying
rather than by SQLite blocking. Setting it **lowers** the tail by a factor of
thirty.

## E. Which pragmas survive a connection

```
a connection opened with no pragmas at all sees:
  journal_mode  = delete   (stored in the file)
  synchronous   = 2        (FULL, per connection)
  busy_timeout  = 0        (per connection)
  foreign_keys  = 1        (per connection)

pooled: set busy_timeout=9999, closed, reopened -> 9999   (it SURVIVED)
unpooled, fresh connection: busy_timeout = 0
```

`journal_mode` is a property of the file and is set once. Everything else is a
property of the connection. A pooled connection is handed back **as it was
left**, so a pragma set on one is inherited by whoever gets it next — which
means the application cannot tell whether it is holding a configured connection
or a fresh one, and must therefore set them on every open.

## F. EF Core on top

```
EF Core's own connection, before anything configures it:
  journal_mode = delete
  synchronous  = 2
  busy_timeout = 0      <- 'Default Timeout' in the connection string is NOT this
```

| | |
|---|---|
| one count, warm | 0.220 ms (raw ADO.NET ≈ 0.05 ms) |
| 200 rows `AsNoTracking` | p50 0.51 ms |
| 200 rows tracked | p50 1.15 ms |

EF Core configures none of it. `Default Timeout` is the **command** timeout,
which is a different mechanism from `busy_timeout` — see H.

## G. ADR 0025's backwards pass, on its own

| | p50 |
|---|---|
| `journal=delete` | 6 803 ms |
| `journal=wal` | 6 837 ms |

**This does not match ADR 0025's 1.7 s and is not offered as a correction to
it.** Different schema (the full release row is 89 MiB here against that ADR's
40 MiB title-shaped table), different needles, different SQLite. What matters
for *this* ticket is the shape rather than the number: the bulk lane holds a
read open for seconds at a time, which is exactly what section B makes the
status page contend with.

Checked separately, since the number invited it: a covering index on
`normalised_title` takes the pass from 6 507 ms to 4 483 ms — 31 % faster for
30 MiB, +34 % on disk. That is a worse trade than the FTS5 one ADR 0025 already
rejected, so it **confirms** that decision rather than reopening it.

## H. Which knob actually serialises two writers

Two threads, each updating 20 000 rows in a transaction, six seconds.

| | committed | `SQLITE_BUSY` |
|---|---|---|
| `busy_timeout=0`, `Default Timeout=30` | 303 | 0 |
| `busy_timeout=0`, `Default Timeout=1` | 312 | **5** |
| `busy_timeout=5000`, `Default Timeout=1` | 322 | **1** |

There are two timeouts and they compose. `Default Timeout` — the command
timeout, 30 s by default — retries a busy database on its own, which is why
section D saw no errors with `busy_timeout=0`. Shrink it and errors appear.
That matters because ADR 0014's backoff would count such an error as the
routine's own failure and, three of them in, raise a Gap for a database that
was merely busy.

## I. WAL size under continuous writing with a reader always present

```
peak WAL during 15 s of writing with a reader present:  4 618 KiB
WAL after the writers stop:                             4 618 KiB
after an explicit TRUNCATE checkpoint:                      0 KiB
```

The WAL settles in the megabytes, not the gigabytes, and does not shrink on its
own — the file is reused rather than truncated. Nothing to manage, and nothing
that changes ADR 0034's data-volume arithmetic.

## Package finding, outside the measurements

`Microsoft.Data.Sqlite` 10.0.0 resolves `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11,
which carries a high-severity advisory (`NU1903`, GHSA-2m69-gcr7-jv3q) and
bundles SQLite 3.49.1. Pinning `SQLitePCLRaw.bundle_e_sqlite3` to 3.0.5
explicitly clears the warning and moves SQLite to 3.53.4. It belongs in
`Directory.Packages.props` as a deliberate entry rather than a transitive one.
