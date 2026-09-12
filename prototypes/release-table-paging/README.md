# Prototype: what the Release table costs before it pages

Throwaway code, kept as the primary source behind the measurement that closed
the question "is the in-memory paging in `ReleaseBrowse.ReadAsync` a problem
yet". It is not part of the build and nothing references it.

```
dotnet run -c Release                 # database in the temporary directory
dotnet run -c Release -- /some/path   # database on real storage instead
```

## The question

`ReleaseBrowse.ReadAsync` cuts the page two different ways. In the Site and
Actor contexts it orders by `FirstSeenAt` and does `Skip`/`Take` in SQL, so one
page costs a page. In the Video context ADR 0008's ranking decides the order,
the ranking is computed in memory, and so the whole Release set for that Video
is materialised — with `Indexer`, `Video`, `Video.Site` and `Site` included —
before 50 rows are kept. Same screen, same pager, two cost profiles.

The measurement prices that difference, because nothing had: it drives the real
`ReleaseBrowse` over one Site holding one Video holding *N* matched Releases, so
`VideoAsync` and `SiteAsync` select exactly the same rows and only the paging
differs. `ReleaseRankings.ForVideoAsync` is timed on its own as well, since it
reads the same set a second time and is the floor under any change.

Every fiftieth Release confesses a password and every fourth is `Probable`, so
the ranked and the excluded group are both populated the way a real Video's
table is. Each read gets its own DI scope, because that is what a request is.

## The numbers

Measured on a 12-core laptop (Intel Core 5 210H), .NET 10.0.12, EF Core 10.0.10,
`Microsoft.Data.Sqlite` 10.0.10, SQLitePCLRaw 3.0.5. 21 reads per cell after two
untimed warm-up reads, page size 50. **What transfers is the shape, not the
absolute numbers.**

Database in `/tmp`, which is a tmpfs here — storage removed from the
measurement, leaving the in-memory ordering and the entity materialisation on
their own:

| Releases for one Video | Video p50 | Video p95 | Site p50 | Site p95 | ranking alone p50 |
| --- | --- | --- | --- | --- | --- |
| 5 | 7.0 ms | 16 ms | 8.3 ms | 15 ms | 2.0 ms |
| 50 | 18 ms | 29 ms | 9.6 ms | 11 ms | 3.7 ms |
| 200 | 22 ms | 25 ms | 5.9 ms | 7.3 ms | 5.4 ms |
| 500 | 23 ms | 26 ms | 4.5 ms | 6.2 ms | 8.1 ms |
| 1 000 | 32 ms | 40 ms | 3.5 ms | 4.7 ms | 11 ms |
| 2 000 | 62 ms | 70 ms | 4.6 ms | 6.4 ms | 26 ms |
| 5 000 | 205 ms | 227 ms | 8.8 ms | 13 ms | 80 ms |
| 10 000 | 488 ms | 559 ms | 21 ms | 27 ms | 172 ms |
| 20 000 | 824 ms | 1 053 ms | 22 ms | 26 ms | 276 ms |

The same run with the database on an NVMe SSD instead:

| Releases for one Video | Video p50 | Video p95 | Site p50 | Site p95 | ranking alone p50 |
| --- | --- | --- | --- | --- | --- |
| 5 | 9.0 ms | 17 ms | 8.6 ms | 17 ms | 1.7 ms |
| 50 | 13 ms | 19 ms | 13 ms | 15 ms | 3.8 ms |
| 200 | 24 ms | 28 ms | 8.5 ms | 10 ms | 8.7 ms |
| 500 | 35 ms | 47 ms | 5.3 ms | 6.6 ms | 11 ms |
| 1 000 | 56 ms | 63 ms | 4.4 ms | 5.0 ms | 16 ms |
| 2 000 | 83 ms | 108 ms | 7.6 ms | 10 ms | 28 ms |
| 5 000 | 213 ms | 251 ms | 8.7 ms | 11 ms | 60 ms |
| 10 000 | 336 ms | 345 ms | 13 ms | 14 ms | 148 ms |
| 20 000 | 973 ms | 1 138 ms | 29 ms | 31 ms | 298 ms |

Storage barely shows, and that is itself a finding: one Video's Releases are a
few MiB even at 20 000 rows, so after the first read they are in SQLite's page
cache and what is left is CPU — the ordering and the materialisation of the
entity graph. A slower disk moves the first read, not the curve, which is why
the two tables agree. Asking for the *last* page rather than the first changes
nothing on the Video side either — the program prints those two columns as
well, and they stay within the noise of the first page: the whole set is
already in memory by the time the skip happens.

## What it says

- **Up to about 500 Releases for one Video the difference is not visible.** The
  Video page stays in the tens of milliseconds; the gap to the Site branch is
  under 30 ms, which is less than the variance between two runs.
- **It becomes perceptible somewhere between 2 000 and 5 000.** At 2 000 the gap
  is 55-75 ms, at 5 000 ~200 ms, at 20 000 about a second. The growth is roughly
  linear in the number of Releases, as the shape predicts.
- **Between a third and a half of it is `ReleaseRankings.ForVideoAsync`**, which
  reads the same set independently. Any change that leaves that alone buys at
  most half of the difference.
- **Releases matched to a *wanted* Video are pinned against eviction**
  (`WantedIdentificationReleasePin`), so this is the one set that grows without
  a retention bound. The only ceiling above it is ADR 0015's 100 000 rows per
  Indexer, which is a bound on the whole cache rather than on one Video.

Against that stands what a Video's Release set is made of. A Release is
Indexer-specific (ADR 0002), and only Releases identified as *this* Video take
part, so the set grows by the number of times that one Video is posted again and
picked up — a handful per Indexer, not a page of them. Reaching the thousands
where this starts to show would take a Video re-posted in numbers nothing else
about the tool is built for.

## If it ever matters

Not attempted here, and written down so the next reading of this does not start
from nothing. The order ADR 0008 requires is not expressible in SQL, but it does
not have to be: the ranking hands back the ranked and the excluded set already
ordered, and everything else in the table — a candidate, a Site-Only Match,
anything not matched as this Video — is ordered by `FirstSeenAt` and pages in
SQL like the Site branch does. So the page could be cut over the ranking's own
id list first and only fall through to a SQL page for the remainder, fetching
the 50 rows it decided on by id.

That keeps ADR 0008's order and ADR 0028's surface intact and removes the
materialisation of the entity graph, but not the floor: the ranking itself needs
every matched Release to say which one automation would take and why each other
one was passed over. The floor is the `ranking alone` column above.
