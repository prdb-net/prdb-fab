# The sidecar and the entry image are overwritten until they match the catalogue, and the grid never reads them

[ADR 0017](0017-the-filed-path-is-computed-once-and-then-recorded-rather-than-recomputed.md)
settled that a sidecar and an image are written beside every filed video file,
and stopped there on purpose: the filed path only needed a sidecar to **be**
there, because it wins over the file name, which is what makes an escaped or
stale name cosmetic. [ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md)
then fixed *when* the first write happens — entry directory, sidecar and image,
any relabel, and the video file last, so that every state Jellyfin can observe
is a complete one.

What is left is what they carry, what happens when one is already there, and who
writes them after the first time. That last part is not optional: ADR 0017 makes
the sidecar the only thing that ever carries a correction from prdb to the user,
because nothing on disk is ever renamed, and [ADR 0013](0013-the-prdb-catalogue-is-a-cache-with-pinned-rows-repaired-by-re-reading.md)'s
repair pass is what discovers such a correction, continuously and unattended.

## Why `prdb-ordeno`'s answers do not transfer

`prdb-ordeno` measured the shape of both files against a real Jellyfin, and
those **measurements** are adopted wholesale below. Its **decisions** are not,
and the reason is one difference: it files into somebody's existing collection,
and this tool does not. `CONTEXT.md` calls the library "the sorted collection
this tool writes, and the only directory it owns"; scan directories are out of
scope for the first release; and ADR 0017's collision rule steps around any
computed directory that is occupied, so an entry directory exists only because
this tool created it.

That single difference inverts two of `prdb-ordeno`'s three answers — the
marker, and never replacing an image — and leaves its third, the field list,
standing.

## What the sidecar carries

`movie.nfo` in the entry directory, root element `<movie>`, measured to win over
`<video file name>.nfo` where both exist. **Five elements and no others:**

| Element | From |
|---|---|
| `<title>` | `VideoDetailDto.title`, non-nullable |
| `<premiered>` | `releaseDate`, where there is one |
| `<studio>` | `site.title`, non-nullable |
| `<actor>` | one per entry in `actors`, `<name>` child, `<type>Actor</type>` |
| `<uniqueid type="prdb">` | `id` |

Four rules come straight off `prdb-ordeno`'s measurements and are not re-argued
here, because they were measured rather than reasoned. `<premiered>` is emitted
as a bare `yyyy-MM-dd`; every other form, including a valid ISO 8601 timestamp,
is discarded silently along with the production year. A performer must be an
`<actor>` element with a `<name>` child — text directly inside `<actor>` is
dropped, which is what a naive writer produces. `<type>` must be a value
Jellyfin knows, so it is `Actor` whatever prdb calls the role; `Performer`
yields a person of type `Unknown`. And `&`, `<` and `>` are escaped, because an
unescaped one makes the document unparseable and Jellyfin then uses **none** of
it without complaining, which looks exactly like a metadata lookup that returned
nothing.

**No plot, genre or tag**, because prdb has none and a field invented here is a
field the media server believes.

**No runtime**, and the reason has to be stated fresh, because `prdb-ordeno`'s
reason has expired. It wrote none because prdb published none; prdb now
publishes `durationMs`, a **median across the files prdb holds**, with
`durationSpreadMs` saying how far those files disagree. That is a fact about the
video, and a sidecar sits beside **one file**, whose real runtime the spread
explicitly permits to differ. [ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)
already established the general form of this: Jellyfin reads the streams out of
the file itself, so any copy in the sidecar can only duplicate what the file
says and then go stale. A consensus figure would be worse than a duplicate — it
would be a different file's answer, written next to this one.

**No `<studio>` for the network.** `site.network` exists and is nullable, and
Jellyfin turns every `<studio>` into a browsable entry. Writing both puts a site
and its parent network in one flat list the user cannot sort, which is two
levels of the catalogue collapsed into one screen.

