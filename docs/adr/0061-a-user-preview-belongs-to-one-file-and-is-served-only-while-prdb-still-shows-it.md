# A user preview belongs to one file, and is served only while prdb still shows it

prdb publishes a second population of pictures beside the `images[]` that
[ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md)
caches: images its users submitted, each one bound to the **osHash of the exact
file it was made from**, and each one either a single still or a **Sprite Sheet**
with a paired WebVTT saying which tile is which second. They are moderated, they
can be withdrawn and restored, and they are the only thing in this tool's reach
that says *this picture is of that file* rather than *of that Video*.

This decision settles what may be asked of prdb about them, what is kept, how
long, and what happens when prdb stops showing one. It settles nothing about
what they are shown *in* — the Preview gallery, the Review Queue and the Library
each answer for themselves — and it deliberately leaves the automatic
Identification evidence policy to
[ADR 0062](0062-a-preview-hash-binding-is-evidence-and-prdbs-answer-is-still-the-authority.md).

## What the API actually offers, verified

Against `Prdb.Sdk` **0.13.0**, the version this repository references, and the
public document it is generated from. No upgrade is needed for any of it: every
operation and every field below is already generated.

| Operation | What it answers |
| --- | --- |
| `GET /videos/{videoId}/user-images` | Every **publicly visible** user image **of that Video**, `displayOrder`, `createdAtUtc`, `id`. Hidden, denied and soft-deleted rows are excluded. |
| `GET /video-user-images/by-os-hash/{hash}` | Every publicly visible **unlinked** user image for that osHash. **Linked rows are excluded.** |
| `GET /video-user-images/{id}` | One publicly visible row. Hidden, denied and deleted rows are *not returned* — a `404` here is a withdrawal, not an absence. |
| `GET /video-user-images/changes` | A seek-paged current-state delta feed over `updatedAtUtc`, then `id`. Carries created, updated, soft-deleted **and moderation-visibility** rows as the current payload, plus `serverTimeUtc` and a `nextCursor`. |

`VideoUserImageDto` carries `id`, `userId`, a **nullable** `videoId`,
`basedOnFileWithOsHash`, `previewImageType`, `filesize`, `width`, `height`,
`displayOrder`, `url`, `hasVtt`, `vttUrl`, the five sprite geometry fields,
`moderationStatus`, `moderationVisibility`, `isDeleted`, `deletedAtUtc`,
`createdAtUtc` and `updatedAtUtc`. `moderationStatus` and `moderationVisibility`
are **open strings** in the document rather than enumerations, which is the
single most important fact about them here.

### What hash validation does and does not prove

The document calls `basedOnFileWithOsHash` a *server-validated 16-character OS
hash*. Three distinct claims are easy to read into that sentence, and only the
first is in it:

1. **The syntax was checked.** It is sixteen hexadecimal characters. This is
   what "server-validated" says.
2. **The submitter said this image came from that file.** The value is attached
   to an upload by whoever made it, and prdb has no copy of the file to compute
   the hash from.
3. **prdb asserts that the file with that hash is that Video.** Nothing in the
   document says this, and for a row whose `videoId` is null it is not even
   claimed.

So a hash on a user preview is a **binding made by a person and reviewed by
moderation**, and a *linked* row — one with a `videoId` — is the strongest form
of it. It is evidence. ADR 0062 says what may be done with it; nothing here
treats it as an assignment.

### The by-hash endpoint cannot identify a Video, and that is upstream's to fix

`GET /video-user-images/by-os-hash/{hash}` is the obvious way to ask *what is
this file*, and it is the one shape that cannot answer: it excludes exactly the
linked rows that would carry a `videoId`. An identified file is asked about the
other way round — `GET /videos/{id}/user-images`, filtered locally by hash —
which is only available once something already knows the Video.

`AGENTS.md` says a limitation nobody reports is a limitation nobody fixes, so it
is written down as a proposal in
[`docs/prdb-api-proposals.md`](../prdb-api-proposals.md) rather than only worked
around. Sending it is a separate act and not part of this decision.

## Interest, and why an ordinary GET never asks prdb

