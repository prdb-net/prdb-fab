# Measuring Sync

Run the synthetic socket and scheduler fixtures without production credentials:

```
dotnet test --project tests/Prdb.Fab.Infrastructure.Tests -- --filter-class '*SyncCostTests'
```

A fake clock advances three seconds while the real SDK receives a synthetic
response. The routine log must show three seconds between start and finish;
transport diagnostics must show one request, 3,000 ms and the received byte
count. Deferrals remain separate from processing and do not create successful
runs. Diagnostics contain only closed endpoint categories, work classes, status
classes and counters. They never retain request/response bodies or URLs.

For before/after comparisons use the same generated Release names, match ratio,
Video detail sizes and quotas. Record transport requests, bytes, elapsed HTTP
time, identified distinct inputs, reused answers and avoided detail reads,
separately from Release rows updated. Compare useful coverage and elapsed time
at equal quota, not only the first burst. Request counters are process-local and
bounded by the finite endpoint/work/status categories; their `Since` timestamp
starts a new measurement interval on restart. Routine history is durable and
keeps the last fifty runs per routine. Scheduled wait seconds describe requested
deferrals, not measured productive time or a completion estimate.

Release Identification asks for inline Video documents. A 200-input batch with
all unique matches previously needed one Identify plus up to four 50-ID detail
requests; with complete inline documents it needs one. Ambiguous candidates still
need missing/stale detail reads; current local documents (including those covered
by a caught-up Video feed) avoid those reads. Compare actual response bytes as
well as calls: inline match documents enlarge the Identify response. The governed
transport buffers at most 32 MiB per response under its existing 30-second
timeout. Response bytes include the full body even if Content-Length was absent.
No percentage saving is promised for a particular match ratio or detail size.


## Reproducible rolling-window workload

```
FAB_SYNC_REPORT="$PWD/TestResults/sync-workload.json" \
  dotnet test --project tests/Prdb.Fab.Infrastructure.Tests -- \
  --filter-class '*SustainableSyncWorkloadTests'
```

Parameters are `FAB_SYNC_RELEASES` (98,872), `FAB_SYNC_DISTINCT` (79,150),
`FAB_SYNC_DAYS` (35), `FAB_SYNC_HOURLY` (1,000), `FAB_SYNC_MONTHLY` (200,000),
`FAB_SYNC_OTHER_DAILY` (4,000) and `FAB_SYNC_INDEXER_DAILY` (1,000). These are
synthetic fixture defaults, never product policy. No production names, IDs,
credentials or remote services participate.

The workload combines a request-demand model at that scale with the real
SDK, transport, Governor and fake rolling-window server over thirty-five days.
Existing account use is distributed over seven old cohorts; another client
keeps spending throughout. The recurring nine-feed profile runs at its actual
cadences. Representative 200-input inline response payloads have a deterministic
100 ms socket latency. SQLite projection, exact-input identity, cross-Indexer
fan-out, overlap drift, restart, expired evidence and interrupted detail batches
are tested separately through their real composition. This is not an end-to-end
production speed benchmark or a claim about real prdb network latency.

The committed [synthetic report](fixtures/sync-workload-report.json) records:

| Quantity | Previous algorithm's demand | Current fixture |
| --- | ---: | ---: |
| Identify requests per daily working set | 495 | 396 |
| Additional detail requests, all matches | 1,978 | 0 |
| Total metered Identification requests/day | 2,473 | 396 |
| Mandatory ten-second gaps per daily working set | 4,940 seconds | none; bounded turns yield to the lane's one-second tick and Governor |
| Thirty-day demand including ordinary feeds and competing client | 203,700 | 141,390 |

