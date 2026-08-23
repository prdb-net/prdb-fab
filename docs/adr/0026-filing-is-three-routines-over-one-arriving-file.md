# Filing is three routines over one arriving file, and none of them keeps a position

Between a download reaching **Collected** and a video file sitting in the
library stand three kinds of work with three different costs: reading the
filesystem in milliseconds, asking prdb under the governor, and moving bytes for
minutes or hours. They are three routines in three lanes over **one row per
arriving video file**, and that row is the review queue entry before it has a
reason.

> **Note.** This decision says *scene directory* and *poster*; they are the
> **entry directory** and the **entry image** since
> [ADR 0027](0027-the-sidecar-and-the-entry-image-are-overwritten-until-they-match-the-catalogue.md),
> which also settles what those two files carry and who rewrites them.

None of the three remembers a position. Each one's work set is a query over a
state, which is the shape [ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md)
already chose for the outstanding set — and it is what answers this question's
sharpest requirement, that a download of six video files must not restart all
six because the container went down on the fifth.

## One row, and it is the queue entry before it has a reason

[ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)
puts runtime, width, height, codec and the `osHash` "on the video file row".
[ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
gives a review queue entry that same shape plus a reason, the download and the
release. Both describe the same file — once before anything was decided about
it, once after the decision was *not to move it*. That is one table, not two,
and the row is called an **Arriving File**: a promotion of the phrase
`CONTEXT.md` already uses under **Duplicate** rather than a new coinage.

Two things follow that would otherwise have needed rules of their own.

**An open entry is a row with a reason that has not been closed.** So a file
still waiting to be asked about is not counted by the review queue count, which
ADR 0022 puts in the header of every page — not because a case was excluded, but
because it never had a reason to be counted by. ADR 0022 asked for exactly this
and left the mechanism open.

**Nothing is copied between tables when a file stops.** ADR 0022's *the reason is
the first that applies* becomes a single field written once, by whichever routine
reached that reason, rather than a rule some later step has to re-derive.

A row is created per video file when a download is collected. Collecting a
download that already has rows for some of its files skips those, so the
position inside a six-file download is a query too, matched on the path the file
was found at.

## Three routines, three lanes, four with the tidy-up

| Routine | Lane | Work set |
|---|---|---|
| Collect and probe | bulk | downloads in **Completed** |
| Identify arriving files | sync | arriving files `AwaitingIdentification` |
| File | **file** | arriving files `AwaitingFiling` |
| Tidy up a download directory | bulk | downloads in **Collected** with no tidied stamp |

One routine walking a file end to end was the alternative, and it fails twice.
It makes the governor a wait *inside* a loop, so a routine holds a lane in order
to do nothing; and it throws away the batch, since `POST /videos/identify` takes
up to 200 files in one request that counts once against the rate limit. A
release with six video files would spend six requests where one would do.

Identifying arriving files stays a **separate routine** from the release
identification [ADR 0023](0023-nothing-local-identifies-anything-and-a-pre-name-is-only-a-reason-to-ask.md)
put in the sync lane. The payloads differ — a hash and a size against a bare
name — and so does the precedence: ADR 0014's scarcity order already puts
`POST /videos/identify` for a file that has arrived ahead of everything else,
and a routine that mixed the two could not honour that.

## Why filing gets a lane of its own

[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md) argued three
lanes against *minutes*: a five-second SABnzbd obligation must not wait behind a
repair pass. A cross-filesystem copy of a 40 GB release on the hardware this
tool is deployed on is **hours**, which is a different argument.

The bulk lane holds collecting itself, ADR 0013's repair, ADR 0023's screening,
ADR 0025's backwards search and the tidy-up. Filing there would stop downloads
being collected for as long as one large move ran, and would stall the screening
and search that feed the whole discovery loop — so the lane that exists to keep
slow work off the fast path would be blocked by the slowest work in the tool.

The **file** lane is serial, and that is a requirement rather than a convention:
[ADR 0011](0011-a-duplicate-is-decided-after-the-download-by-quality-label.md)
relies on the first video file of a download reaching the library before the
second is measured against it, which is how two copies in one job resolve
themselves without ever being compared to each other.

## The ordered test is also a cost order

ADR 0022 fixed the order of reasons. It is not evenly distributed, and the
uneven part is what places the work:

1. `IdenticalFile` and 2. `UnreadableQuality` are **local, and come before the
identification** — decided by collect-and-probe, in the pass that already has
the file open.
3. `Unidentified` is the identify routine's, and nothing else's.
4. `Duplicate` and 5. `EntryMissing` need the video *and* fresh filesystem
reads — ADR 0011 requires a `stat` of the filed file, ADR 0017 two more — so
they are tested at the **head of the file lane**, immediately before the move,
rather than minutes earlier from the sync lane where they could go stale.

So a file whose quality cannot be read, or whose bytes the library already
holds, **never costs a prdb request**. ADR 0022's ordering was argued from what
the user needs to be told; it turns out to be the cheap order as well, and this
decision leans on that rather than re-deriving it.

The states are what is left over: `AwaitingIdentification`, `AwaitingFiling`,
`Filing`, `Filed`, and a reason, which is terminal until a person acts.

## A deferred identification is not a state

When the governor defers, the row **stays in `AwaitingIdentification`** and the
routine moves on. There is nothing else to record.

It is not a queue entry, for the reason ADR 0022 gave: an entry states a reason
the file was not moved, and *we have not asked yet* is not one of them. It is
not a **Gap**, because nothing is broken, and not a **Brake**, because no
configuration is holding it back. Where deferral is not transient — a plan too
small to carry the schedule — ADR 0014 already raises the Gap, and adding a
second one here would report one condition twice.

## What goes out, and what comes back

`IdentifyVideoFileDto` accepts five fields and no others, which settles what
this call carries more firmly than an argument could:

- `ref` — the arriving file's id.
- `filename` — the name the file **arrived** under. Not the path, and not the
  release name: for a Usenet download the two are often the same string, and
  that is a coincidence rather than a rule.
- `filesize` and `osHash` from the probe.
- `pHash` null, deferred by `VISION.md`.

**No probe field is accepted at all** — no runtime, no dimensions, no codec.
That is worth stating, because ADR 0021 reads as though the probe feeds the
identification, and it does not: it feeds the review queue and
`POST /videos/filehash-submissions`, which is a different endpoint with a
different shape.

`includeVideoDetails` is **true**. The alternative is a `POST /videos/batch` for
the same videos immediately afterwards, charging the governor twice for a
document prdb already had in hand — and ADR 0013's catalogue and ADR 0017's path
computation both need it. The catalogue row is written and pinned **before** the
arriving file moves to `AwaitingFiling`, so the file lane never waits on a
catalogue read.

## The order of writes, and what a crash leaves

The scene directory is created, the sidecar and the poster are written, an
already filed file is relabelled where ADR 0017 requires it, and the **video
file arrives last**. Jellyfin identifies an item by the video file's path —
ADR 0017 measured that — so putting the video file last means every state
Jellyfin can observe is a complete one.

Within one filesystem the move is a rename and cannot half-happen. Across one it
is a copy to ADR 0017's hidden temporary name, `.filing-<download id>.part`,
then a verification, then the rename — the name that ADR chose for the replace,
now used for every cross-filesystem move, since the reasons it was chosen for
(invisible to Jellyfin's scanner, unreachable by its grouping rule, attributable
when it is left behind) apply identically.

**The copy is verified by size and `osHash`, computed fresh on both sides after
the copy is closed**, with a byte comparison below the minimum size the hash is
defined for. `prdb-ordeno` measured this against real files and its reasoning
transfers whole, including the part that matters most here: the `osHash` the
probe read is a reading of the **source, from before the copy**, and verifying a
write against it verifies nothing that happened during the write.

The dangerous gap is between the final rename and the record. A file would then
sit in the library with nothing pointing at it, and ADR 0017's collision rule —
seeing an occupied path it has no record of writing — would sidestep to
`… [<uuid>]` and treat this tool's own work as a stranger's.

So **the intended path is written into the row when it enters `Filing`**, before
anything on disk is touched, and recovery is a rule rather than a guess: if the
intended path holds our bytes, by size and `osHash`, the source is deleted and
the row becomes `Filed`; if it holds nothing, the move starts over. And the
transition to `Filed` happens **only after the source is gone**, so a crash
between the rename and the delete leaves the row in `Filing` and recoverable,
rather than leaving a decided file lying in a download directory that nothing
will ever clear.

## What a failure is here, now that the release is gone

ADR 0016 split failure into the release's fault and the installation's. By this
point the release is consumed and the retry budget has nothing to spend, so the
split is **renamed rather than reused**, and it still has exactly two sides:

- **The file's own** — an unreadable quality, no named video, a duplicate, a
  missing scene directory. It becomes a review queue entry: a question for a
  person, never retried by the tool.
- **The installation's** — a target that cannot be written, a full disk, a mount
  that went away, a copy that broke off. Nothing changes state, and it is a
  **Gap** on ADR 0014's unchanged three-consecutive-failures threshold. It is
  retried forever, because there is no version of this the tool should give up
  on.

There is no third kind, and in particular nothing here can put an arriving file
into a state it cannot leave.

A file that **has vanished between collecting and the probe** is not either of
those: its row is dropped silently. ADR 0022's rule that an entry outlives its
file is about an entry a person could already see, and an entry about a file
nobody was ever asked about is noise.

## The tidy-up walks download rows, never the filesystem

ADR 0005 deletes leftovers once no video file in a directory is still undecided,
and ADR 0022 made that a routine. Its work set is **download rows**, and the
directory it may touch is only ever the one SABnzbd itself named as `storage`.

That is not a refinement. ADR 0016 records that for the very common single-file
release `storage` is the **video file**, not a directory — so a routine that
took "the directory" to mean the file's parent would, on an installation where
SABnzbd drops finished single files into the top of its complete folder, sweep
that entire folder. Deriving the directory and then defending it with heuristics
is the shape `VISION.md` refuses when it refuses user-written delete patterns.
So where `storage` was a file, there is no directory of ours, nothing is
deleted, and the download is done. The price is an `.nfo` left lying beside a
single-file release, which is untidy and not a loss.

Completion is a **nullable tidied-at stamp on the download row**, not a fifth
download state: ADR 0016's four states describe following a download, and this
describes a directory. A directory that no longer exists is nothing to do and is
stamped, which is also what a restored installation's download rows meet.

## Considered options

**One routine per file, end to end.** Rejected above: it turns the governor into
a wait inside a loop and spends one request per file where the endpoint takes
two hundred.

**Filing in the bulk lane, accepting the delay.** Rejected: the lane also holds
collecting, so one 40 GB move across a share would stop the tool noticing that
any other download had finished, for hours, and stall the screening and search
that feed the loop. The delay is not the cost — the head-of-line blocking is.

**A separate upstream table for files in flight, feeding a queue entry when one
stops.** The tidier read of ADR 0021 and ADR 0022 as written. Rejected: it
copies six values and a provenance between two tables at the moment a file
stops, and then needs a rule about which of the two the review queue count reads.
One row with a nullable reason answers both by construction.

**`includeVideoDetails: false`, resolving through `POST /videos/batch`.**
Rejected: it charges the governor a second request for a document prdb had
already assembled, on the one path ADR 0014 declares highest priority precisely
because a file is waiting on it.

**A fifth download state for "tidied".** Rejected: it would make ADR 0016's
state machine describe two different things, the download and its directory,
and **Collected** would stop being terminal for a reason that has nothing to do
with the download.

**Deriving the download's directory from the video file's parent.** Rejected
under *the tidy-up walks download rows*: on a plausible SABnzbd configuration it
deletes the user's complete folder, and the defence against that is a heuristic
guarding a deletion rather than a deletion that cannot reach.

**Writing the video file first and the sidecar and poster after.** The natural
order, and wrong: Jellyfin can scan between the two and take the item in under
its raw file name with no date and no artwork, which is exactly the state
ADR 0017 wrote the sidecar to prevent — and a scan that saved the item then
holds it until something changes.

**Verifying the copy against the `osHash` the probe already read.** Free, and it
verifies the source against itself. Rejected on `prdb-ordeno`'s reasoning
unchanged.

**A surface showing how many files are waiting to be identified or filed.**
Rejected: it is a progress display for work that always finishes, and ADR 0018
already refused to be a second downloads view for the same reason. What is
genuinely waiting on a person is the review queue, which is counted on every
page already.

## Consequences

- `CONTEXT.md` gains **Arriving File** and **Lane** — the latter has been
  load-bearing since ADR 0014 without ever being a term, and this decision adds
  the fourth one.
- **ADR 0014 is amended**: a fourth lane, **file**, and two routines beyond the
  ones ADR 0016 and ADR 0022 already added — identifying arriving files (sync)
  and filing (file lane). Their cadences are not fixed here; they belong with
  the pacing question ADR 0023 and ADR 0025 also left open, because what makes
  each of them due is the arrival of rows rather than a clock.
- **ADR 0016 is amended** twice: its open question — which routine files the
  collected files, and in which lane — is answered, and the download row gains
  a nullable tidied-at stamp beside the four states, which stay as they are.
- **ADR 0022 is amended**: an entry is not created "after the probe and the
  identification" in every case. Two of its five reasons are set during
  collecting, before any prdb request exists, which is what makes the reason
  order a cost order.
- The data model gains the **arriving file** table — ADR 0021's six values, the
  download and release, the video or site once known, the candidates, the
  reason, whether the file is still on disk, the state, and the intended path
  while it is `Filing`. It is not exported, describing files in a download
  directory, which is what ADR 0022 already said of the queue entry it is.
- Every filed video file leaves the library's own record as the only thing that
  knows where it went, which ADR 0017 already required; nothing in this chain
  recomputes a path to find earlier work, including the recovery rule, which
  reads the intended path off the row.
- A cross-filesystem move reads a few hundred kilobytes more than the copy
  itself, whatever the file's size, and a flipped bit in the middle of a copied
  file is not detected. Stated here as a known limit rather than an implied
  promise.
