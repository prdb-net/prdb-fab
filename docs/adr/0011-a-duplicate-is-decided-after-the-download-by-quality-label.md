# A duplicate is decided after the download, by quality label

A duplicate is an arriving video file whose video the library already holds
under the same quality label. It is never a release, and it cannot be decided
before a download: quality is read from the file with `ffprobe` (ADR 0005), and
before a download there is no file to read.

`VISION.md` describes duplicate detection as one check performed twice at
different strengths. Two decisions since have taken the earlier half away.
ADR 0007 gave automation its own rule — it refuses a video the library holds in
*any* quality, because the incoming quality is unknowable — and ADR 0008 made
size a stand-in for quality only in the ordering of releases. What is left
before a download is not a weaker duplicate check but a different thing: a
statement of what the library holds, shown in the release view and repeated in
the confirmation when someone fetches by hand. It does not block, because a
better encode of a video already owned is exactly what this looks like, and
nothing but the person can tell the two apart.

## What is compared

**The quality label, never the dimensions and never the size.** The label comes
from a fixed ladder — `2160p`, `1440p`, `1080p`, `720p`, `576p`, `480p`,
`360p`, `240p` — with each threshold halfway to the next rung down, and the
width counted alongside the height. `prdb-ordeno` derived that ladder against
real files and its reasoning holds here unchanged: a 3840×1600 scope encode is
a 4K release with the letterboxing cut out of the file, and calling it `1600p`
or `1080p` would make it a second quality of itself the next time the same
release turned up in its full frame. Comparing exact dimensions would file
1920×1080 and 1918×1080 both, and then want to give them one name.

The comparison runs against every file of the library entry, not against one of
them, so a third copy arriving beside a 2160p and a 1080p is measured against
both.

Four outcomes, and they are distinct:

- **The same video at a quality the library does not hold** — filed, as a
  second quality of the entry.
- **The same video at a label the library already holds** — a duplicate.
  Nothing is moved.
- **The same bytes**, the `osHash` matching a file already filed — its own
  outcome, said in its own sentence. A retry, or the same package fetched from
  a second indexer. There is nothing to choose between two identical files, so
  it is not offered as a choice.
- **A quality that could not be read** — not filed, whatever the library holds.

Size does not enter the comparison, however far apart two files of one label
sit. Before a download, size stands in for quality because nothing better
exists (ADR 0008); after one, the quality is measured, and letting a weaker
signal back in at that point undoes the measurement `ffprobe` is in the image
for. A user who wants the 4 GB encode over the 900 MB one is shown both figures
side by side and picks.

## What happens to it

Nothing is moved, and nothing is deleted. The file stays where SABnzbd left it
and becomes a review queue entry, which widens what that queue holds: it is now
every video file the tool declined to move, whether because it could not be
identified or because it was identified perfectly and is redundant. Both are
the same sentence to the user — there is a video file here that was not touched,
and it is yours to decide — and someone whose download directory will not empty
has one place to find out why.

The entry carries four things: the video it was identified as; the arriving
file's quality and size against the filed file's quality and size, side by side;
the path the filed file sits at; and the release and indexer the arriving file
came from.

Three actions are offered on it, per file, with multiple selection so twenty of
them are bearable:

- **Delete** the arriving file.
- **Replace** — the arriving file takes the filed file's place and the displaced
  file is deleted.
- **Leave it**, which drops the entry from the queue and leaves the file on
  disk.

There is no setting in either direction. A "delete duplicates automatically"
switch is ruled out by the principle ADR 0005 narrowed rather than weakened:
video files are never deleted unasked, duplicates named explicitly. A "keep both
anyway" switch has nowhere to write to, because the quality label is the only
thing distinguishing two files of one video in the layout and two `[1080p]`
cannot sit side by side.

