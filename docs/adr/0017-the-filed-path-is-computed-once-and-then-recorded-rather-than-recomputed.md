# The filed path is computed once and then recorded, rather than recomputed

The path a video file is filed to is derived from prdb's metadata **at the
moment of filing**, and from then on the recorded path is the truth. Nothing
recomputes a name to find what was written earlier, and no correction arriving
from prdb renames anything on disk.

> **Note.** This decision calls the directory a video is filed into a *scene
> directory*, and the file beside it a *poster*. Both were renamed by
> [ADR 0027](0027-the-sidecar-and-the-entry-image-are-overwritten-until-they-match-the-catalogue.md):
> they are the **entry directory** and the **entry image**, and the image is
> written as `fanart.jpg`. Nothing decided here changes.

ADR 0005 settled that the first release files into the Jellyfin layout and that
`prdb-ordeno`'s rules apply unchanged. Those rules assume their inputs exist and
say nothing about a library that has been running for a year. This decision
covers the remainder: what the name is when an input is missing, what a
filesystem will carry, what happens when the computed path is occupied, and what
happens when prdb changes its mind about a title after the file is on disk.

## What prdb makes moot

Three of the cases this question was raised against do not exist here.
`VideoDetailDto` has a **non-nullable `title`** and a **non-nullable `site`**,
and `VideoDetailSiteDto` has a non-nullable `title` of its own. Only
`releaseDate` is nullable. `prdb-ordeno` had to answer for a missing title
because its `Recognition.Title` was nullable and its site rung produced an answer
with no video behind it; here an identification either names a video — which
carries a title and a site — or it is a Site-Only Match, which is not an
identification of a video and has nothing to file.

The cast is likewise not a path question. The layout is
`<Site>/<Site> - <yyyy-MM-dd> - <Title>/`, and no actor appears in it. An empty
cast changes nothing about where a file goes; it is a question for the sidecar
and is asked there.

## The library holds more than video files

The library carries a **sidecar** and a **poster** beside each filed video file,
not only the video file. Section 3 of `prdb-ordeno`'s layout document measured
what a library without them looks like: Jellyfin takes the file name verbatim as
the item's title, with no premiere date and no production year, because the date
in the name is not read as a date. For this material there is no metadata
provider Jellyfin could ask instead, so a library of bare video files is a wall
of release-shaped names and empty tiles — not what `VISION.md` means by "a
sorted library here should be a Jellyfin library, directly".

Only *that* the two are written is settled here, because the rest of this
decision depends on a sidecar existing. What goes in them — which elements, how
an actor is shaped, what an empty cast produces, when they are rewritten and in
which of ADR 0014's lanes — is its own question.

That the sidecar exists is what makes the rest of this affordable. Where the
name and the sidecar disagree, **the sidecar wins**, measured. So the name on
disk is for humans and for Jellyfin's version grouping, and nothing else — which
means an escaped character, a truncated title or a stale name costs tidiness
rather than correctness.

## The name

`prdb-ordeno`'s `LibraryNames` and `JellyfinPaths` are adopted unchanged, because
they were measured rather than reasoned — against a real Jellyfin instance and
against an SMB 3.1.1 share on a NAS, with a local ext4 filesystem as the control.

**A missing release date drops its segment, separator and all**, leaving
`<Site>/<Site> - <Title>/`. Nothing takes its place. A placeholder — `0000-00-00`,
`unknown`, a dash — puts data-shaped non-data on disk and buys no path stability,
since the path changes anyway once the date arrives. The version grouping
survives the drop, because it is a relation between a directory name and the file
names inside it and both lose the same segment.

**Escaping defends against the storage, not against Jellyfin.** Jellyfin served
every character class it was given, emoji included. The SMB share is the problem:
it accepts `" * : < > ? |` and does not store them as written, mapping them to
private use codepoints another client reads back as something else, and the same
share mounted without `mapposix` rejects them outright. So the Windows-reserved
set `< > : " / \ | ? *` and every control character become a **space** — not a
deletion, so that `A/B` stays two words — runs of whitespace collapse to one, and
a leading dot or a trailing dot or space is trimmed.

