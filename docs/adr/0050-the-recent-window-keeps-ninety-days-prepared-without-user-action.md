# The Recent Window keeps ninety days prepared without user action

Sync continuously keeps a fixed rolling **Recent Window** of ninety days ready
in local storage. It covers prdb Catalogue videos, Releases from every enabled
Indexer, and prdb Identification of every Release in that interval. Opening a
page performs no remote work and is not a prerequisite for any of those rows to
exist.

This amends ADR 0013's page-count bootstrap and pinned-only repair, ADR 0015's
rule that an Indexer is never re-walked, ADR 0023's rejection of eager and
scheduled Identification, and ADR 0014's list of recurring routines. Their
bounded-cache, authority, governor, lane and local-page boundaries remain.

## Why the obligation is a time window

The earlier decisions were made before the installation workload and available
prdb allowance were measured. They optimized away requests so aggressively
that a newly opened Release page could truthfully show no result even when both
prdb and an Indexer already knew the Release. A page read cannot repair that
without becoming remote work, and a one-shot bootstrap cannot repair it after
an Indexer exposes an old Release late or was unavailable during the pass.

The product obligation is recent availability, not a permanently fresh copy of
the entire outside corpus. Ninety days is a bounded, explainable interval that
includes the ordinary discovery horizon while leaving older data under the
existing cache and pin rules. Its request volume varies with outside activity,
but every request still passes the prdb Governor or the configured Indexer's
Daily Query Budget. The Status surface exposes an incomplete or stale proof
instead of letting an empty local result imply that no Release exists.

## How the window stays complete

The fast head readers remain. Beside them, one recurring bulk-lane pass walks
prdb's videos newest-first and one targeted recurring bulk-lane pass walks each
enabled Indexer with `maxage=90`. Each pass carries a durable page, reaches the
inclusive time boundary or the end of the source, records when that complete
proof was obtained, and begins another complete pass within twenty-four hours.
Restarting resumes the page rather than requiring a user action or restarting
from the head.

A Release inside the Recent Window goes directly to `Awaiting`; local Screening
is unnecessary because inclusion in the bounded current interval is already a
reason to spend Identification capacity. Every recent Release is submitted to
prdb again when its last Identification is about twenty-four hours old. prdb
remains the only Identification authority, and the new answer replaces the old
answer and its derived automatic-decision state.

Catalogue details in the Recent Window are likewise re-read when about
twenty-four hours old, whether or not the row is pinned. Older Catalogue rows
are repaired only while pinned. Recent Catalogue rows and Releases are excluded
from count-based eviction, so a configured ceiling may be exceeded rather than
silently breaking the ninety-day promise.

## Consequences

- A complete Recent Window means prdb and every currently enabled Indexer have
  each completed a full pass recently. An Indexer added or reconfigured makes
  the proof incomplete until its pass finishes.
- Late Indexer visibility and temporary source outages are repaired by the next
  full pass. The head reader remains responsible for low latency between those
  proofs.
- Initial setup becomes useful incrementally, but Status and Release pages say
  that an empty local result is not authoritative until the proof is complete.
- Data older than ninety days is not continuously reconciled. Wanted Sweep,
  Manual Search, pins and the existing forward/backward discovery paths still
  bring in and preserve older rows where there is a concrete reason.
- The fixed interval is product policy, not an installation setting. Making it
  adjustable would make page semantics and resource guarantees differ between
  installations.