Two acts create **Interest** in a Video's user previews, and nothing else does:

- **A person opens a Preview.** The sheet is the surface that shows them.
- **A Video File is filed.** The Library wants the preview made from *that*
  file, and Identification wants the hash bindings.

Both go through the same door as
[ADR 0060](0060-a-preview-fetches-the-rest-of-a-videos-pictures-on-sight-and-they-are-evicted-first.md)'s
one request: an explicit `POST`, a durable routine row, and a sheet that renders
what it has. A `GET` reads local rows and makes no prdb request, ever — that is
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)'s
rule, and this is the second surface where it would be easiest to break.

**Deduplication is the routine row**, exactly as in ADR 0060. One targeted row
per Video means pressing a sheet open a hundred times schedules once, and a
restart finds the row still there. The row retires when the read lands.

**Negative results are remembered.** A Video whose list came back empty is the
ordinary case — most Videos have no user previews at all — and must not cost a
request every time somebody glances at it. Unlike ADR 0060 there is no existing
column that says when this population was last asked about, so this one is
recorded: `UserPreviewInterest.LastReadAt`, one row per Video, written whatever
the answer was. **A week**, the same figure and the same argument as ADR 0060's
`Recently`.

That row is also the interest register: it is what makes the change feed due
(below), what a cleanup pass expires, and what a restart resumes from.

## The snapshot and the feed, and the order they must happen in

Two things keep the local rows current, and the handoff between them is the one
place a race would lose a change silently.

- The **snapshot** is `GET /videos/{id}/user-images`, once per interested Video,
  refreshed no more often than the freshness window.
- The **feed** is `GET /video-user-images/changes`, global, following every
  moderation change prdb makes to any row.

The feed is a global feed over a population this installation holds a fraction
of, so it starts at prdb's own clock rather than at the beginning of history —
the argument
[ADR 0013](0013-the-prdb-catalogue-is-a-cache-with-pinned-rows-repaired-by-re-reading.md)
made for the images feed, unchanged. `serverTimeUtc` is what that clock is read
from; this tool's own clock is expressly not a substitute, and the API document
says so.

**The clock is taken before the first snapshot, never after.** If a snapshot ran
first and the clock were read afterwards, every change between the two would
fall in a gap neither covers, and a withdrawal landing in it would be served
forever. So the first read for an interested Video establishes the feed position
first — a one-row `changes` request whose rows are thrown away, which is the
existing `JustTheClock` shape — and only then asks for the list. It costs one
extra request once per installation.

The other direction is safe and is what the design leans on: a change *replayed*
by the feed over a snapshot that already has it is an upsert of the same values.
Everything below follows from that.

- **Equal timestamps** are handled by ADR 0013's overlap and its tie-breaker,
  which `FeedPosition` already implements: a settled position is set back a
  minute before it is used, a mid-walk one resumes at the exact row. Nothing new
  is needed here.
- **Out-of-order responses** — a snapshot answering after a feed page that
  already withdrew one of its rows — are decided by `updatedAtUtc` and not by
  arrival: a write whose payload is **older than the stored `UpdatedAtUtc`** is
  discarded. That single rule makes the two writers commutative.
- **A global feed is not permission to mirror it.** A page names rows for Videos
  this installation has no interest in; those are dropped without a row and
  without a byte, the same way `VideoImageFeed` drops images it cannot place.

## The moderation lifecycle, which is not `FoundDead`

`catalogue_image.FoundDead` means *prdb hard-deleted this image and the URL will
never work again*: marked once, never retried, permanent. Every clause of that
is wrong for this population, and reusing the column would be the worst kind of
bug — a picture that is invisible for a week and then comes back would be gone
for good.

A user preview instead carries what prdb reports: `ModerationStatus`,
`ModerationVisibility`, `IsDeleted`, and the `UpdatedAtUtc` they were true at.
From those, one derived question — **is this row servable?** — and it is derived
on read rather than stored, because a stored copy would be a second writer of a
fact the row already holds (ADR 0033).

- **Withdrawn, denied or deleted.** The row stops being served immediately: no
  bytes leave this tool for it, the gallery drops it, and any managed Library
  asset written from it is removed once the change is observed. The cached bytes
  are dropped too — they are a copy of something prdb has stopped publishing.
