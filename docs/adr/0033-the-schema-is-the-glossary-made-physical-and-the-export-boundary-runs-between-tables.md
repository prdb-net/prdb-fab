# The schema is the glossary made physical, and the export boundary runs between tables

Thirty-two decisions each added or removed columns, and this turns that prose
into a schema. It settles four things the prose never could: what identity each
row has, whether the export boundary is closed, where the account cut runs, and
whether pinning is a column or a query.

Two of those answers correct earlier ADRs. **Pinning is a query**, against the
"pin reason per row" that [ADR 0013](0013-the-prdb-catalogue-is-a-cache-with-pinned-rows-repaired-by-re-reading.md)
and [ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md) both
assumed; and the **entry directory sits on the library entry**, not beside each
file.

## Table names are glossary terms, and three exceptions are named

Every table is a term from `CONTEXT.md`, and no table or column carries a word
that term lists under `_Avoid_`. That is a mechanical check rather than a
matter of taste, and it caught three places where the obvious name is the
forbidden one.

**`Cursor` is reserved against Watermark, and the feed table keeps it anyway.**
`CONTEXT.md` lists *Cursor* under **Watermark** — but that entry is about the
indexer side, "how far an indexer walk has already come". prdb's change feeds
carry a cursor the API itself names and documents. So `FeedCursor` keeps prdb's
word because it is prdb's word, and the indexer walk's own progress is a
**watermark** and a resume page, never a cursor and never an offset.

**`Verdict` is reserved against Identification State, and the indexer row keeps
it.** `CONTEXT.md`'s **Connection** is defined as a route "together with the
credential it is reached by and the verdict of the last check against it". The
prohibition is on calling an identification state a verdict; a connection check
genuinely has one.

**`durationMs` keeps prdb's spelling even though *Duration* is reserved.** The
three columns from [ADR 0031](0031-the-consensus-runtime-is-shown-beside-the-files-own-and-still-decides-nothing.md)
are quotations of an API field, and renaming a quotation is how a reader stops
being able to find it in the OpenAPI document. The **term** for what they hold
is Consensus Runtime; the column names are prdb's.

## The export boundary runs between tables, never through one

A table is exported whole or not at all.

The alternative — a column list per table — makes the shape of the backup
document depend on a set nobody can check, and
[ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md) built
that document to be **read** by a person. It also has one concrete victim in
this schema: `CONTEXT.md`'s **Connection** puts an indexer's URL, key and last
verdict together with its rank and daily query budget, all configuration and all
exported — while ADR 0015 puts a watermark, a resume page and a stored caps tree
on the same conceptual thing, and every one of those is cache that refills
itself.

So the indexer is **two rows in two tables**: `Indexer`, exported, and
`IndexerWalkState`, not. Nothing else in the schema needed splitting, which is
worth recording as evidence that the rule is cheap.

## Why the boundary is closed, and what actually makes it so

Five exported tables reference rows that are not exported:

| Exported row | References |
|---|---|
| `LibraryEntry` | catalogue video, catalogue site |
| `Download` | catalogue video, cached release |
| `ReportedState` | catalogue video |
| `ConfirmedAssignment` | catalogue video |
| `OperationLogEntry` | catalogue video |

Read as foreign keys these are five restores that cannot load. They are not,
and the reason is the rule this decision extracts and then imposes:

> **An exported row may reference a non-exported row only through an identifier
> that some outside authority owns — prdb's video or site id, or ADR 0015's
> derived release identity. Never through a locally minted surrogate.**

Every one of the five satisfies it. A restored `LibraryEntry` names a prdb video
id; the catalogue is empty; ADR 0013 pins what something local points at, and a
library entry is one of those things, so the row is fetched and pinned on the
next repair pass. Nothing dangles because nothing pointed at a local row in the
first place.

**This is what makes ADR 0016's consumed state survive a restore, and it is
load-bearing in a way that was not previously visible.** That ADR says the
download row *is* the consumed state — a release is consumed for a video exactly
when a download row exists for the pair. After a restore the cache is empty, so
the release row is gone. When a walk re-sees that release, ADR 0015's identity
ladder derives **the same** id from the same guid, the download row matches
again, and the release stays consumed. Had ADR 0015 minted release ids locally,
every restore would silently free every consumed release and the retry budget
would start spending them again.

