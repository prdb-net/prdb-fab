# A queue entry is one unmoved file with one reason, and a confirmation outlives it

A review queue entry is one video file the tool declined to move, carrying one
reason — the first that applied. Every entry offers the same two exits, Delete
and Dismiss, and at most one action that does something beyond them, chosen by
the reason. Confirming which video a file is leaves a record behind that
outlives the entry, because that record is what the second reporting channel
sends and what its switch counts before it is thrown.

## One row per video file, and one reason on it

**The unit is the video file.** A download that leaves two video files behind,
one of which files, produces one entry. The download is a column on that row,
never a grouping level with actions of its own: one list whose rows mean two
things, and whose bulk actions mean two things, is the mistake
[ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)
already refused when it kept the queue out of the library.

An entry carries exactly one reason, and a file can satisfy several. The reason
is the first that applies, in this order:

1. **`IdenticalFile`** — the `osHash` is one the library already holds.
2. **`UnreadableQuality`** — `ffprobe` could not read a quality, so
   [ADR 0011](0011-a-duplicate-is-decided-after-the-download-by-quality-label.md)
   files nothing whatever the library holds.
3. **`Unidentified`** — no named video: `None` or `Partial` under the gate of
   [ADR 0006](0006-acting-alone-needs-a-named-video-and-an-allowed-confidence.md),
   `Ambiguous` with its candidates, or a Site-Only Match.
4. **`Duplicate`** — a named video the library already holds at that quality
   label.
5. **`EntryMissing`** — the video is held, and the scene directory recorded for
   it is gone
   ([ADR 0017](0017-the-filed-path-is-computed-once-and-then-recorded-rather-than-recomputed.md)).

The order is not arbitrary. The `osHash` comparison is local, costs nothing, and
is a stronger answer than anything prdb can give: a file whose bytes are already
filed is answered by the tool's own record even when
`POST /videos/identify` says nothing about it, and calling such an entry
*unidentified* would hide the answer the user needs. Unreadable comes next
because that file does not file however perfectly it identifies. The three cases
under `Unidentified` are **one** reason with different evidence rather than
three: their exits are identical, and the candidates are a shortcut inside the
one that matters.

Waiting files are still not compared against each other.
[ADR 0011](0011-a-duplicate-is-decided-after-the-download-by-quality-label.md)
rejected that and the reason is unchanged: declaring one of two waiting files
the original picks a winner nobody chose.

## Two exits everywhere, one action per kind

Every entry, whatever its reason, offers the same two exits, both with multiple
selection:

- **Delete** — the video file is deleted, behind a confirmation that names every
  file it covers. At twenty selected files that is twenty lines with their
  sizes; ADR 0011 requires the confirmation to name the file, and a count is not
  a name.
- **Dismiss** — the entry is closed and the file is left exactly where it is.
  This is what unblocks the cleanup of a download directory without deleting
  anything the user did not agree to lose, and it is ADR 0011's *leave it* under
  a word that cannot be misread as deletion.

On top of that, at most one acting exit, fixed by the reason:

| Reason | Acting exit |
|---|---|
| `Unidentified` | **File it as …** — the user names the video |
| `Duplicate` | **Replace**, exactly as ADR 0011 specifies it |
| `EntryMissing` | **File it as the only copy** |
| `IdenticalFile` | none |
| `UnreadableQuality` | none |

The last two have nothing to choose. Two identical files leave no decision worth
offering, and a file whose quality could not be read must not be filed at all,
because every filed file carries a known quality.

**There is no Defer exit.** Doing nothing *is* deferring: nothing expires and no
entry is ever closed by the passage of time. A button that does nothing suggests
a deadline that does not exist.

## What an entry shows

[ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)
caps what may come out of the file at six values, and every collected file is
read before anything is decided about it, so no entry is ever without them.
Every row carries the same columns whatever its reason: the file name as it
arrived, the **runtime**, the quality, the dimensions, the size, the video
codec, and the release with its indexer.

