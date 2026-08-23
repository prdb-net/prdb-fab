# A download is followed by polling, and a failure is the release's or the installation's

A download is followed by polling SABnzbd and by nothing else. No
post-processing script is installed into anyone's SABnzbd, no clock declares a
download stuck, and the only thing this tool ever writes to SABnzbd is the
submission itself.

Everything that can go wrong divides in two, and the division is what the rest
of this decision hangs on. A **release failure** is the release's fault — it
consumes that release for that video, charges the retry budget, and the ranking
of ADR 0008 names the next one. An **installation condition** is not a failure
at all — a full disk, a paused downloader, an unreachable SABnzbd, a path
mapping that does not resolve — and it consumes nothing, charges nothing and
changes no download. It is a **Gap**, and the download waits.

## Why polling alone

SABnzbd has no webhook. The two push mechanisms that resemble one, Apprise and
notification scripts, carry a translated title and the job's display name and no
identifier at all, so neither can be attached to a download this tool submitted.
There is exactly one push path that carries the `nzo_id`, and it is a
post-processing script in SABnzbd's own Scripts folder. It is genuinely better
than what the HTTP API offers: `SAB_COMPLETE_DIR` is always a directory, and
`SAB_FILES` is an exact manifest of what the job produced, which removes every
ambiguity in *Collecting the files* below.

It is still rejected for the first release. The script cannot be the source of
truth — it never runs for a job deleted before post-processing, which is the
failure mode most likely to be met in practice — so the `storage` path has to be
walked anyway, and an optional script would be a second code path arriving at
the same answer. It also costs a setup step inside a different application, on a
host this tool may not share, immediately after ADR 0010 spent its argument on
keeping onboarding down to a prdb key and a library root. The manifest is worth
having later; it is not worth two mechanisms now.

## What a poll asks, and what an answer means

Both `mode=queue` and `mode=history` accept a comma-separated `nzo_ids` filter,
so the loop asks about the downloads that are outstanding rather than reading
lists: the queue for every outstanding id, then history for the ids the queue
did not return. History is asked with an explicit `limit`, because its default
is ten. The user's history length then does not matter, and there is no paging
in the loop.

Each id resolves to one of three outcomes, and the third is the one that is easy
to get wrong:

1. **Found**, in the queue or in history — apply what it says.
2. **The request failed** — a transport error, a timeout, a 5xx. The download's
   state is *unknown*, not absent. Nothing is applied, nothing is counted.
3. **The request succeeded and the id was in neither** — genuinely absent.

Collapsing 2 into 3 turns an unreachable SABnzbd into a pile of vanished
downloads, and, under the rule below, into a pile of consumed releases.

Absence is tolerated rather than acted on. A deleted job leaves no history row,
no status and no message, so absence is the only evidence there will ever be —
and it is indistinguishable from a restarted SABnzbd or a purged history. Three
*consecutive* genuine absences make it terminal; a failed request neither
increments nor resets that count. At the five-second cadence of ADR 0014 the
threshold is fifteen seconds, which nobody waits through.

## The four states a download carries

A download's state is the tool's own, and SABnzbd's `status` string is carried
beside it for display only. `fail_message` is stored verbatim with `stage_log`
and shown to a person, but it is never read for control flow: every one of those
strings passes through gettext and reads differently on a German SABnzbd.

- **Outstanding** — in the queue or in post-processing. Asked about every five
  seconds.
- **Completed** — SABnzbd is finished; the files have not been collected yet.
- **Collected** — the video files were found and handed to filing. Terminal.
- **Failed** — a release failure, with its cause. Terminal.

The outstanding set is not a position a routine remembers. It is a query — every
download in **Outstanding** — so a container restart mid-download resumes by
definition rather than by ADR 0014's resumable-position mechanism.

## Collecting the files

Only a download in **Completed** has files to find, and `storage` is the only
field that points at them. It is `""` for the whole post-processing window, so
it is read once the history row says `Completed` and not before. For a failed
download it is never read at all: it may point into the incomplete tree, and
nothing there is wanted.