**Length is budgeted in bytes, not characters**: 255 per path component, measured
identically on ext4 and on the share, where 85 CJK characters fit and 86 did not.
A cut falls between runes, never inside one, and what the cut exposes is trimmed
of a trailing space, period, hyphen or underscore. The scene directory keeps
**15 bytes** free for the longest name derived from it — ` - [2160p]` plus a
five-byte extension — and that is a constant rather than the length of the
extension actually being filed, or the same video arriving as `.mkv` and as
`.mpeg` would produce two directories. The extension is taken from the file,
lower-cased, and a file arriving without one is given none.

The filed path is **not reversible to the source**, and does not need to be. The
release name does not survive filing, and nothing ever reads a name back: ADR
0011 already requires a record of which video was filed where.

## Collision

A duplicate never reaches a path computation — it is refused before filing (ADR
0011), as are identical bytes. What can still collide is a computed path occupied
by something this tool has no record of writing.

- A directory that **exists and is empty is free**. A filing that stopped half
  way, or a directory somebody made, is not another video's.
- Occupied by something: the same name plus prdb's video id, `… [<uuid>]`. The
  full id rather than a prefix — a collision needs the same site, the same date
  and the same title, which is rare enough that the ugliness is rarely on screen,
  and when it is there, an identifier that can be looked up is worth more than a
  shorter name.
- The distinguished path occupied too, **or a directory whose state could not be
  read**: nothing is filed. Sidestepping is right for a collision and wrong for
  everything else — a permissions or mount problem must not quietly produce a
  second library beside the first.

## A second quality

The first copy of a video is filed unlabelled, because at that point there is
only one of it. When a second quality arrives, the **file already filed is
renamed to carry its own label** and the newcomer is written beside it, so the
version list reads `[2160p], [1080p]` rather than one full file name beside one
label. The order is fixed — relabel first, then move the newcomer in — so that an
interruption leaves one correctly labelled file, which is a valid entry, rather
than two files of which only one is labelled.

This writes against something the user considers filed, which `CONTEXT.md`
reserved to Replacing. The reservation holds, but it is about **content**: a
relabel reads, copies and deletes no bytes, it is a rename inside one directory
and therefore on one filesystem by construction, atomic and unable to
half-happen. `CONTEXT.md` is corrected to say content, because as written it
reads as a promise that nothing filed is ever touched.

**Where the recorded path no longer resolves**, the scene directory decides:

- The directory is there and the file inside it is gone — the user tidied up.
  The newcomer is filed, the record is corrected, and because it is the only copy
  again it is filed **unlabelled**; there is nothing to relabel.
- The **directory itself is gone** — nothing is filed. It becomes a review queue
  entry saying that this video was held and is not there any more, with the new
  copy offered. A deliberately deleted entry and a mount that silently did not
  come up look identical from one `stat`, and ADR 0009 already chose the careful
  side of that same confusion.

## A correction from prdb renames nothing

When prdb corrects a title, a date or a site's name, the **sidecar is rewritten
and nothing on disk is renamed**.

This is where this tool differs from `prdb-ordeno` in a way that changes the
answer. There a refresh is a run of its own over what the tool filed; here ADR
0013's repair pass re-reads pinned videos **continuously and unattended**,
precisely in order to learn about corrections prdb announces nowhere. Renaming on
that trigger means a library that rebuilds itself under a running media server on
a schedule nobody started. Jellyfin's item is identified by the **video file's
path** — measured — so every such rename is a vanished item and a new one rather
than a renamed one; what that costs in watch state, favourites and playlist
entries was not measured, which is itself the argument for not doing it
unattended.

Nothing is lost by not renaming, because the sidecar wins on display: the library
shows the corrected title as soon as Jellyfin next scans, and a sidecar edit
landing months after Jellyfin last saved the item is well outside the
one-minute tolerance a scan applies. What is left is untidiness in a file
manager.