The stated price: immediately after a restore, a download's release is a key and
a name rather than a row. The downloads surface
([ADR 0028](0028-downloads-are-a-table-of-their-own-and-the-release-view-answers-for-the-video.md))
falls back to the **submitted name**, which ADR 0016 already keeps for exactly
this class of reason.

The one reference that goes the other way is `Download.originRuleId` → 
`AutomationRule`, and both are exported. ADR 0028 already made it nullable with
the rule's name copied beside it, so a deleted rule leaves a readable row.

## Identity

**Exported tables get a UUIDv7. Non-exported tables get an integer surrogate.**

The boundary is exactly where identity has to be self-describing, because the
backup document is the one place a row exists outside the database that minted
it. Inside the cache nothing outlives a rebuild, so an integer costs less and
means nothing. ADR 0009 restores onto an empty installation only, so an integer
would in fact round-trip — the reason to spend the extra bytes is that a UUIDv7
is sortable, needs no sequence to be restored alongside it, and makes it
impossible to write a restore that accidentally depends on ordinal values.

**Three exported tables need no minted key at all**, because they already have a
natural one, and using it removes a whole class of restore bug:

- `LibraryEntry` — the prdb video id. ADR 0012 fixes one entry per video, so the
  video id *is* the entry's identity, and a second quality is a second
  `VideoFile` under it.
- `ReportedState` — (video, `userHash`), which
  [ADR 0019](0019-fulfilment-understates-the-quality-and-is-retracted-only-by-a-person.md)
  states as the row's shape.
- `ConfirmedAssignment` — (`osHash`, video, `userHash`), which
  [ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
  states as the row's shape.

**Paths are absolute in the database and root-relative in the export.**
ADR 0009 already requires the second. The first is
[ADR 0017](0017-the-filed-path-is-computed-once-and-then-recorded-rather-than-recomputed.md)'s
rule taken seriously: the record is the authority and nothing recomputes a path,
so a stored relative path would put a concatenation with the current library
root on every single read — which is
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)'s
"the library root alone is history" reintroduced as a runtime dependency.
Changing the root stays what ADR 0020 calls it, a **re-rooting**: one bulk update
over stored paths, refused while filing is running.

## Pinning is a query, not a column

ADR 0013 speaks of "a pin reason per video row" and ADR 0015 of a pin reason on
the release row. Both are amended: **nothing stores a pin, and nothing stores a
pin reason.**

`CONTEXT.md` already defines **Pinned** as derived — "said of a row the tool
must keep because something local points at it" — and lists what may point:
a library entry, a wanted video, a download, a review queue entry, the candidate
videos of an open entry, a cached release that was downloaded, consumed, or
identified as a video still wanted.