The path is SABnzbd's own view of its filesystem, so it is resolved through the
path mapping ADR 0010 collects and verifies during onboarding, longest prefix
first and only on a separator boundary, so `/data` never matches `/database`.
Then it is `stat`ed, because SABnzbd has already run the final path through
`one_file_or_folder`: for the very common single-file release `storage` is the
**video file**, not its directory, and the descent recurses through
single-child directories. A file is the output; a directory is walked
recursively for video files, which are the only things filing moves (ADR 0005).

Collecting is a routine of its own — the **bulk** lane, every 60 seconds, over
every download in **Completed** — which adds one row to ADR 0014's table. It is
not in the live lane, because a walk over a NAS share must not sit in front of a
five-second obligation. Putting it on a schedule rather than at the end of the
poll also makes the unhappy path free: a download whose path did not resolve
simply stays in **Completed** and is tried again a minute later, so repairing
the mapping in settings collects the files without fetching anything again. No
backoff, no second mechanism, no expiry.

When the mapped path is missing or unreadable, the actual directory listing of
the nearest existing ancestor goes into the log. Nearly every report of "it
downloaded and then nothing happened" is either a broken mapping or a
single-file release, and that listing separates the two immediately.

## Six causes, one behaviour

A cause is derived from *where* the failure was observed, never from what it
said:

- **Rejected** — the response to `addfile` carried no `nzo_id`. `status` and
  `nzo_ids` disagree in both directions, so only the id counts. SABnzbd refused
  the NZB or discarded it as its own kind of duplicate.
- **Failed** — the history row says `status == "Failed"`.
- **Unusable** — a queue slot paused with the label `ENCRYPTED` or `UNWANTED`.
  Both are silent stalls by default: `pause_on_pwrar` is on and sets no
  `fail_msg`, so nothing else would ever report them.
- **Vanished** — three consecutive genuine absences.
- **Abandoned** — the user pressed *stop following*.
- **Empty** — collecting found no video file. The download arrived and produced
  nothing that can be filed, which another release may well fix.

All six behave identically: the release is consumed for that video, the retry
budget is charged, and the ranking names the next release. They differ only in
the sentence a person reads. **Vanished** says it was not found in SABnzbd after
three polls and likely deleted, and invents no cause beyond that.

The retry is automatic whether the download was permitted by an automation rule
or started by hand: the budget of three bounds it, and someone who wanted a
video still wants it after one broken release. When the budget is spent, or when
the ranking has nothing left, the wanted video reaches a visible end state —
ADR 0008 keeps those two apart — and only the user's reset brings it back.

## What is never written to SABnzbd

Only `addfile`. Not `mode=retry`, which mints a new `nzo_id` and destroys the
old history row, making every id this tool recorded permanently unresolvable.
And not `delete`, not even for a job in the tool's own category: SABnzbd's queue
belongs to the user, and a paused encrypted job that this tool has already
written off stays there until they deal with it. The download record says so.

The submission checks the category against `mode=get_cats` first. An unknown
category is silently downgraded to Default, and the files then land somewhere
the mapping does not describe — which surfaces later as a broken mapping and
sends the search in the wrong direction.

## No clock

Nothing is declared stuck by elapsed time. A download is stalled only when
SABnzbd says something named — a pause label, `paused: true` with the reported
free space near zero, three absences. A 60 GB release on a slow line,
`Propagating`, and a long block fetch are all indistinguishable from a stall by
duration alone, and a timeout would manufacture failures out of them, consume
good releases and charge a budget of three. Every outstanding download carries
*outstanding since* and its last seen SABnzbd status in the UI instead: the
person sees the stall, and the tool does not claim it.

## Consequences

