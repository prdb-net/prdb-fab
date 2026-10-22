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
