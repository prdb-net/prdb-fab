# The library shows only what is held, and the release view is one table

The library view is a grid of artwork over library entries and nothing else — a
wanted video with no file does not appear in it. The release view is a table
ordered by the release ranking, reached from either a wanted video or a library
entry, differing between the two only in what it says the library already holds.

## Why the library is a grid and not a table

The tool never plays anything, which is an argument for treating the library as
an inventory: what is held, at what quality, where it sits, what is still
waiting. That reading points at a table, and a table answers those questions
better than a grid does.

It loses anyway, because Library is one of five browse surfaces. What's new,
Sites, Actors and Wanted are all lists of videos that are not held yet, where
there is nothing to tabulate and artwork is the only thing to show. Making the
fifth a table makes it the odd one out among its own siblings, and the user
pays for the inconsistency on every visit while the inventory questions have
somewhere better to be answered — the entry page, which lists every file with
its quality, size and filed path. `VISION.md` had already committed to the grid
by explaining why artwork is cached at all.

## Why a wanted video is not in it

It has no quality, no filed path and no size, so every filter the view offers —
site, actor, quality — either misses it or has to define what those mean for a
file that does not exist. It is an intention rather than a library entry, and
the wanted list is where intentions live. The cost is that "do I have this?"
and "did I want this?" are two places rather than one; the benefit is that the
library never shows a row that cannot answer the questions the library is
asked.

## Why the review queue is beside the library rather than in it

Both hold video files, and mixing them is tempting for exactly that reason: it
would put the unidentified file in the user's way instead of behind a link. But
ADR 0011 gives a queue entry three per-file actions with multiple selection, and
a library entry has a different action entirely. One list whose rows mean two
things, and whose row actions mean two things, is worse than two lists. The
queue is therefore its own surface, named from the library by a persistent count
in the header and a banner above the grid while it is not empty, so it is never
invisible.

## Why the release view is a table

ADR 0008 fixed the ordering and asked for three things to be visible: the
ranking as the default sort, size and indexer rank side by side, and every
excluded release shown with its reason. All of those fit any layout. The
tie-breaker is the 5 % size tolerance, which that ADR calls a guessed default
and expects to be corrected in the light of what the user sees here. A column of
`Δ vs #1` is skimmed; two cards are compared one pair at a time. The table also
keeps the excluded releases in the same rows as the rest, struck through beside
their reason, rather than in a tray that has to be opened to be believed.

## Considered options

**A single large recommendation with a generated explanation**, the other
releases compact beneath it. The most immediately readable of the three
prototypes: it says "largest of 4 usable, 2.6 % above the next, inside the
tolerance, so the indexer rank decided" in a sentence. Rejected as the view's
organising idea, because that sentence is prose that must be kept true as the
comparator chain changes, and a chain of five criteria produces explanations
that either omit steps or stop being a sentence. It survives as one caption
above the table, where being incomplete is honest rather than misleading.

**Ranked cards.** Rejected for the tolerance: the numbers that need comparing
end up on different cards.

**Lanes grouped by site**, mirroring the layout on disk. Attractive because the
library the user browses would then have the shape of the library on the
filesystem. Rejected because it fixes one ordering — site — for a view whose
whole job is to be filtered and searched along three others, and because a
library of any size becomes a page of horizontal scrollers, each hiding most of
its contents.

**The review queue as rows pinned above the library in one list.** The best
answer to "how does an unidentified file appear next to identified ones", and
the reason it lost is that the question contains a wrong premise: an
unidentified file is never filed, so it is never a library entry. Rejected for
the action mismatch above; the banner keeps what the pinning was for.

**A drawer instead of a route for the library entry.** Rejected because the
entry surface starts the search for a better release and carries the replace
confirmation from ADR 0011, and a drawer that hosts confirmations is a page with
worse ergonomics. A route is also linkable, which a Gap or a post-restore
message needs.

## Consequences

- The release view has two entry points and one implementation. The difference
  is one line: what the library holds of this video, which is the pre-download
  statement ADR 0011 requires and which never blocks.
- Nothing offers the release view from a card. An action that spends bandwidth
  does not belong on a grid people scroll past; it belongs on the entry page.
- A card carries artwork, title, site, and the quality labels held — up to two,
  then `+N`. Site is on the card because two videos sharing a title are common.
  Release date, actors and runtime are on the entry page and in the hover
  overlay. (*Amended by
  [ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md),
  which settles **Runtime** as the word — this bullet said "duration" while the
  next one said "runtime" — and answers which file's runtime a card of two
  shows: the one carrying the highest quality label.*)
- prdb bounds how noisy a card can get on its own: `VideoDetailDto` carries
  title, site, release date, actors, images and pre-names, and no runtime, tags
  or description. Anything else a card might show is local knowledge, not prdb's.
- The header carries a review-queue count on every page, so the count is
  something the sync has to keep current rather than something the library
  computes. (*Amended by
  [ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md):
  the demand this bullet makes is that the count never be taken from the
  filesystem, and that stands — but it is read as a `COUNT` over an indexed
  column rather than kept as a running total, because a maintained counter is
  a second place the truth lives.*)
- The data model needs the filed path, quality and size per file readable
  without touching the filesystem, since the entry page lists them.