- **The download record is the consumed state.** A release is consumed for a
  video exactly when a download row exists for that pair, so there is no second
  list to keep in step. Download rows are never deleted; they are what ADR 0009
  exports as the consumed releases, what the dashboard counts as downloads over
  time, and the retry budget is nothing but their number per video. ADR 0008's
  user reset is one operation: discard that video's download rows.
- **The budget is charged by every download, and consulted only while the video
  is not held.** A download that succeeded but identified as a different video
  charges it — bandwidth was spent for that video — as ticket 07 left open.
  ADR 0011's duplicate keeps its meaning under the new arithmetic: the row is
  counted, but the video is held, so nothing ever reads the count again. Where
  that ADR says the budget is not charged, read: never consulted.
- **`CONTEXT.md` changes.** **Consumed** widens to any release a download was
  submitted for, whatever became of it — the old wording turned on whether the
  video ended up held, which contradicted ADR 0011. **Collecting**, **Path
  Mapping** and **Retry Budget** are added.
- **ADR 0014's table gains a row** — collecting, bulk lane, 60 s — and its Gap
  mechanism is used unchanged for the installation conditions, except that
  SABnzbd's reachability raises its Gap on the first failed contact rather than
  after three. That it was unreachable at the last contact is a fact; that a
  download is gone is an inference, and only inferences need a threshold.
- **The data model gains a download table** carrying the video, the release,
  the `nzo_id`, the submitted name, the state, the cause, the last seen SABnzbd
  status, `fail_message` and `stage_log`, the consecutive-absence count and the
  time it went outstanding. It is exported, since it carries the consumed state.
  The submitted name is kept because it is the only fallback for matching a
  download whose id became unresolvable. (*Amended by
  [ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md), which
  adds a nullable tidied-at stamp for the directory sweep to work through. The
  four states above are untouched: they describe following the download, and the
  stamp describes its directory, which is why it is not a fifth one.*)
- **A failed download's files are left where SABnzbd left them.** The tool does
  not collect them, does not delete them, and does not ask SABnzbd to. What a
  failed job leaves in the incomplete tree is SABnzbd's own cleanup.
- **The seam to filing is Collected.** Identification, `ffprobe` and the
  duplicate check are not part of following a download; the collecting routine
  hands over the video files it found. Which routine files them, and in which
  lane, is not settled here. (*Answered by
  [ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md): three
  routines over one row per arriving video file — collect and probe in bulk,
  identification in sync, filing in a fourth lane of its own — none of which
  keeps a position, each working a query over a state exactly as the outstanding
  set above does. The probe moved to the early side of this seam with ADR 0021,
  so collecting reads the file as well as finding it.*)

## Considered options

**Install a post-processing script, optionally.** Rejected above: it cannot be
the source of truth, so it buys a second path to the same answer at the price of
a setup step in someone else's application. Ruled out of scope for the first
release rather than into the fog — the manifest is a real improvement, and it is
a whole feature, not an unfinished thought.

**One kind of failure.** Rejected: a full disk pauses the entire downloader with
no `fail_msg`, and treating that as the release's fault would consume three good
releases for every wanted video in flight, permanently, while nothing at all was
wrong with any of them. The split costs one classification step and is the only
thing standing between an operator problem and a wanted list that quietly
exhausts itself.

**Treat a vanished download as a question for the user rather than a failure.**
Rejected: the common case is a person deleting the job in SABnzbd because they
did not want that release, and fetching the next one is exactly the right
answer. It costs at most one of three attempts, and the message says plainly
that the download disappeared rather than inventing a reason.

**A wall clock for stalls.** Rejected under *No clock*.

**Let the tool tidy SABnzbd's queue** by deleting the jobs it wrote off.
Rejected: it is the one destructive act available against another application's
state, the encrypted job it would remove is exactly the one a password could
still rescue, and a tool that deletes from a queue the user also uses by hand is
the surprise `VISION.md` rules out everywhere else.

**Keep a consumed-releases list beside the downloads.** Rejected as the same
fact stored twice, with a reset that has to touch both.
