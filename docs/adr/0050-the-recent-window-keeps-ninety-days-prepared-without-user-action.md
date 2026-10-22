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

## Amendment: a caught-up Video feed also proves detail freshness

Catalogue membership is still proved by recurring Recent Window discovery. Its
completion alone does not claim current details. Detail freshness can now be
proved by a completed local baseline plus a Video change feed caught up within
the revalidation interval, even when unchanged rows have old LastReadAt values.
Each relevant row must be covered by that baseline or a later detail/feed write;
new summary placeholders remain due. Indexer and Identification proofs remain
independent. A missing/lost cursor, incomplete baseline or old caught-up proof
keeps bounded fallback repair due; it never declares the window complete merely
because the feed made a request.

## Amendment: Identify answers precede detail hydration

A validated Identify response is saved on each Release before any dependent
Video detail request. The Release remains Awaiting and cannot enter acquisition
until required match details exist. A new scope or restart resumes saved results;
successful detail batches are reused, so a deferred/lost later request cannot
force another metered Identify call. Omitted match documents retain their saved
answer and retry details hourly. Candidate identity stubs stay repairable and
pinned. The JSON checkpoint is disposable cache state and is never exported.

## Amendment: exact-input reuse

Filename-only Release Identification coalesces the complete exact input tuple,
including all absent hashes and size. Neither normalisation nor punctuation
removal establishes equality. A versioned SHA-256 key over that tuple and the
account scope indexes the authoritative answer retained on bounded cached
Release rows. Independent Release identities, candidate edges and acquisition
history remain independent. At most 200 distinct inputs and 2,000 row updates
are considered in a turn; excess duplicates reuse the saved answer next turn.
Reuse retains the original answer timestamp and expires at the existing
revalidation lifetime. It never extends authority by copying an old answer.
Changed titles clear the checkpoint; changed account scope cannot reuse it.
Inline documents are projected once and removed from completed checkpoints.

## Amendment: staged Indexer coverage replaces the fixed daily 90-day obligation

The Catalogue remains ninety days. Indexers now prove the newest **14 days**
first, extend that pass to the initial **30-day** target, and extend up to
**90 days** only from protected budget surplus and available downstream
Identification capacity. An incomplete optional extension is healthy. The
minimum takes precedence whenever its proof ages, including during expansion.
Each range has its own durable position, start/completion timestamps and query
cost. Extending reuses the completed shorter range's anchor rather than starting
its minimum scan again. A pass that took longer than a day cannot claim a fresh
proof merely because its final page just completed.

Newznab offsets are not snapshots. Every resumed request overlaps ten rows and
must rediscover the saved identity anchor before advancing. A missing anchor
restarts that proof at the head, preserving all cached rows. Head polling stays
at fifteen minutes beside these passes. Concurrent late visibility can still be
repaired only by a later pass: the external interface supplies no snapshot or
change cursor. Legacy completed 90-day proofs initialize all three ranges;
legacy unfinished numeric offsets lack an identity anchor, so the first anchored
proof restarts once. The previous cache and legacy progress record are retained.

RecentWindow.Days is unchanged: Catalogue scope and protected cache retention
still use ninety days. Existing older cached data is not destructively trimmed.
Daily Identification revalidation covers the thirty-day working set and wanted
older matches. Other retained older answers are best effort; newly discovered
older Releases and explicit Screening/search reasons still enter Awaiting once.
Optional history pauses when existing Identification demand exceeds two distinct
batches or the local dual-window Governor cannot currently admit background work.
No additional Automation permission follows from coverage.


A cached answer is also invalidated by explicit backwards Screening after a new
Catalogue needle, and an unfinished checkpoint cannot survive an input/account
scope change or its original revalidation expiry as fresh evidence. Release
titles are optimistic concurrency tokens: a response cannot be attached after
the submitted title changed in another lane. A changed title explicitly clears
all derived evidence and acquisition state, including fields a concurrent
Identify response may just have written.

