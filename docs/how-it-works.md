# How prdb-fab works

The detailed behaviour behind catalogue browsing, downloads and library filing.
For installation, start with [Running in Docker](running-in-docker.md); for a
quick overview, see the [README](../README.md).

## Catalogue and browsing

It reads prdb, on its own, and keeps what it reads: the videos prdb publishes,
the sites and actors behind them, your wanted list and your favourites, and one
picture per video. **What's new**, **Search**, **Sites**, **Actors** and
**Wanted** show that catalogue. Wanted and Favourite actions are written to
prdb through the same governed connection used by the background sync.

## Indexer search and identification

Each enabled indexer is also walked continuously. Releases enter a local,
disposable cache, are screened against the catalogue, and prdb is asked to
identify their names. The Catalogue keeps ninety days prepared. Indexers prove
14 days first, target 30 days, and extend optionally up to 90 only when budgets
allow it; Identification follows that working set. All of this runs without a
page first being opened. A shared **Releases** table opens from
all four browse surfaces and shows the Indexer, size, first seen time,
Identification State, Confidence and `matchedBy`. Ambiguous answers remain
Candidates and a Site-Only Match remains a Site — neither is presented as a
Video.

The browser never turns a page load into an indexer query. **Search Indexers**
is an explicit Video action: it records durable work, and the ordinary
scheduler queries all enabled Indexers or one selected Indexer while the page
shows progress. New results pass through prdb Identification before becoming
downloadable. Every other page search and refresh reads only local state. Each Indexer Cache is
bounded at **100,000 Releases**; the oldest examined Releases nothing points at
are evicted first, while Recent Window, unseen or still-wanted rows are never
discarded to hold the ceiling. The cache may exceed its ceiling when protecting
those rows requires it.

## Background sync and request budgets

Each newly added Indexer starts with a **1,000-request Daily Query Budget**.
Head polling every fifteen minutes and the measured cost of the newest **14
days** have protected capacity. A fresh installation proves those 14 days first,
then extends to its initial **30-day target**. Optional history up to 90 days
runs only from surplus Indexer budget and downstream prdb capacity. Wanted Sweep
and Manual Search take precedence over optional history, with reservations based
on actual bounded search demand. Existing older cache rows are retained.
Resumed pages overlap and verify a Release identity anchor because Newznab
offsets move as its results change. An interrupted pass resumes; an absent
anchor restarts its proof without dropping cached data.

The prdb Governor reads both **hourly and rolling monthly account-wide budgets**,
including use by other clients. Background Identification and repair are paced
below essential account/file work; an oldest-slot expiry never resets a full
allowance. Current inline Video details and exact-input answer reuse reduce
metered requests. Identify answers survive interrupted detail reads. Catalogue
scope remains ninety days: a resumable full-document baseline hands off to
Video changes, avoiding repeated daily discovery while coverage is current.
Older servers keep governed summary/detail repair. Retained Identification
answers use optional server freshness proofs, with conservative fallback on
expiry or missing support. Validation still costs ordinary quota; six-hour
default proofs cannot avoid a 23-hour daily re-identification. Routine
revalidation covers the 30-day Indexer
working set and wanted older matches. Status shows actual 14/30/90 proofs,
remaining distinct inputs, request/byte costs and the reason work waits. It
reports an unsatisfied minimum or a measured capacity shortfall; incomplete
optional history alone is healthy. See [the measurement recipe](sync-measurements.md).

## Status and scheduled work

**Status** lays the whole unattended loop out as Sync (prdb), Sync (Indexers),
Match, Decide, Download and File. Its headline counts only **Gaps** that need a
repair. A **Brake** explains work that is deliberately held by a configured
gate or budget and links to that choice without calling it broken. The liveness
line shows the last file filed, Download started or Release cached. The page
polls local state every five seconds; opening it never contacts prdb, an Indexer
or SABnzbd.

Every scheduled routine can be made due with **Run now** from Status. This is a
request to the normal scheduler, not a second execution path: the Governor,
daily Indexer query budget and empty work set still win, and the accepted,
deferred or refused answer remains visible beside the routine.

## Downloads and retries