- **Restored.** The same id becoming visible again is an ordinary feed page. The
  row becomes servable, and the bytes are fetched again like any other cache
  miss. Nothing about the id is poisoned.
- **An unknown moderation value is not visible, and every value is unknown.**
  `moderationStatus` and `moderationVisibility` are open strings with no
  enumeration and no listed values anywhere in the document, so an allow-list of
  the words that mean *visible* would be a guess this tool cannot check. What
  the document does fix is which endpoints filter: the three read endpoints
  return only publicly visible rows and exclude hidden, denied and soft-deleted
  ones, while the change feed carries all of them. So **visibility is decided by
  the endpoint that delivered the row**, and the pair of strings it carried
  while it was visible is kept as a signature. A feed page reporting a different
  signature is a withdrawal; one reporting the same signature again is a
  restoration; a row this installation has only ever seen on the feed has no
  signature and is not shown until a snapshot includes it. A false negative
  costs a picture not shown until the next snapshot, and self-corrects. A false
  positive would be publishing what prdb withdrew, which is the outcome this
  refuses to risk.
- **A dead URL is this minute's answer, not the row's.** A `404` from the CDN
  for a row prdb still shows means the object moved or expired; the bytes are
  dropped and the URL is asked about again on the next demand, with ordinary
  backoff. Nothing is marked permanently, and there is no `FoundDead` for this
  population.
- **A changed URL or changed geometry** is a new version of the asset. The
  cached pair is invalidated together, so a sprite is never served against a
  WebVTT written for a different grid.

## Cadences, bounds and freshness

Every number this contract fixes, in one place — and in the code as
`Core/Sync/UserPreviewContract.cs`, so that the code downstream reads the figure
rather than restating it.

| | |
| --- | --- |
| Interest freshness | 7 days, whatever the answer was |
| Interactive read | one Video per run, `Sync` lane, 15 s cadence, one-shot per Video |
| Library enrichment read | one Video per run, `Bulk` lane, 60 s cadence, one-shot per Video |
| Change feed | one page per run, 1000 rows, hourly, `Sync` lane |
| Change feed due when | at least one interest row exists (ADR 0032) |
| Interest expiry | 90 days untouched, and the Video not pinned |
| Rows per Video | 60 kept, by `displayOrder`, `createdAtUtc`, `id` |
| Sprite ceiling | 8 MiB per file |
| WebVTT ceiling | 256 KiB and 400 cues per file |
| Tiles | 400 per sprite |
| Asset cache | 2 GiB, its own ceiling, least-recently-served first |

**The change feed costs nothing until something is interested.** It is a routine
with a work set (ADR 0032), and the set is the interest table. An installation
whose owner has never opened a Preview makes no request for this at all, which
is why `IdleProfile.RequestsAnHour` is unchanged: the idle profile is what the
schedule costs with nothing to do, and with nothing to do this does nothing.

## Where these requests stand in the order of precedence

[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)'s order is the
declaration order of `PrdbWork`, and this adds exactly one member to it.

- **Interactive reads are `Preview`.** Somebody opened a sheet, which is the
  same act, the same waiting person and the same 8 % reserve as ADR 0060's
  detail read. Giving browsing a second kind of work would divide one demand
  between two reserves for nothing.
- **Library enrichment and change-feed reconciliation are `UserPreviews`**, a
  new member between `Sites` and `Repair`, held back at **35 %**. Both are
  background work nothing is sitting in front of, and both must give up before
  the feeds that keep the Catalogue current — a Library preview arriving an hour
  late costs nothing, and a moderation change observed an hour late costs a
  picture being shown an hour too long. It still outranks the repair pass, which
  ADR 0014 gives only what is left above half the limit.
- **Uploads are `Writes`.** The companion epic submits generated previews, and a
  submission is a queued obligation exactly like a Fulfilment report. It is
  written here so that it is not *inserted* later by whoever builds it, which is
  how an order becomes a matter of opinion.