## Amendment: full-document Catalogue baseline and lossless change handoff

SDK 0.22's bounded full-document baseline replaces summary paging plus detail
hydration for the Catalogue's initial Recent Window. The creation range is
fixed, half-open and ninety days wide. Each page and its opaque cursor, original
server `changesSince` watermark and explicit scan/catch-up/live phase commit in
one SQLite transaction. A lost response replays only the uncommitted page.
Typed oversized-document escapes keep their ID and continuation durably until
the ordinary governed detail route succeeds or reports 404; the subsequent
change feed resolves disappearance and merges.

The Video feed pauses during the scan, then starts inclusively at that server
watermark with no tie ID. Each change page and its cursor commit together;
older revisions cannot resurrect known tombstones. Known merge survivors are
read through governed detail batches. Only after a complete inventory may
missing in-range rows be marked unavailable; metadata, Library identities and
older cached rows remain. The baseline and feed serialize their handoff across
lanes without holding a database transaction across HTTP.

A completed inventory plus a current Video feed proves rolling Catalogue
membership and details; it replaces unconditional daily rediscovery. Loss of
that feed cursor, or resumption beyond tombstone retention, invalidates coverage
and requests a new inventory. A current baseline does not claim coverage for an
unread pinned older row. Servers without the baseline route retain bounded,
governed summary/detail discovery and repair, retrying capability on the next
daily pass. Status names the phase and counts actual full documents and requests.
Indexer 14/30/90 coverage and all acquisition permissions remain independent.

## Amendment: validate retained Identification authority before re-identifying

The fixed 23-hour interval now makes an answer **due for validation**, rather
than unconditionally requiring the Identification ladder to run again. SDK
0.22 opts into account/full-tuple/algorithm/evidence-bound freshness proofs.
Each answer, its original Identify timestamp, optional proof and server expiry
are saved atomically. A separate validation timestamp records a server-confirmed
`unchanged` answer. Validation never renews the token expiry or strengthens its
Confidence, never refreshes Video details, and never resets a settled Release's
Candidate edges or automatic decisions.

At most 200 distinct complete tuples and 2,000 Release rows enter a turn. If at
least one retained proof could still be usable, the generated validation route
handles that bounded mixed batch with `reidentify=true`; only `identified`
results replace authority. A server's explicit `reidentify` result takes an
ordinary bounded Identify fallback. If no proof can still be usable, ordinary
Identify avoids a larger validation envelope. Missing/expired proofs, changed
evidence or algorithm, cache loss and older servers therefore retain conservative
fallback. A new Catalogue needle requests server validation of any retained
proof instead of accepting the old negative from the local cache. Title changes
discard the tuple and proof; account changes invalidate retained authority in
the same transaction as the new key. In-flight responses cannot commit under a
changed account. Rotating a key within the same verified account preserves the
account-bound proof.

Local exact-input reuse retains the source's original Identify and validation
timestamps and its original expiry. A stale answer, pending detail hydration or
unknown authority cannot enter Download Ranking or reserve a Download. Automatic
decisions keep their pending work until validation; resumed manual reservations
also wait and re-check immediately before submission. This is a wait for the
ordinary governed routine, not a remote request from a page read. The original
23-hour client authority interval remains the conservative fallback policy;
a token's handle expiry says it can no longer be validated, not that a previously
received answer was independently revoked. The expiry itself is never extended.

Every validation batch costs the same one hourly/monthly quota unit as Identify.
The published server defaults to **six-hour** proofs and permits up to 24 hours.
At a 23-hour validation interval, six-hour proofs cannot avoid the ordinary daily
ladder work; FaB does not poll more often just to keep those handles alive.
Longer server-issued proofs can avoid retransmitting unchanged answers while
usable. Measured savings and limits are documented separately; no quota reduction
or increase in subscription allowance follows from a freshness hit.
