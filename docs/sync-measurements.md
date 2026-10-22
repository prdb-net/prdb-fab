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
| Monthly total including ordinary feeds and competing client | 203,670 | 141,360 |

The old request counts are calculated from its 200-row Identify and 50-ID detail
batches; the new schedule actually sends 13,860 background batches and 11,060
feed requests over 35 simulated days. No feed waits for budget and no batch
remains at the end. Account peaks are 242/hour and 158,405/month, both within the
same configured limits. There are 580 background deferrals and 2,965 unmetered
quota readings. The synthetic responses total 581,515,942 bytes; 27,885 HTTP
samples each take the fixture's 100 ms (p50/p95 both 100 ms by construction).
Inline payload size is a tradeoff, not a production byte-saving claim.

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