- **Nothing is starved.** `Identification` and `Verification` are held back at
  zero and are unreachable from here; `Writes` at 5 % is above every member this
  adds. The new member is below all six existing feeds, so no existing work
  gives up one request earlier than it did.

The **bytes** pass no governor, for ADR 0030's reason unchanged: a sprite is a
`GET` against a CDN, with no API key and no entry in the rate-limit headers. The
timeout, the size ceiling and the content check stand in the governor's place,
and this population adds the WebVTT bounds to that list.

## What is disposable, what is durable, and what a Restore finds

[ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md)
runs the export boundary between tables, so each new table declares one side.

- **`user_preview` — not exported.** Metadata that can be fetched again, about
  pictures that can be fetched again. It fails ADR 0009's *cannot be fetched
  again* test in both halves.
- **`user_preview_interest` — not exported.** Derived from what has been looked
  at and what is held. A Restore rebuilds it: the Library it restored is the
  interest, and the enrichment pass finds it as an empty work set to fill.
- **The `VideoUserImages` feed cursor — not exported**, like every other cursor.
  A restored installation takes prdb's clock again and re-reads what it needs.
- **`library_preview_asset` — exported.** The exception, and the reason is the
  filesystem rather than the network. It says *this tool wrote this file at this
  path beside this Video File, from this preview version* — a claim of ownership
  over something in the user's library. It cannot be re-derived, because a file
  beside a Video File may equally have been put there by a person, and a tool
  that guessed would eventually delete somebody's own sidecar. That is the same
  argument ADR 0017 made for recording the filed path rather than recomputing
  it, and the mirror image of `LibraryVerificationRow`'s argument for staying
  out.

A Restore therefore arrives with ownership records and no cached bytes, no
interest rows and no cursor — which is exactly the state the enrichment pass is
written for, and ADR 0032's self-emptying work set does the rest.

## What this amends

**[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)** gains one
kind of work in its order of precedence, `UserPreviews`, between `Sites` and
`Repair` at a 35 % reserve, and one routine with a work set whose idle cost is
zero. Its named condition — shed load in a fixed documented order when the plan
cannot carry the idle profile — is untouched, because the idle profile does not
move.

**[ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)**
said a Video File is read once, and what was read decides nothing. Both halves
stand and one gains a clause: the osHash a file was read for is now also what
selects the picture written beside it, so the *stored* hash is read again by
something new — the file is not. Nothing here reopens, re-probes or re-hashes a
file, and no local reading of a file has become a decision.

**[ADR 0023](0023-nothing-local-identifies-anything-and-a-pre-name-is-only-a-reason-to-ask.md)**
said nothing local identifies anything. That is the rule ADR 0062 has to
reconcile with, and this decision does not touch it: everything here is
metadata prdb published and moderated, held locally as a cache. Holding a copy
of prdb's answer is not becoming a second authority — but *acting* on it is, so
the acting is ADR 0062's to permit and to bound.

**[ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md)**
gains a second cache beside its own, with its own directory, its own ceiling and
its own eviction, rather than a second population inside it. The two are alike
enough to be tempting to merge and different in every property that matters: one
file per row against a *pair* of files, an immutable object against one that can
be withdrawn, a permanent `FoundDead` against a reversible visibility, and a
year-long `immutable` response against one that must be revocable. Sharing a
table would mean a nullable column for every one of those differences.

**ADR 0060** is extended rather than corrected. It deferred *take the pictures
from `GET /videos/{id}/user-images`* as "a decision of its own and wants making
on its own"; this is that decision. Its own gallery, its ceiling, its `Preview`
precedence and its narrowed pin are all unchanged, and the user previews are
shown beside its pictures rather than instead of them.

## What is deliberately left open

**The automatic Identification evidence policy.** What counts as sufficient
evidence, which Confidence an automatic assignment records, what happens when
two linked previews disagree, and what happens to an arrival already identified
when the evidence behind it is withdrawn — all of it is ADR 0062's. This
decision only guarantees the inputs: normalised hashes, current moderation
state, and a row that says which Video prdb linked a preview to.

**No permanent hints-only restriction is encoded here.** The user asked for
automatic Identification rather than for visual assistance, and nothing in this
contract stands in its way.