Absent, never approximate, where prdb has nothing. No `releaseDate` means no
`<premiered>` **and no guessed `<year>`** — ADR 0017 drops the date segment from
the path for the same reason. An empty `actors` array means no `<actor>`
elements at all: not an empty one, not a placeholder. An entry whose `name` is
empty is skipped by the writer rather than left for Jellyfin to drop, so that
what was written and what is displayed agree.

**Nothing records that a person made the identification.** [ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
keeps a confirmed assignment as its own row and [ADR 0019](0019-fulfilment-understates-the-quality-and-is-retracted-only-by-a-person.md)'s
channel treats it as its own kind of knowledge, but none of that reaches the
disk. A `<tag>` would give the user a browsable category in their media server
about their own review queue work, and the sidecar describes the video, not the
provenance of the assignment. What is written is identical either way: what prdb
says about the video.

## The entry image is `fanart.jpg`, and the name is not a detail

One file, `fanart.jpg`, and no `poster.jpg`. This contradicts the word ADR 0017,
`VISION.md` and `CONTEXT.md` all used, and the measurement is why.

Section 5 of `prdb-ordeno`'s layout document measured the sixteen names against
a 600×900 poster, then re-measured against the shape prdb actually serves — the
shape of the video. The recommendation inverted. A Movies card is portrait
whatever the image is and centre-crops it, and the client derives its **request
size from the image's own aspect ratio**: a 16:9 `poster.jpg` is fetched 113
pixels tall and stretched over a card three times that, while an item with *no*
poster falls back to its backdrop, is cropped identically, and gets 300 pixels
to do it with. A landscape `poster.jpg` spends bandwidth and disk to make the
library look worse than writing nothing does.

So the word changes rather than the file. `CONTEXT.md` gains **Entry Image** —
named for the library entry it belongs to, not for a Jellyfin slot, and
listing *Poster*, *Fanart*, *Backdrop*, *Thumbnail* and *Cover* under `_Avoid_`
so that neither the slot this fills nor the slot it deliberately leaves empty
becomes the name of the thing.

**Which image**: the first in `images` carrying a non-null `url`. prdb documents
that array as ordered oldest first with the image id breaking ties, stable
across requests — a guaranteed **order**, expressly not a **ranking**. Nothing
says the oldest is the best one; it is chosen because two runs choose the same
one, and reproducibility is the property a filing decision needs. `cdnPath` is a
deprecated alias carrying the identical value and is not read. An empty array,
or one with no usable URL, writes nothing and is not a failure — absence was
measured to cost nothing.

## The bytes are copied from the cache, and the file lane asks nothing of the network

The image is **copied out of the artwork cache**, never fetched from the CDN by
filing.

For a held video the library grid needs those same bytes anyway, so a download
at filing time would be the same bytes a second time — and the bounded download,
the JPEG check and the temporary-name dance `prdb-ordeno` had to build for this
already exist on the cache's side of the line. This is ADR 0026's own principle
applied to the image: that decision writes and pins the catalogue row **before**
an arriving file reaches `AwaitingFiling`, expressly so the file lane never
waits on a read. The lane that holds hour-long moves waits on nothing remote,
and now that includes nothing on the network.

**If the cache does not have the image yet, none is written.** No wait, no
failure, no retry inside the lane — the same clean item a video with no artwork
at all produces, and the repair pass brings it later. The artwork cache
therefore inherits one requirement from this decision, and it is one the library
grid already imposed independently: a held video's image should be on disk.

**No setting.** `prdb-ordeno` put artwork behind a switch, off by default,
because spending somebody's bandwidth unasked is not something that happens by
omission. That switch guarded an extra download, and after the paragraph above
there is no extra download — only a file copy per filed video. [ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)
admits a setting where the tool cannot know the answer, and here it knows it:
`VISION.md` writes a sidecar and an image precisely because this material has no
metadata provider a media server could ask instead. A switch would be one that
sets the library against its stated purpose.

## The library is written over, because the tool owns it

**Neither file carries a marker, and both are overwritten unconditionally.**

`prdb-ordeno` marks its own sidecar with an XML comment and leaves anything
unmarked alone; it never replaces an image at all. Both rules exist to protect
work somebody else did in a directory that predates the tool. Here there is no
such directory — see *Why `prdb-ordeno`'s answers do not transfer* — and the
rule would land somewhere it does harm: a marker protects exactly the file
somebody has looked at, and ADR 0017 makes that file the **only** route a
correction has to the user. It would trade the feature for a guard against a
case the collision rule already prevents.

Applying one rule to the sidecar and a different one to the image would also be
an inconsistency in a single directory that nobody could explain from the
outside. One rule: both are written, both are replaced.

**Both are written to a dotted temporary name in the same directory, flushed,
and renamed into place.** The reason is ours rather than the server's: a
container killed halfway through a truncating write leaves a document that
parses nowhere, and Jellyfin discards an unparseable sidecar in silence and
falls back to the file name. For the image it is one step stronger — a
half-written `fanart.jpg` is not merely a bad image, it is a file at the name
the next write would otherwise use.

## The repair pass refreshes both, in its own lane

ADR 0013's repair pass re-reads pinned videos to learn about corrections prdb
announces nowhere, and diffs `images[]` against the local copy to find removed
artwork. **That pass writes the refreshed files itself**, in the bulk lane it
already runs in.

The rewrite is not an event of its own; it is what discovering a correction
*means*. Making it a separate routine would introduce a state whose only purpose
is to wait between two halves of one operation, and it would add a sixth routine
to the set whose pacing is already an open question. Not the file lane: that one
holds hour-long moves, and queueing a correction behind a 40 GB copy delays it
without making anything safer. A concurrent move into the same directory is
harmless, because both sides write the same content from the same catalogue row
and both land by atomic rename.

**What triggers a write is a difference in the file, not a difference in the
row.** `updatedAtUtc` moves for any edit at all, including ones touching no
field we write, and a rewrite producing identical bytes changes the mtime in a
directory a running media server scans. So there are two comparisons, one per
file:

- **The sidecar is rendered from the catalogue row and compared with what is on
  disk**, and written only if they differ. This replaces a list of "fields that
  count" that somebody would have to maintain as the writer changes, and it
  repairs a sidecar that something else corrupted.
- **The image is compared by identity, not by bytes** — the pass already diffs
  `images[]`, so it knows without an extra column whether the first entry with a
  `url` has become a different one. Comparing bytes would be the expensive route
  to the same statement.

**Where the recorded video file is not on disk, the pass does nothing and says
nothing.** It creates no directory, writes no sidecar beside a missing video,
and raises no review queue entry. ADR 0017's `EntryMissing` exists because an
*arriving file* has nowhere to go and **a decision is pending**; here none is —
there would only be a sidecar nobody reads. An unattended pass that creates
directories because a mount did not come up is the outcome ADR 0017 and
[ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md) have
each already decided against. The check costs nothing extra, since the sidecar
is read anyway for the comparison above.

**When prdb's last image goes away, the file on disk stays.** Deleting it would
turn an item with a picture into one without, unattended, with nothing taking
its place — a visible regression as the result of a repair. If another image
takes its place, that is a change *with* a replacement and is written by the
rule above; only when nothing is left does the old file remain. This is the one
place the tool declines to write over its own, and the reason is not ownership
but that deleting is not refreshing. ADR 0013's concern about removed artwork is
about the **cache**, where a dead URL means an empty grid — not about the
library, where the bytes are already local.

## The grid never reads the library

The artwork cache is the source for every one of ADR 0012's five artwork grids,
including the library grid. The entry image is an **output**, and nothing in the
UI reads it back.

The library is the one place ADR 0017 permits to drift: the user may rename a
directory, deleting the image is a documented way to ask for a fresh one, and
the directory may be gone entirely (`EntryMissing`). A grid sourced from there
turns each of those into a broken tile. The other four grids need the cache
regardless, since they show videos prdb knows about rather than videos held.

This does not halve the open artwork-caching question, as the map's fog patch
suspected it might — it bounds it. That question is now only about the cache.

## Considered options

**Write `poster.jpg`, because that is the word already in `VISION.md`, ADR 0017
and `CONTEXT.md`.** Rejected. It is the one reading that takes those documents
literally, and it pays for the consistency with a measurably blurrier library
grid. Renaming a term is cheaper than shipping a worse picture.

**Choose the file name from the downloaded image's shape** — `poster.jpg` when
portrait, `fanart.jpg` otherwise. Rejected for `prdb-ordeno`'s reason and one of
our own: the aspect ratio is knowable only after the bytes are in hand, and here
the bytes come from a cache whose contents filing must not depend on.

**Keep `prdb-ordeno`'s marker comment.** Rejected above. Worth restating what it
would cost: the user most likely to hand-edit a sidecar is the one who noticed
something wrong with it, and they would be the only user who never receives the
correction that fixes it.

**Write the runtime from `durationMs`.** Rejected. It is a median across other
people's files, sitting beside one file whose own runtime `durationSpreadMs`
explicitly permits to differ — and ADR 0021 established that Jellyfin reads the
real one out of the file anyway.

**Let the library grid read the entry image and cache nothing for held videos.**
Rejected. It would make the UI depend on a directory the user is invited to
modify, and it saves nothing, because four of the five grids need the cache in
any case.

**A separate routine for the rewrite, fed by a flag the repair pass sets.**
Rejected. A sixth unpaced routine, plus a state that exists only to wait between
two halves of one operation, to move work out of a lane that is already the
right one for it.

**Compare `updatedAtUtc` and rewrite when it moved.** Rejected. It rewrites for
edits to fields we do not write, and each such rewrite touches the mtime of a
file a running media server watches — the cost is paid in the media server, not
here, which is the kind of cost that goes unnoticed until somebody reports a
library rescanning for no reason.

**Delete the entry image when prdb's last one goes away.** Rejected. It is the
only case where a repair pass would make the library visibly worse, and the
information it would be propagating — prdb removed a picture — is worth less
than the picture.

## Consequences

- `CONTEXT.md` gains **Entry Image** and **Entry Directory**. The second settles
  a term the glossary never held while four ADRs used it freely: they say "scene
  directory" throughout, and the glossary's very first entry defines **Video** as
  "the single scene prdb catalogues" and lists *Scene* under `_Avoid_`. A
  `prdb-ordeno` import running against this repo's own first term, and it is the
  directory this decision writes into, so it is named here. ADR 0017 and ADR 0026
  get a note; their decisions are untouched.
- `VISION.md`'s sentence about what is written beside each video file uses the
  new term.
- **The library grid gains no dependency on the filesystem**, and the artwork
  cache gains one requirement: a held video's image should be on disk. The map's
  artwork-caching question is bounded to the cache alone.
- **The repair pass becomes a writer.** It was a reader of prdb and a writer of
  the catalogue; it now also writes into directories a media server reads. Its
  budget is unchanged — the writes are local and follow work it was already
  doing — but the failure surface is not: a permissions problem in the library
  now surfaces from a background pass rather than only from filing.
- **A second quality filed into an existing entry directory refreshes the
  sidecar and the image as a side effect**, because filing writes both
  unconditionally. The gap `prdb-ordeno` had to name explicitly — a scene filed
  before a correction keeps the old sidecar — does not exist here.
- Filing spends **no prdb requests and no bandwidth**. Both files are written
  from the catalogue row ADR 0026 pins before the file reaches `AwaitingFiling`,
  and from the artwork cache.
- A user who hand-edits a sidecar or replaces the image will have their change
  overwritten, by filing or by the next repair that finds a difference. This is
  a deliberate consequence of the tool owning the library, and the documentation
  has to say so rather than let someone discover it.
- **The wish list is amended**: *A runtime on a video* was granted in
  `Prdb.Sdk` 0.11.0 as `durationMs`/`durationSpreadMs`/`durationFileCount`, and
  ADR 0021's sentence "prdb publishes no runtime, so there is nothing to compare
  against" no longer holds — which reopens the minimum-runtime gate it refused
  on exactly that ground. A new wish is added in its place: a **preferred image**
  per video, since the documented order is expressly not a ranking and this
  decision picks the oldest only for reproducibility.
