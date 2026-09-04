# The operation log records one act per video file, and nothing reads it back

One entry per **act on content**: a video file moved, relabelled, replaced or
deleted, and one entry per leftover sweep. Not one per filesystem step. Every
entry names who acted and why, the log is a read-only surface in the first
release, nothing prunes it, and it is exported.

Nothing in the tool ever reads it. That is the property that makes the rest of
this decision safe.

## What the log is actually for in the first release

`VISION.md` names it twice — "every move and every deletion is logged with what
it was and why", and, under *files are irreplaceable*, "which is also what makes
an undo possible". The second half is the one that misleads. The undo it points
at is the dry run and the reversal a **scan directory** needs, and scan
directories are out of scope; and
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md) has since
removed the other candidate reader by making crash recovery a rule over the
arriving file row — the intended path is written before anything on disk is
touched, and `Filed` is reached only once the source is gone.

So in the first release the log has exactly one reader, and it is a person
asking **what did you do to my disk, and why**. Sizing it against a machine
reader it does not have is how it would end up recording seven rows per filed
file to support an undo nothing can perform yet.

That does not make it optional. The tool moves and deletes video files
unattended, and a tool that cannot say what it did to them is the one thing
`VISION.md`'s principle refuses.

## One entry per act, and the act is on content

The unit is the **decision as it lands on one video file**, with the filesystem
steps it took recorded as part of it rather than as entries of their own.

ADR 0026 makes the cost concrete: filing one video file is up to seven steps —
entry directory, sidecar, entry image, a relabel, the copy or rename, the
verification, the delete of the source — and
[ADR 0011](0011-a-duplicate-is-decided-after-the-download-by-quality-label.md)'s
replace is four more. A log of steps over a library's worth of filing is an
order of magnitude larger than a log of decisions and answers a question nobody
asked, because *what did you do to my disk* is not answered by the fact that a
directory was created.

Five acts, and no sixth:

| Act | Entry |
|---|---|
| **Filed** | one video file arrived in the library: where from, where to, the video, the quality |
| **Relabelled** | an already filed file renamed to carry its quality label ([ADR 0017](0017-the-filed-path-is-computed-once-and-then-recorded-rather-than-recomputed.md)) |
| **Replaced** | the arriving file took the filed file's place; names both, and the displaced file's deletion is part of this entry, not a second one |
| **Deleted** | a video file the user deleted from the review queue, or by deleting its complete library entry |
| **Tidied** | one download directory swept; names the leftovers that went |

**Replaced is one entry and not two**, even though a video file was deleted in
it, because ADR 0011 made it one decision with a confirmation that names both
files — and splitting it would produce a deletion entry whose reason is only
legible beside the entry above it.

## What is not logged, and why that is not a loophole

**The sidecar and the entry image are not logged.**
[ADR 0027](0027-the-sidecar-and-the-entry-image-are-overwritten-until-they-match-the-catalogue.md)
overwrites both unconditionally, from filing and from every repair pass that
finds a difference — so logging them would write an entry per pass per entry,
forever, and the log would become a record of the tool agreeing with itself.
They are also not content: `VISION.md`'s principle is explicit that it is
"deliberately about content" and that clearing a `.par2` out of a download
directory is not what it protects against. The two files this tool writes and
owns fall on the same side of that line, and ADR 0027 already requires the
documentation to tell people that hand edits do not survive.

**Temporary files are not logged.** ADR 0026's `.filing-<download id>.part` is
created and renamed within one act; ADR 0017 chose that name precisely so a
leftover is *attributable* by its own name, which is the job a log entry would
otherwise do.

**Failed attempts are not logged.** A move that broke off left nothing at a name
the user sees, and ADR 0014's log of the last fifty runs per routine is where a
failure is already recorded, with the Gap that follows from it. A log carrying
attempts would be a second run log with a worse retention rule.

**Reads are not logged.** The repair pass stats and reads across the library
(ADR 0027) and produces no entries unless it writes content, which it never
does.

## Leftovers: one entry per sweep