Replacing is the one operation in the first release that writes against content
the user already considers filed, so its order is fixed: the arriving file is
put beside the filed one under a temporary name and verified — a copy-verify
where the download directory is on another filesystem — then the filed file is
deleted, then the newcomer takes the final name, then the source is removed.
Broken off at any point, the library holds the old file under its correct name:
never none, and never two under one. The confirmation names both files, both
sizes, and says that the existing one goes, which is what makes the deletion
asked for rather than configured.

## Considered options

**Treat two files of one label but plainly different sizes as two keepers.**
Rejected: it is a second uncalibrated number beside ADR 0008's 5 % tolerance,
it contradicts reading quality from the file, and the layout has no name for the
second file. The report shows both sizes instead, which answers the case without
a threshold.

**File a file whose quality could not be read when the library holds nothing of
that video.** There is no duplicate question to answer in that case, and the
first copy of a video is filed unlabelled anyway, so the label is not needed
either. Rejected because it costs the invariant that every filed file has a
known quality, and buys a file that `ffprobe` could not open — which is a strong
hint it will not play. Nothing downstream should have to carry an unknown
quality for the sake of filing one broken file.

**Compare against files still waiting in the review queue, not only against the
library.** Rejected: a waiting file is not something the user holds — it is in
the download directory precisely because it is still a question, and calling one
of two waiting files a duplicate of the other picks a winner nobody chose. The
case still resolves, because filing is sequential: the first file of a job
becomes the library, and the second is measured against it.

**Return the displaced file to the download directory instead of deleting it on
a replace.** The gentler shape, and it keeps the no-deletion rule intact without
an exception. Rejected because it puts a file the user has considered sorted for
months back into a directory built to empty itself, and leaves open which
directory that would even be. An explicit confirmation naming the file is the
honest version of the same care.

**Keep "duplicate" as one word spanning both sides of the download.** Rejected:
before a download there is no quality to compare, so the word would mean
"already held in some quality" on one side and "already held at this label" on
the other. A term that means two things is two terms.

**Block a manual download of a video the library already holds.** Rejected for
the reason the check exists at all — a better encode and a forgetful click look
identical from here, and only the person in front of it can tell them apart. The
confirmation says what is held and at what quality, and then gets out of the
way.

## Consequences

- `CONTEXT.md` changes in three places: **Duplicate** narrows to a file,
  **Review Queue** widens to everything left for the user to decide, and
  **Quality** gains its label and the rule that only the label is compared.
  **Replacing** is added.
- Container and codec are not quality. An `.mp4` and an `.mkv` of one video at
  `1080p` are duplicates of each other, and a replace across the two changes the
  filed file's extension.
- Before writing a duplicate outcome the tool checks that the filed file is
  still at its path — a `stat`, not a hash. It is the only decision that holds
  an arriving file back on the strength of a record, and a user who deleted the
  old file by hand should get the new one filed rather than refused. A whole
  library gone missing does not surface here as a thousand replacements, because
  filing has to write under a root that is then absent.
- ADR 0009's rule that an unverified entry counts as held is unaffected: it
  answers whether the library is empty, not whether one file is where it was
  left.
- A release whose download ended as a duplicate is consumed for that video, so
  the ranking never offers it again. The retry budget is neither charged nor
  consulted: the video is held, nothing is waiting to be fetched in its place,
  and deleting the redundant file leaves it held. Fulfilment is likewise
  untouched — it was reported when the video was first held, and the quality in
  that report has not changed.
- Waiting duplicates keep their job directory open. ADR 0005 removes leftovers
  only once no video file in that directory is still undecided, and a duplicate
  is undecided until the user acts. Nothing expires on its own; an expiry would
  be a deletion nobody asked for.
- The library needs a record of which video was filed where and at what quality.
  A filesystem cannot say whose a directory is, and both the duplicate check and
  the replace depend on knowing.
- What happens to the *name* of a filed file when a second quality arrives —
  `prdb-ordeno` relabels the file already filed so both end up bracketed — is
  not settled here. It belongs to the filed path.