The runtime sits **beside the quality and not behind a disclosure**. ADR 0021
refused a minimum-runtime gate because prdb publishes nothing to calibrate one
against, and made the sample visible here instead — four minutes beside `1080p`
is the entire mechanism by which a sample is caught, and Ticket 01 made samples
the ordinary case rather than the rare one. An entry that buries the runtime
silently repeals that decision.

One block is added per reason: `Unidentified` shows the confidence, the
`matchedBy` rung, and either the candidates or the site; `Duplicate` shows both
qualities and both sizes side by side with the filed file's path; `IdenticalFile`
shows the filed file; `EntryMissing` shows the directory that is not there;
`UnreadableQuality` shows what `ffprobe` reported. The `osHash` is prominent on
no row at all — it is a search term, not a verdict.

## Choosing the video, and what happens after

**File it as …** has three entry points into one picker. Candidates come as
cards with artwork and resolve in one click, which is the common case and has to
be the fastest. A Site-Only Match opens the picker with `SiteId` already set,
ordered by release date descending. Everything else is free text.

The search runs **live against prdb**, `GET /videos` with `Search`, not against
the catalogue. The catalogue is a cache of what has been looked at
([ADR 0013](0013-the-prdb-catalogue-is-a-cache-with-pinned-rows-repaired-by-re-reading.md)),
and a video that failed to identify is by definition not in it. The request
passes the governor like every other, including one a person asked for
([ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)), and the
chosen video is written into the catalogue and pinned.

Naming the video does not file the file. **It puts it back through the ordinary
filing checks**, which the missing video is all that held up — so the entry may
not disappear at all: it can come back as a `Duplicate` or an `EntryMissing`
with the exits that belong to those. That is the reason ordering above, walked
once.

## An entry whose directory is gone

ADR 0017 files nothing when the recorded scene directory is missing, because one
`stat` cannot separate a user tidying up from a mount that did not come up. One
fact narrows it here: if the **library root** is present and writable, the total
failure is already excluded — filing under an absent root fails before it
reaches a directory (ADR 0011).

So the entry states what the tool sees — root present, directory not — and
offers **File it as the only copy**: the record is corrected and the file is
filed unlabelled, which is ADR 0017's first case reached by hand. It is never
worded as *file it anyway*. Beside it stand Dismiss and Delete.

Nothing about this entry may be phrased as though prdb had been told anything.
The fulfilment claim stands: ADR 0019 retracts only when a person removes the
library entry, never because the disk stopped answering, and a missing directory
is exactly the disk not answering.

## The confirmation, and the channel it feeds

`VISION.md` promises a second reporting channel — hash-to-video assignments
confirmed by hand — and
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)
built it a named place on the Reporting route and left it empty for this
decision. `POST /videos/filehash-submissions` is that channel, and it carries an
obligation the API states and cannot enforce: a `UserConfirmed` submission
**must be strictly opt-in and off by default**, because a person is vouching for
it.

**Eight fields go out and no ninth.** The four the endpoint requires — the
video, the `osHash`, the file size and `source: UserConfirmed` — plus
`durationMs`, `width`, `height` and `videoCodec`, which are exactly the four
ADR 0021 stores. This channel is not a reason to read a file a second time, so
what the probe did not produce is left out rather than guessed. `releaseName`
goes as the release the file came in. `filename` goes as the name the file
**arrived** under, never the filed one: the filed name is this tool's layout
convention and tells prdb nothing.

**A confirmation is written when the user names the video, whatever becomes of
the file.** If it turns out to be a duplicate and the user deletes it, the
assignment is still true — they said which video those bytes are, and the bytes
outlive the file. The record is a **Confirmed Assignment**: one row per
`osHash` and video, scoped by `userHash` the way ADR 0019 scopes reported state,
so an assignment the previous account submitted is not counted as sent by an
account prdb never heard it from.