ADR 0005 deletes leftovers under a switch, and ADR 0026 bounded the sweep to the
directory SABnzbd itself named as `storage`, doing nothing at all where that was
a single file. So the volume is small and sometimes zero.

One entry per download's tidy-up, naming the files that went. Not one per file:
leftovers are not content, and eleven `.par2` volumes are one act with eleven
names. This is the one entry whose target is a directory rather than a video
file, and it is admitted because it is the only place a person will ever ask
where an `.nfo` went.

## Actor and reason on every entry

Both, on every entry, because the log's whole value is separating the two cases
the user cares about.

**Actor** is the named routine that acted, or the person. ADR 0026's four
routines and ADR 0027's repair pass all have names already, and both a review
queue Delete and a complete library-entry Delete are in a person's hand.

**Reason** is the decision behind the act, at the resolution the act was decided
at: the identification for a filing, the queue action for a delete or a replace,
ADR 0011's second quality for a relabel, ADR 0005's switch for a sweep.

What the reason does **not** carry is why the bytes are here in the first place.
That is the download's **origin**, which
[ADR 0028](0028-downloads-are-a-table-of-their-own-and-the-release-view-answers-for-the-video.md)
stores with the Download: the person who started it, or every Automation Rule
that permitted the submission
([ADR 0046](0046-an-automatic-origin-is-every-rule-that-permitted-the-download.md)).
The log entry links the Download, and the Download answers *why is this on my
disk*. Two questions,
two places, one link — copying the rule names into the log would be the same fact
stored twice with two ways to go stale.

## It is a surface, and a small one

A route of its own, beside the three
[ADR 0028](0028-downloads-are-a-table-of-their-own-and-the-release-view-answers-for-the-video.md)
now names: read-only, newest first, paginated, filtered by act, with a search
over the file name. **No actions at all** — nothing here can be undone, edited
or dismissed, and a log with a button is a log somebody edits.

The same rows, filtered to one video, appear on the library entry page. That is
where *why is this file named that*, *what happened to the 1080p* and *what did
the replace displace* are actually asked, and it costs one indexed read of a
table the route already queries.

A table that exists only for a later release was the alternative, and it is
rejected by [ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)'s
own admission rule read backwards: a column nothing displays is a column that
will be wrong for years before anyone notices. A log written by five code paths
and read by none would be exactly that, and the release that finally read it
would find it subtly wrong in ways nobody could reconstruct. Making it visible
now costs one route and makes it self-correcting.

## Nothing prunes it

No count cap, no age cap, no retention setting.

The volume follows from what is in scope. Scan directories are out —
`VISION.md`'s thousands-in-one-run case does not exist here — so the log grows
by a handful of entries a day, at most one per filed video file plus the
deletions a person makes. A library of five thousand entries implies a log of
roughly that order, at a few hundred bytes a row.

The precedent is exact and already shipped: ADR 0016's download table is never
pruned, for the same volume, because it carries state nothing can reconstruct.
Two adjacent tables of the same size with opposite retention rules would need an
argument, and there is none — the log is *more* irreplaceable than the download
row, not less, since a download can at least be inferred from what is in the
library.

The one thing a cap would definitely discard is the record of every move made
before the release that finally needs it, which is the release `VISION.md` wrote
the sentence for. Bounding it would be throwing away the stated purpose to save
a few megabytes beside video files measured in gigabytes.

## It is exported

ADR 0009's test is *cannot be fetched again*, and this passes it more plainly
than anything else in the backup: no service holds a record of what this
installation did to its own disk.

The size objection is the one worth answering, because ADR 0009 built the backup
as a readable document. At the volumes above the log is comparable to the
download table already in it, and both are dwarfed by nothing else in the file —
the backup's other contents are settings and a few thousand rows. A restored
installation that cannot say what it deleted before the restore has lost exactly
the thing the principle protects.

## Nothing reads it

No routine, no view, no recovery path and no decision consults the log. Every
consequence above rests on that: an unread table can be as long as it likes, and
a table nothing reads cannot become subtly load-bearing.