The old request counts are calculated from its 200-row Identify and 50-ID detail
batches. The adaptive schedule sends 13,860 background batches and 10,845 feed
requests over 35 simulated days. All background batches finish. There are 215
feed deferrals: the competing client can spend the protected 20% before old
cohorts expire, and FaB waits rather than invading that reserve. Account peaks
are 242/hour and 162,819/month (the latter includes the other client's use).
There are 1,127 background deferrals and 2,935 unmetered quota readings. The
synthetic responses total 581,505,663 bytes; 27,640 HTTP samples each take the
fixture's 100 ms (p50/p95 both 100 ms by construction). The server now models
the published 31-day window. Inline payload size is a tradeoff, not a production
byte-saving claim.

The Indexer demand model takes one page per simulated minute and uses 10-row
resume overlap. It needs 414 minimum queries and 652 queries through the initial
30-day target, reaching those at minutes 413 and 651 in that deliberately coarse
model. Head gaps stay at fifteen minutes while permanently oversized optional
history still advances 612 pages over four days. These times describe the model,
not FaB wall-clock latency. The real range/anchor writer is exercised by
`StagedCoverageTests`, including new data during bootstrap. No complete 90-day
source volume is inferred from the observed partial cache. A 400-query plan
reports a minimum shortfall and does not claim optional coverage.

The final migration is one additive upgrade from the released schema. The
intermediate development migrations were consolidated before publication;
existing cached Releases and completed legacy proofs survive. An unfinished
legacy offset lacks an identity anchor and restarts its coverage proof once.

## Published full-document baseline

```
dotnet test --project tests/Prdb.Fab.Infrastructure.Tests -- --filter-class '*VideoBaselineTests'
```

The generated SDK 0.22 request builders, real Governor/transport and SQLite
writers receive 1,000 complete synthetic documents in **one baseline request**,
then one empty change page establishes live coverage. Summary discovery at 100
rows/page plus hydration at 50 IDs/request needs **30 requests** for those same
1,000 new documents, before its equivalent change-feed proof. With a fixed
100 ms transport latency, those document-transfer round trips alone represent
100 ms versus 3,000 ms; this is a request-latency model, not a production or SQL
wall-clock speed claim. The document payload remains complete on both paths.

The fixture also checks lost responses and scope/cursor replay after restart,
transaction rollback after a partially written page, edits/inserts/deletions and
merge survivors during catch-up, typed 413 escapes across quota pauses and 404,
backwards server bounds, stale replay against a tombstone, and lost live cursors.
No immediate repair duplicates documents covered by the finished inventory.
The compatibility reader's existing tests still prove the ninety-day boundary
and bounded resumption when an older server lacks the operation.

## Identification freshness under ordinary churn

```
FAB_FRESHNESS_REPORT="$PWD/TestResults/identification-freshness.json" \
  dotnet test --project tests/Prdb.Fab.Infrastructure.Tests -- \
  --filter-class '*IdentificationFreshnessWorkloadTests'
```

The [committed report](fixtures/identification-freshness-report.json) runs actual
SDK/Governor/SQLite turns for 40 complete distinct tuples. Seven turns are
23 hours apart; one input's matching evidence changes each turn, including a
positive becoming a negative. Complete match documents contain a 1 KiB synthetic
description. Another client consumes 4,000 requests/day against the same reported
1,000/hour and 200,000/month windows. Both paths keep the existing bounded turn
and cadence; no production credentials or network latency are involved.

| Server proof lifetime | Ordinary Identify inputs | Inputs identified with proofs | Unchanged reused | Response bytes before / after | Metered batches before / after |
| --- | ---: | ---: | ---: | ---: | ---: |
| 6 hours (published default) | 280 | 280 | 0 | 367,868 / 367,868 | 7 / 7 |
| 24 hours (published configurable maximum) | 280 | 160 | 120 | 367,868 / 221,189 | 7 / 7 |

The default provides **no daily re-identification or quota saving**: all handles
have expired by that work's next turn, so FaB uses ordinary Identify directly.
With 24-hour server-issued handles, this changing fixture avoids 43% of ladder
inputs and 40% of response bytes. Validation spends ordinary quota, so request
counts remain equal. Neither setting is selected or changed by FaB, and these
figures do not predict production churn, payload size or wall-clock speed.
Earlier coalescing/inline-detail savings and the 30-to-1 Catalogue baseline are
independent of this result.

The real integration cases also cover all four answer classes, mixed statuses
and refs, a missing-proof input, bounded fallback, expired and unsupported
operations, quota deferral after a replacement checkpoint, restart, unknown
statuses, account changes during a response, stale acquisition gates and
preservation of automatic decisions and Candidate edges on an unchanged answer.

## Adaptive account budgets

`AdaptiveBudgetTests` exercises the real SDK and Governor with the locally
observed budget shape (1,000/hour, 200,000/31 days, 80,000 monthly remaining).
At least 100 background requests can begin in the initial bounded block, enough
for 18,700 distinct inputs at 200 per batch. It then verifies an actual pacing
wait and resumed admission. This is an admission test, not a wall-clock promise;
network responses, SQLite writes and other routines still take time.

`Published_plans_keep_polling_and_background_progress_across_31_day_expiry`
simulates 40 days for the repository defaults: Plus 100/1,000, Premium
1,000/20,000 and Ultra 10,000/100,000. All recurring feeds must run repeatedly,
background work must continue after rolling expiry, and observed account usage
must stay within 80% of both limits. The socket expires requests after 31 days.
These defaults are test inputs only: production always reads its actual quotas.

### Local validation, 2026-10-23

The running account reported Plus with overridden limits of 1,000/hour and
200,000/31 days. After the local update at 11:22:51 UTC, the already-populated
installation cleared its roughly 15,950 pending Release rows by 11:25:05 UTC.
The process counters recorded 13,589 identified inputs and 2,352 reused answers;
Status showed zero remaining rows and distinct inputs, zero quota deferrals,
and zero consecutive Identification failures at completion. One 30-second
Identify timeout was retried by the ordinary scheduler and recovered. The 79
HTTP attempts included one unmetered budget read. This is an observed warm-cache
backlog drain, not a controlled before/after benchmark or a promise about an
empty installation, another network, or another account's competing traffic.