Nothing is sent at the click. A routine in the sync lane drains what has not
been sent, 200 per request, exactly as ADR 0019 drains fulfilment. All four
per-entry outcomes are terminal: `Recorded` and `Updated` are done, and
`VideoNotFound` and `Conflicted` are **not retried**, because a retry can never
change either. Both appear in the routine's run log and nowhere else — a
surface for *prdb disagrees with you* would be a surface nobody can act on in
the first release.

The switch is off, opt-in, and **throwing it on sends the whole backlog**: every
confirmation ever made and not yet sent for this account. That is the count
ADR 0020 requires to be named before the switch is thrown. Turning it off stops
sending and retracts nothing — the same asymmetry as ADR 0019, for a harder
reason: this channel has no retraction at all.

## Nothing here is reversible

No queue decision can be undone, there is no window and no wastebasket. What
protects the user is the confirmation before the one destructive action, not a
way back after it. A wastebasket would be a second place video files live, in a
tool whose whole shape is one library in one place.

This puts weight on **File it as …** that the first release does not lift:
reassigning a file that has already been filed is out of scope, so a wrong video
chosen here is repaired by hand on disk. `VISION.md` requires every move and
every deletion to be logged with what it was and why, and the queue is where a
person triggers deletions — every queue action belongs in that log. What the log
holds and who reads it is not settled here.

## An open entry brakes the video

A download that arrives unidentified is not a release failure under
[ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md):
the download succeeded. But the video stays wanted and unheld, so automation
would reach for the next release on the next pass — and in the sample case that
is a loop, because the next release is another sample and nobody is watching.
The retry budget bounds it, but every pass leaves a file on the disk and a row
in the queue.

**So an open entry holds automation for that video.** While a video has an open
queue entry, no rule downloads another release for it. That is the tool working
exactly as configured and therefore not acting, which makes it a **Brake** under
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md),
with a route to the entry. Waiting for the question to be answered is cheaper
than asking it five times.

## Lifecycle, and the download directory

An entry is created at collecting, after the probe and the identification, and
is closed **only** by a user action. Nothing expires.

(*Amended by
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md): not in
every case. The row exists from collecting for every arriving video file, and a
reason is written by whichever step reached one — `IdenticalFile` and
`UnreadableQuality` during collecting, before any prdb request exists;
`Unidentified` by the identification; `Duplicate` and `EntryMissing` immediately
before the move, where the `stat`s this ADR and ADR 0017 require are still
fresh. An entry is a row **with a reason**, which is what keeps a file merely
waiting to be asked about out of the count in the header.*)

**Delete removes the one video file and never a directory.** The directory is
then ADR 0005's business: leftovers go once no video file in it is still
undecided. That cleanup is a **routine** in the bulk lane rather than something a
click performs, so a sweep that fails against a directory is retried rather than
lost — the same argument ADR 0016 made for collecting.

If the file disappears from disk while the entry is open, **the entry does not
disappear with it**. It is marked as no longer on disk and stays dismissible. An
entry that deletes itself on a failed `stat` is ADR 0017's confusion of a
tidy-up with a failed mount, arrived at from the other side.

An open entry **pins** its catalogue video and its cached release, which is
already what `CONTEXT.md` says under *Pinned*.

## The surface

A table beside the library, newest first, paginated the way the library is, with
a filter on the reason and a column for the download. Selection spans the whole
list and the action bar offers only what applies to **all** of the selection —
in practice Delete and Dismiss, since the acting exits are reason-bound. There is
no bulk confirm across several videos.

The count in the header of every page (ADR 0012) and at the *File* stage of the
status page (ADR 0018) counts **all open entries regardless of reason**,
including those whose file has gone: it answers *is anything waiting for me*, and
there the answer is yes. It is read as a `COUNT` over an indexed column rather
than kept as a running total — a second place the truth lives drifts, and
ADR 0012's requirement was that it not be computed from the filesystem.

**A full queue is never a Gap and never a Brake.** Nothing is broken and nothing
is being withheld by configuration; it is work waiting for a person. The gate
Brake ADR 0018 already carries is untouched by this.

## Considered options