This is the same shape ADR 0016 chose for `fail_message` — stored verbatim,
shown to a person, never read for control flow — and the same shape ADR 0021
chose for the probe. Stating it as a prohibition rather than an observation is
deliberate: the first thing to reach for the log will be a feature that wants to
know whether a file was ever filed before, and the answer to that lives on the
library entry.

## Considered options

**One entry per filesystem step.** Rejected above: an order of magnitude more
rows, and the individual step does not carry the *why* that `VISION.md` asks
for. The steps are not lost — they are what an entry is composed of, and the
ones a person might ask about (the source, the target, the displaced file) are
named on the entry.

**A table with no surface, written now and read by a later release.** Rejected
under *it is a surface*: unread means unverified, and the release that read it
would inherit years of quiet errors.

**Log the sidecar and the entry image too.** Rejected: ADR 0027 rewrites both
whenever the catalogue moves, so the log would fill with the tool restating its
own output, and neither file is content.

**Cap the log at a row count, as ADR 0014 caps the run log at fifty runs.**
Rejected: that cap is sized for a routine that runs every five seconds, and this
is sized by the library. The two look alike and share nothing.

**Cap it by age — a year, say.** Rejected for the same reason ADR 0013 rejected
a time-based eviction: a duration implies an unpredictable amount, and here the
amount it discards is precisely the oldest and least reconstructible part.

**Leave it out of the backup because it is unbounded.** Rejected: it is bounded
by the library, it is the definition of what cannot be fetched again, and the
download table sets the precedent at the same size.

**Put the log on the status page.** Rejected: ADR 0018 kept history off that
page beyond ADR 0014's fifty-run cap, and *what did you do to my disk* is not
*is anything broken*. The page would grow the second axis it was cut to avoid.

**Give the log an undo button.** Rejected: `VISION.md` names the log as what
makes an undo *possible*, in the release that has one to perform. An undo here
would have to reverse a move across filesystems, recreate a deleted file it
cannot recover, and take back a fulfilment claim
([ADR 0019](0019-fulfilment-understates-the-quality-and-is-retracted-only-by-a-person.md))
— the apparatus ADR 0022 already declined for reassignment, arrived at from
another direction.

**Record the automation rule on the log entry as well as on the download.**
Rejected under *actor and reason*: one fact, two places, two ways to go stale.

## Consequences

- `CONTEXT.md` gains **Operation Log**, with *Audit log*, *History*, *Activity*
  and *Journal* under `_Avoid_` — the first three because they promise either a
  security record or the dashboard's question, the last because it suggests
  something a recovery path replays.
- The data model gains an **operation log entry**: the act, the video file's
  name and path before and after, the video and library entry where there is
  one, the download it came from, the displaced file for a replace, the leftover
  names for a sweep, the actor, the reason, and the time. Indexed by video for
  the entry page. **Exported.**
- The writers are named and there are no others: filing and its relabel
  (ADR 0026's file lane), replace and review-queue Delete (ADR 0022's queue
  actions), complete library-entry Delete, and tidy-up (ADR 0026's bulk
  routine). Anything that later wants to write a video file has to appear in
  that list, which is the check this decision leaves behind.
- **The navigation gains a fourth sibling.** Library, Review queue, Downloads,
  Operation log. The log is the only one of the four with no count in the
  header, because nothing about it is waiting for a person.
- **The library entry page gains a section**, filtered to that video's files.
- `VISION.md`'s operation log sentence is a settled requirement rather than an
  unhomed one, and its *undo* clause is explicitly about the deferred scan
  directory feature rather than about the first release. The document is amended
  to say so, since as written it reads as a first-release promise.
- ADR 0022's closing sentence — that every queue action belongs in the log and
  that what the log holds is not settled there — is answered.
- **Nothing gains a dependency.** No routine, view or recovery path reads the
  log, so it can be truncated by hand in an emergency without breaking the tool.
  That is a property worth having in the one table that never shrinks.