**The media-server output format.** Whether a sprite and a WebVTT beside a Video
File are discovered and played by Jellyfin is a question about Jellyfin, and the
answer is not predetermined. Until it is proven, no Library integration is
chosen and none is counted as delivered. **It was put to a running server and
answered: see the amendment at the end of this document.**

## Considered options

**One table with `catalogue_image`, distinguished by a column.** Rejected under
what ADR 0030 gains above: four of the differences are lifecycle rather than
shape, and every one of them would become a nullable column that half the rows
must ignore. ADR 0033 makes the schema the glossary made physical, and these are
two words.

**Mirror the global `changes` feed.** Rejected for the reason ADR 0013 rejected
mirroring the images feed, only more so: the population is larger than the
Catalogue, this installation can place a fraction of a percent of it, and the
rows it cannot place name Videos it will never hold. The feed is read for the
rows it is interested in and the rest of the page is dropped.

**Ask `by-os-hash` for every arriving file.** Rejected on the endpoint's own
documentation: it excludes linked rows, so the answer is empty exactly when a
Video could have been named. It has a use — an *unlinked* preview for a file
nobody has linked yet — and that use is the companion epic's, not this one's.

**Serve the CDN URL to the browser.** Rejected, and it is worth writing down
because these URLs are more tempting than `images[]`: they are large objects and
the tool would save the bandwidth. ADR 0030's answer applies unchanged — the
browser asks the tool and never the CDN, which is what makes the timeout, the
ceiling and the content check enforceable rather than conventional. It applies
harder here: a user preview is user-submitted content whose URL and geometry are
mutable, and a browser holding an address this tool cannot revoke would keep
showing a picture a moderator removed.

**Keep the assets in the Backup.** Rejected under ADR 0009's test, twice over:
they can be fetched again, and half of them may have been withdrawn by the time
anybody restores.

## Consequences

- **`CONTEXT.md` gains four words**: *User Preview*, *Sprite Sheet*,
  *Moderation Visibility* and *Timeline Preview*. The last is the pair written
  beside a Library Video File, and it is a different thing from the first — one
  is prdb's row, the other is what this tool may write from it.
- **`PrdbWork` gains `UserPreviews`** between `Sites` and `Repair`, and
  `PrdbBudget` a 35 % reserve for it. The staircase below `Preview` is untouched.
- **`Feed` gains `VideoUserImages`**, one more row of `FeedCursor`.
- **Three tables**, two disposable and one exported, and one new section of the
  Backup document.
- **A second asset cache** under `previews/`, with its own ceiling and its own
  eviction pass, holding pairs rather than files.
- **Two new routines** and one new feed routine, the last of which is due only
  while something is interested.
- **An entry in `docs/prdb-api-proposals.md`**, which is a new file: the first
  place this repository writes down what it would ask prdb for.

## Answered by ADR 0063

[ADR 0063](0063-the-timeline-preview-stays-the-media-servers-and-nothing-is-written-beside-a-video-file.md)
closes the one thing this decision left open, and the answer is no: a sprite and
a WebVTT beside a Video File are not something Jellyfin takes, Plex keeps its
preview thumbnails in a private store, and a WebVTT beside a video becomes an
external subtitle track. The control run that was meant to be a baseline
established something further — the server's import branch *replaces* its own
extraction rather than supplementing it, so anything written there is a downgrade
of a better preview the server makes from the file itself.

Two things above are therefore not built, and this is the record of it:

- **`library_preview_asset` does not exist.** *Three tables, two disposable and
  one exported* is two tables, both disposable. Nothing is written into the
  user's library, so there is no ownership to record — the very argument that
  earned the table its place on the exported side is what removes it.
- ***Timeline Preview* means the other thing.** The glossary keeps the word and
  turns it around: it names what the media server makes, and what this tool
  deliberately does not write.

Everything else here stands unchanged. The interest a filed Video File registers,
the lane it reads in, the share it spends and the fact that it never expires are
all still right — ADR 0063 only replaces the reason, which is now that those
bindings are ADR 0062's evidence rather than an asset to reconcile.