**Reassign a file that has already been filed.** This is the one exit that would
fire ADR 0019's second retraction trigger — a misidentification corrected and
refiled as a different video. Rejected as out of scope: such a file is by
definition not a queue entry, since it was moved, and reassigning it means
rewriting the sidecar and the poster, moving the file between scene directories,
withdrawing the fulfilment claim and withdrawing the confirmed assignment that
vouched for it. That is an apparatus, not an exit. ADR 0019's rule stands
correctly; its second trigger simply never fires in the first release.

**Submit `ClientDetected` assignments as well.** The API accepts them, says they
may be on by default, and prdb evidently wants them. Rejected: `VISION.md` names
one channel, the confirmed one, and this would be a third switch with its own
default and its own explanation of what leaves the machine unasked. The value of
this channel is the human judgement it carries — that is what prdb cannot obtain
otherwise.

**A Defer exit.** Rejected under *two exits everywhere*: it is a button that does
nothing, and it implies a deadline the queue does not have.

**An undo window, or a wastebasket the deleted file goes to first.** Rejected: it
is a second place video files live, and the tool's shape is one library in one
place. The confirmation that names every file is the honest version of the same
care, and it is the shape ADR 0011 already chose for the replace.

**Let automation keep fetching and rely on the retry budget.** Rejected under
*an open entry brakes the video*: it spends bandwidth and disk on files nobody
asked for, and fills the queue with five rows where one asks the question.

**Search the catalogue instead of prdb when naming a video.** Rejected: the
catalogue holds what has been looked at, and the video that failed to identify
is exactly what has not been. It would fail in the most common case and look
like prdb not knowing the video.

**Keep the queue count as a running total.** Rejected: a maintained counter is a
second source of truth that drifts, and the query it replaces is a count over an
indexed column.

**Let a non-empty queue raise a Gap once it grows past some size.** Rejected:
nothing is broken, and a Gap that appears because a person has not got round to
something teaches them to stop reading the page — the failure ADR 0018 exists to
prevent.

**Compare waiting files against each other, so two copies in one job resolve
themselves.** Rejected again, as in ADR 0011: it picks a winner nobody chose.

## Consequences

- `CONTEXT.md` gains **Confirmed Assignment** and **Dismiss**. *Redundant* is
  not introduced as a word: **Duplicate** already means precisely that reason,
  and a second word for it would be a second term.
- The schema gains a **review queue entry** — the video file with the row shape
  ADR 0021 fixed, the reason, the download and release it came from, the video
  or the site where one is known, the candidates where there were several, and
  whether the file is still on disk. Its open count has to be indexed. It is not
  exported: it describes files in a download directory, which ADR 0009 does not
  carry.
- The schema also gains a **confirmed assignment** row — `osHash`, video,
  `userHash`, size, the arrival file name and release name, the four probe
  values, and what prdb answered. This one **is** exported: it is a human
  judgement that cannot be fetched again, which is ADR 0009's test exactly, and
  losing it means a restored installation re-asks questions the user has already
  answered.
- ADR 0020's Reporting route gets its second switch filled in, with its own count
  and its own text. Both switches remain separate, per `VISION.md`.
- ADR 0018 gains a Brake: automation held for a video with an open entry.
- ADR 0014 gains a routine in the sync lane, draining confirmed assignments, and
  one in the bulk lane, sweeping download directories that no longer hold
  anything undecided.
- The picker is the first prdb request a person triggers synchronously and the
  governor may defer. That does not contradict ADR 0018's *refreshing never
  causes work*: a search is work the user asked for, not a page reading itself.
- `VISION.md`'s operation log is now a named requirement with no home. It is
  pulled out as its own question rather than settled in passing here.
- The queue's rows have to be addressable one at a time, so that the Brake above
  and a download row can both route to a single entry. (*Added by
  [ADR 0028](0028-downloads-are-a-table-of-their-own-and-the-release-view-answers-for-the-video.md),
  which needs both directions; it is a filter on the table this section
  describes, not an entry page.*)