On a Catalogue card, **Download** immediately chooses the preferred highest
Quality available and submits it to SABnzbd; its overflow menu still opens the
Video's Releases table for an exact choice. **Settings → Downloads** sets the
highest Quality and lower-Quality fallback ladder used by that direct action.
Because Newznab has no dependable Quality field, common Release-name tags are
used as hints and an unlabelled Release is the final fallback. From the Releases
table, **Download** fetches that exact NZB and submits it to the configured
SABnzbd category. The choice is recorded before the remote write. prdb-fab then
polls SABnzbd, shows its own Outstanding, Completed and Failed state under
**Downloads**, and automatically tries the next ranked,
unconsumed Release after a release failure. Each Video has a budget of three
Download attempts; its Release view shows the spent attempts, the next choice,
and a confirmed reset of that Video's local history.

## Wanted automation

Fresh setup offers **Recent Wanted autopilot** after inventory and a previewed
import of the existing collection. It freezes the newest 100 eligible missing
Wanted Videos released within 90 UTC calendar dates, then admits new recent
Wanted intent. The initial selection is never refilled, and unknown/future dates
are excluded. All enabled Indexers participate. Existing installations adopt
this workflow explicitly in **Settings → Automation**.

Custom rules remain independent permissions for broader Wanted acquisition.
Turning off the supplied policy leaves custom rules unchanged. Every automatic
submission rechecks collection readiness, Wanted, held files, candidate holds,
Confidence, retry budget and the unfinished-Download cap (20 by default).
See [Automation safety](automation.md) for the collection decisions, limits and
Wanted-removal behavior.

## Filing, review and cleanup

When SABnzbd reports Completed, prdb-fab collects supported Video Files,
identifies each one, and files allowed matches into
`<Site>/<Site> - <date> - <Title>/`. Moves within one filesystem are renames;
cross-filesystem moves copy to a hidden temporary file, compare every byte,
rename it into place, and only then delete the source. The entry receives
`movie.nfo` and cached `fanart.jpg`; catalogue repair keeps those two files up
to date without renaming a recorded Video File.

Anything that cannot safely proceed waits in **Review queue** with its evidence
and one reason. Every row can be dismissed or its exact Video File deleted;
Unidentified, Duplicate and Entry Missing add only their reason-specific File
as, Replace or File as only copy action. **Library** shows only held entries and
their qualities, and **Operation log** exposes the immutable Filed, Replaced,
Deleted and Tidied acts.

After all Video Files in a directory-shaped SABnzbd storage have reached a
decision, tidy-up removes only `.nfo`, `.par2`, `.sfv`, `.srr`, `.url`, `.txt`,
`.jpg` and `.png`, then empty directories. The setting is enabled by default.
Unknown files remain, and the parent directory of single-file storage is never
tidied.

## Reporting and published previews

Reporting under **Settings → Reporting** is on by default and has three
independent switches. Fulfilment reporting sends a wanted Video id, whether it
is held, when it was filed and the highest truthfully expressible quality.
Confirmed-assignment reporting sends the exact file metadata a person approved
in the Review Queue: Video id, osHash, size, filename, Release name and available
runtime, dimensions and codec. Turning either switch off makes no outbound
report for that channel; pending differences remain local, and reports prdb
already accepted are not retracted. See
[Privacy and outbound data](privacy.md).

The third switch is the only one whose payload strangers see: **published
previews** submits a scrubbing sheet generated from a filed Video File, which
prdb shows under that Video after moderation. Nothing is generated or sent until
the form saying what it publishes has been saved once, and prdb offers no
retraction for a submission it has accepted. Only files filed from then on are
published automatically; the Library you already hold is published only by
asking, from **Settings → Reporting → Published previews**, which is also where
an upload whose outcome could not be established is decided. See
[Publishing previews](publishing-previews.md).

## Control over SABnzbd

The SABnzbd boundary remains exact: **prdb-fab never calls SABnzbd retry or
delete**, and *Stop following* changes only the local record. Removing a Video
from Wanted abandons its unfinished automatic Download locally, does not retry
it, and leaves the SABnzbd job and anything already filed untouched.

## Further reading

- [Backup and restore](running-in-docker.md#backup-and-starting-a-container-from-one).
- [Sync measurements](sync-measurements.md).
- [Domain glossary](../CONTEXT.md) and [architecture decisions](adr/).
- [Contributing and local development](../CONTRIBUTING.md).