Two things follow and both are load-bearing. A library may hold a directory whose
name and whose sidecar disagree, and **nothing may parse a filed name** — which
section 3 asks for anyway. And the recorded path has to be the truth, or the
second quality of a corrected video would compute a fresh directory and land
beside the first, splitting one entry in two because of a correction the user
never saw.

Re-filing an entry to pick the correction up on disk is **not in the first
release**. It would need a preview, a confirmation and a way back, and for anyone
to press it the entry would have to advertise that its name is stale — which is
the per-video freshness badge ADR 0013 declined, wearing a different hat.

## The temporary name during a replace

ADR 0011 fixes the order of a replace: the arriving file is put beside the filed
one under a temporary name and verified, the filed file is deleted, the newcomer
takes the final name, the source is removed. That temporary name sits inside the
scene directory, where Jellyfin's grouping rule is watching: a file joins the
entry as a version when its name begins with the directory name and the remainder
starts with `-`, `_`, `.` or a bracketed label.

So the temporary name **begins with a dot** and carries a suffix that is not a
video container — `.filing-<download id>.part`. The dot hides it from Jellyfin's
scanner and from this tool's own walk, the name does not begin with the directory
name so the grouping rule cannot reach it even mid-scan, and naming the download
makes the leftover of an interrupted replace attributable rather than anonymous.

## Considered options

**Write no sidecar and no poster.** The smallest footprint in a directory the
media server owns. Rejected because Jellyfin would then show every entry under
its raw file name with no date and no artwork, and because it takes the escaping
and truncation this decision requires and turns them from a cosmetic cost into a
wrong title on screen.

**Keep a placeholder where the release date is missing.** Rejected: one name
shape bought with a false-looking value, and no stability, since the name changes
when the date arrives either way. The real cost of dropping the segment is that a
site's videos no longer sort chronologically in a file manager — and those
entries have no date to sort by in the first place.

**Break a collision with a short prefix of the video id.** Verified rather than
assumed — the distinguished path is checked before it is used — so a prefix would
be safe. Rejected because the collision is rare enough that name length is not
what is being optimised, and a full id is something a person can search prdb for.

**Label every file from the moment it is filed.** Never renames anything.
Rejected: it writes a resolution into every name in a library where most videos
are only ever held once, and it does not remove the mixed case anyway, since a
file filed by an earlier version or by hand is unlabelled all the same.

**Leave the first file unlabelled when a second quality arrives.** The version
list then shows one source as its entire file name, sorted first whatever it
holds, and saying nothing about its quality — on exactly the screen that exists
for choosing between two versions, in the library that has been running longest.

**Rename on a correction.** Keeps the disk and the display in agreement.
Rejected: it buys tidiness in a file manager with an unattended mass rename under
a running media server, performed by a background routine with no user behind it.

**File fresh whenever the recorded path does not resolve.** One rule instead of
two. Rejected because it answers a deliberately deleted entry and a failed mount
identically, and in the second case writes a new library into an empty
directory unattended.

## Consequences

- `CONTEXT.md` gains **Sidecar** and narrows **Replacing** to writing against
  filed *content*, so that the relabel rename is not read as a violation of it.
  `VISION.md` gains a sentence, because "only video files move" reads as "the
  library holds only video files", and it now does not.
- The library can hold four name shapes: with and without a date, labelled and
  unlabelled. Nothing may assume any of them, and nothing may parse them.
- The record of what was filed where becomes authoritative rather than
  convenient. It has to carry the scene directory as well as the file, since the
  second quality, the relabel and the replace all write into that directory
  without recomputing it.
- Every filing does two `stat`s it would otherwise not do: the computed
  directory, for the collision, and the recorded path, for the second quality.
- The review queue gains a kind of entry ADR 0011 does not describe — a video
  that was held and whose directory is gone. It carries the same shape as the
  others: a file that was not moved, and a decision that is the user's.
- A poster and a sidecar are written into a directory the media server reads, so
  filing is no longer only a move. What they contain, and what happens when one
  is already there, is not settled here.
- The first release ships a library that can drift out of date on disk, and the
  documentation has to say so rather than let someone discover it — the displayed
  title is right, the directory name is the one it had when the file arrived.