A stored flag is a second place the truth lives, which
[ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
refused for the review queue count and ADR 0016 refused for a consumed-releases
list. Here it is worse than in either, because it has **six writers and no
reader that would notice a mistake**: a pin never cleared keeps a row forever,
which shows only as a cache that quietly stops evicting, and a pin cleared too
early drops exactly the row ADR 0015 said must never be dropped.

The performance objection is the reason it looked necessary, and it does not
survive contact with how eviction actually runs. Eviction does not scan the
table: it walks rows in first-seen order and stops when it has freed enough
(ADR 0015), so the `NOT EXISTS` clauses are evaluated over the candidates it
looks at, not over a hundred thousand rows. Every referencing column is indexed
for other reasons anyway — see below.

Nothing is lost for diagnosis either: the query knows which `EXISTS` matched, so
"why is this row pinned" is answerable without a column that can be wrong.

## The entry directory belongs to the entry

The fog patch recorded ADR 0017's directory as stored "beside the file". It is
stored on the **library entry**, once.

`CONTEXT.md` defines **Entry Directory** as "the *one* directory a library entry
occupies, holding its video files, its sidecar and its entry image", and ADR 0012
fixes a second quality as another file of one entry. Per file it would be the
same string repeated with the ability to disagree with itself — and the
disagreement would be undetectable, since ADR 0017 makes the record the
authority and nothing on disk contradicts it.

Each `VideoFile` keeps its own **filed path**, which lies inside that directory
and moves when ADR 0011's relabel renames it.

## The account cut is a property each table declares

Three classes, and every table below is in exactly one:

- **Account-scoped** — deleted when the prdb key belongs to a different account.
  `WantedVideo`, `FavouriteSite`, `FavouriteActor`, their three `FeedCursor`
  rows, and [ADR 0024](0024-the-wanted-sweep-asks-with-a-title-from-a-reserved-share-of-the-budget.md)'s
  `WantedVideoSweepState`, which hangs off the wanted list and would otherwise
  order a sweep by another account's history.
- **Account-stamped** — carries `userHash` and is never deleted. `ReportedState`
  and `ConfirmedAssignment`, exactly as ADR 0019 and ADR 0022 require, so an
  assignment the previous account submitted is not counted as sent by an account
  prdb never heard it from.
- **Account-free** — everything else, including the whole library, which ADR 0013
  explicitly keeps pinned across the change.

Writing it as a class per table rather than as prose makes the key-change
operation a list of `DELETE`s that can be read off the schema, instead of a
procedure someone has to keep in step with new tables.

## The tables

Twenty-four. Exported ones are marked **E**; account class is noted where it is
not account-free.

### Installation and access

- **`Installation` (E)** — one row. Password hash, prdb API key, prdb
  `userHash`, library root, the onboarding step marker, the SABnzbd URL, key,
  category and path mapping, the retry budget, the automation cap, the leftover
  switch, the two reporting switches. One row with typed columns and **not** a
  key–value table: ADR 0020 admitted each of these against a test, and a
  key–value table is an invitation to settings nobody argued and an untyped
  backup.
- **`GateAdmission` (E)** — (gate, confidence). The physical form of
  [ADR 0006](0006-acting-alone-needs-a-named-video-and-an-allowed-confidence.md)'s
  two sets. A table rather than a delimited column, because a list inside a
  string is one the schema cannot constrain and the export has to parse.
- **`Session`** — the revocable row behind the cookie (ADR 0010). Not exported,
  as that ADR says.

### Connections

- **`Indexer` (E)** — id, name, URL, API key, enabled, rank, daily query budget,
  the category names matched by name (ADR 0002), the last check's verdict.
- **`IndexerWalkState`** — indexer, watermark (post date **and** held release
  identity, per ADR 0015's outward cursor), resume page, stored caps tree,
  queries spent today. Cache; refills itself.
- **`AutomationRule` (E)** — id, name, enabled, minimum and maximum size.
- **`AutomationRuleIndexer` (E)** — rule, indexer. ADR 0020's rule left with no
  permitted indexer is *disabled rather than left inert*, so the disabled flag
  above is written by that rule and not derived from this table being empty.

### Catalogue — none exported, all refetched

- **`CatalogueVideo`** — prdb id, title, normalised title, site, release date,
  `durationMs`, `durationSpreadMs`, `durationFileCount`, prdb's `updatedAtUtc`,
  when it was last re-read, and whether its title has been searched backwards
  ([ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)).
- **`CatalogueVideoPreName`** — video, pre-name, normalised pre-name, searched
  backwards.
- **`CatalogueVideoActor`** — video, actor.
- **`CatalogueSite`** — prdb id, title, network, still offered. Never deleted,
  per ADR 0013, because a library entry must still name the site its path was
  built from.
- **`CatalogueActor`** — prdb id, name.
- **`CatalogueImage`** — prdb image id, video, URL, whether the bytes are
  cached, whether the URL was found dead, when it was last served
  ([ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md)).
- **`FeedCursor`** — feed, cursor, plus the What's New high-water mark and
  backfill position on their own row. Three of these are account-scoped.
- **`WantedVideo`** *(account-scoped)* — video, since when.
- **`FavouriteSite`**, **`FavouriteActor`** *(account-scoped)*.
- **`WantedVideoSweepState`** *(account-scoped)* — (wanted video, indexer), last
  searched. ADR 0024's pair, not per video.

### Indexer cache — none exported

- **`Release`** — surrogate id; (indexer, derived release id) unique; raw guid,
  title, normalised title, size, categories, post date, `pubDate`, download URL,
  first seen, identification state (seven values, ADR 0023), `videoId`,
  `confidence`, `matchedBy`, site, and ADR 0024's boolean saying a search was the
  reason it is `Awaiting`.
- **`ReleaseCandidate`** — release, video. A table and not a JSON array,
  because ADR 0013 pins the candidate videos of an open entry and the pinning
  anti-join needs them as rows.

### Library — exported

- **`LibraryEntry` (E)** — prdb video id as the key, entry directory, the real
  filing time ADR 0019 sends rather than letting prdb stamp.
- **`VideoFile` (E)** — id, library entry, filed path, quality label, size,
  runtime, width, height, video codec, `osHash` (ADR 0021's four plus the hash).

### Acquisition — exported

- **`Download` (E)** — id, video, indexer, derived release id, submitted name,
  `nzo_id`, one of four states, cause, last seen SABnzbd status, `fail_message`,
  `stage_log`, consecutive-absence count, outstanding since, tidied-at, the
  origin rule and the origin rule's name at the time (ADR 0028), started by a
  person or not, created. Never pruned.

### Filing — not exported

- **`ArrivingFile`** — id, download, indexer and derived release id, the path it
  was found at, the name it arrived under, whether it is still on disk, one of
  four states, a **nullable reason**, video, site, the six probe values, the
  intended path while `Filing`, and when it was last attempted (ADR 0032).
- **`ArrivingFileCandidate`** — arriving file, video. A table, for the same
  pinning reason as `ReleaseCandidate`.

### Reporting — exported

- **`ReportedState` (E)** *(account-stamped)* — (video, `userHash`), fulfilled or
  not, the quality rung and timestamp last sent, the terminal marker for
  `NotWanted`/`NotFound`.
- **`ConfirmedAssignment` (E)** *(account-stamped)* — (`osHash`, video,
  `userHash`), size, the arrival file name, the release name, the four probe
  values, what prdb answered, when it was sent.

### The record — exported

- **`OperationLogEntry` (E)** — id, the act, the video file, library entry,
  video and download where each is known, the path before and after, the
  displaced path for a replace, the leftover names as a JSON array, the actor,
  the reason, the time. The leftover names are an array and not a child table
  because nothing ever queries by one — the opposite of the candidates above,
  and the difference is exactly whether a row is ever a join target.

### Scheduling and observation — not exported

- **`Routine`** — name, lane, cadence, whether the cadence is a clock or an idle
  tick, due at, last success, last failure, consecutive failures, retired.
  A **resume position only on one-shot routines**, which is all ADR 0032 left of
  it.
- **`RoutineRun`** — routine, started, finished, outcome, note. Fifty per
  routine.
- **`IdentificationOutcome`** — when, which gate, which named outcome.
- **`ReleaseNotDownloaded`** — when, the reason the ranking discarded.

The last two are ADR 0018's seven-day tallies. **Neither gets a cleaning
routine**: the insert deletes anything past the window in the same statement.
A seventh routine to prune two tables that only ever grow by one row per event
would be machinery around a `DELETE`.

## Indexes, stated rather than left to be discovered

- **Six work-set state columns** — `Release.identificationState`,
  `ArrivingFile.state` (twice over, for two routines),
  `CatalogueVideo.titleSearchedBackwards`,
  `CatalogueVideoPreName.searchedBackwards`, and `CatalogueImage.cached` filtered
  to pinned videos. ADR 0032 makes each of these a `COUNT` every tick.
- **`ArrivingFile.reason` where not null**, a partial index, because ADR 0022
  puts that count in the header of *every* page.
- **`OperationLogEntry.videoId`**, for the library entry page (ADR 0029).
- **`Release.videoId`**, because ADR 0012's release view selects on it — and
  ADR 0025 requires that it never selects on title text.
- **`Download` on (video, indexer, derived release id)** for the consumed check,
  and on **video** alone for the retry budget, which is that count.
- **`Download` on (state, created)** for ADR 0028's table.
- **`Release.firstSeen`** and **`CatalogueImage.lastServed`**, the two eviction
  orders.
- **Every column a pinning anti-join reads** — `LibraryEntry.videoId`,
  `WantedVideo.videoId`, `Download.videoId`, `ArrivingFile.videoId`,
  `ReleaseCandidate.videoId`, `ArrivingFileCandidate.videoId`, and the release
  side of the same.
- **`IdentificationOutcome.at`** and **`ReleaseNotDownloaded.at`**, for the
  window.

**No index on any normalised column, and no FTS5 table anywhere.** ADR 0025
closed that question, and the reason is worth restating in index terms: its
query is `LIKE '%needle%'`, which no B-tree can serve, which is precisely why an
indexless pass beat a trigram index that cost +119 % on the most continuously
written table in the schema.

## A migration is three different things

1. **A schema migration.** Runs at startup, without a person present, and must
   complete or the container does not serve. Everything structural.
2. **A backfill routine.** Where the new value comes from outside the database —
   ADR 0021's read of every video file, a prdb re-read. A one-shot routine with
   a lane and a resume position, retiring when done, exactly as ADR 0021
   specified and ADR 0032 left intact for one-shots.
3. **A recompute inside the migration.** Where the value is derived from columns
   already present, which is ADR 0025's normalisation.

The third is not a special case of the second, and getting it wrong is silent.
If a normalisation change were left to a routine, then between the migration and
the routine finishing, the backwards search would find **nothing** for the
un-recomputed rows — no error, no Gap, just wanted videos not found. That is
ADR 0015's silently skipped row and ADR 0032's lost signal, met a third time, so
the rule is: **a derived column is recomputed by the migration that changes its
derivation, never afterwards.**

## Considered options

**Export the catalogue so the boundary has no cross-references.** Rejected: it
contradicts ADR 0009's test outright — the catalogue is refetchable by
definition — and it would put a cache of unbounded size into a document a person
is meant to read.

**Draw the export boundary per column.** Rejected under *the boundary runs
between tables*: it makes the document's shape depend on an unauditable list,
and the one table that needed splitting was cheap to split.

**Store a pin flag with a reason.** Rejected under *pinning is a query*: six
writers, no reader that would notice a mistake, and two failure modes that are
both invisible.

**Integer keys everywhere, since ADR 0009 restores onto an empty installation.**
Rejected narrowly. It would work, and it makes the restore depend on ordinal
values being reproduced faithfully — a property nothing checks and one that a
future feature (a partial restore, a merge) would break silently.

**A key–value settings table.** Rejected under `Installation`: it admits
settings that were never argued against ADR 0020's test, and it produces an
untyped backup.

**One `Candidate` shape for both releases and arriving files.** Rejected: they
reference different parents, and a shared table would need a discriminator whose
only purpose is to let one index serve two queries that never run together.

**A pruning routine for the two observation tables.** Rejected: a seventh
routine, its own Gap, its own row in ADR 0014's table, to run a `DELETE` that
the insert can do.

**Store the entry directory per file.** Rejected under *the entry directory
belongs to the entry*: the same string repeated with the ability to disagree,
and nothing on disk to catch the disagreement.

**Keep the filed path relative to the library root.** Rejected: it makes every
read depend on a setting ADR 0020 classes as history, and it turns
re-rooting from a bulk update into an invisible global behaviour change.

## Consequences

- **ADR 0013 and ADR 0015 are amended**: no pin reason is stored on either the
  catalogue video row or the release row. Everything both ADRs say about *what*
  is pinned and *what may not be evicted* stands unchanged; only the storage
  does not.
- **ADR 0017 is amended**: the entry directory is on the library entry.
- **ADR 0009 gains its answer**: the identity that survives a round trip is a
  UUIDv7 for exported tables, three natural keys where they exist, and an
  outside authority's identifier for every cross-boundary reference. Its
  root-relative path rule becomes a rule about the *export*, with the database
  holding absolute paths.
- **ADR 0015's derived release identity turns out to be load-bearing for the
  backup**, which nothing had noticed: it is the only reason a consumed release
  stays consumed across a restore.
- **`CONTEXT.md` is unchanged.** No new term is needed, and the mechanical check
  against every `_Avoid_` list passes, with the three deliberate exceptions
  named above.
- Twenty-four tables, of which ten are exported. The largest two —
  `OperationLogEntry` and `Download` — are the two that grow with the library
  forever and are exported for the same reason.
- **Nothing in the schema is a condition, a Gap, a Brake or a pin.** All four are
  computed at read time from rows that exist for other reasons, which is what
  ADR 0018 required and what this decision extends to pinning.
- [Ticket 33](../../.scratch/first-release-spec/issues/33-how-the-tool-is-run-and-documented.md)
  inherits one fact from here: what grows on the data volume is the two exported
  tables above, the artwork cache under its ceiling, and the indexer cache under
  ADR 0015's hundred thousand rows per indexer.
