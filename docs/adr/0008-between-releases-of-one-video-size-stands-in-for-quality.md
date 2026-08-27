# Between releases of one video, size stands in for quality

The releases of one video are ordered by a fixed comparator chain, and the only
quality signal in it is size: under the maximum its rule allows, the largest
release wins. Nothing in the ordering reads the release name.

ADR 0007 removed quality as a condition on automation rules and left it as "a
preference between releases that already qualify" — which lands it here, in the
one place that compares releases with each other. But ADR 0005 reads quality
from the file with `ffprobe`, and before a download there is no file. What the
Newznab API guarantees on every release across every implementation is
`title`, `link`, `pubDate`, `category` and `size` (`research/newznab.md` §9);
everything richer is `extended=1` and absent or differently meant somewhere. So
the preference is either parsed out of the name or read off the size, and
refusing to believe release names is the premise of this tool.

The chain, in order:

1. **Exclusions.** A release confessing a password; a release outside the size
   limits of every rule that permits the video; an indexer no such rule allows;
   a release already consumed for this video.
2. **Confidence tier** — `{Exact, Strong}` before `{Probable}`.
3. **Size descending**, with differences below 5 % of the larger counting as
   equal.
4. **Indexer rank**, a user-sortable order over the configured indexers.
5. **Release identity** — the indexer and its own id, so the order is total.

Only releases identified as *this video* take part. A Site-Only Match has no
video and is therefore never in a video's ranking, which is what ADR 0007
already implied by drawing every rule from the wanted list.

## Considered options

**Parse the resolution out of the release name, as a preference only.** The
argument is that a wrong parse here costs bandwidth rather than a wrong file,
which is the reason names are distrusted elsewhere. Rejected because it makes
the name authoritative in the one comparison that decides what is actually
fetched, and because it would be the tool's only surviving use of a name as
evidence — a single exception is how a rule stops being a rule. Size gets most
of the same result honestly, and the file is measured properly on arrival
anyway.

**A weighted score over the same signals.** Rejected for the reason ADR 0007
rejected ordered rules: a chain answers "why this one" with a sentence, a score
answers it with arithmetic nobody can check.

**Let the user order the criteria.** Rejected — it multiplies a setting that
cannot be calibrated without data the user does not have.

**Age as a criterion**, newer first, on the theory that retention makes old
releases fail. Rejected because the age is not reliably knowable: `pubDate` is
the index-add date, and `usenetdate` is `extended` and missing from several
implementations, so the criterion would measure something different per indexer
and order wrongly. The retry absorbs the retention risk instead.

**Deliberately fetching two releases of one video** to settle a weak
identification by hash. Rejected: it doubles bandwidth against an uncertainty
that the arriving file resolves for free, and it manufactures the two-qualities
state that `CONTEXT.md` reserves for something the user wanted.

**Requiring a release to satisfy every rule that permits its video**, rather
than any one of them. Rejected as a forbid by the back door, which ADR 0007
rules out: a narrower rule would take away what another one allows.

**Freezing the list of releases at the first attempt.** Rejected because the
indexer cache fills continuously, and a retry that ignores the better release
which arrived in the meantime is the wrong answer for the sake of a simpler
one.

## Consequences

- Confidence gains an order — `{Exact, Strong}` before `{Probable}` — as an
  enumerated list of sets. This extends ADR 0006 without breaking it: there is
  still no `>=` anywhere near `IdentifyConfidence`, and `Ambiguous` appears in
  no set.
- Indexers gain a user-sortable rank, in the existing indexer list rather than
  a screen of its own; a newly added indexer goes last. Where the same package
  sits on two indexers their reported sizes rarely match to the byte, which is
  why the size comparison carries a tolerance at all — without one the rank
  would never decide anything.
- The 5 % tolerance and the retry budget are guesses, unmeasurable without real
  indexer traffic. As with ADR 0006's defaults, the protection is visibility:
  the release view shows size and indexer rank side by side, so a wrong value
  is apparent rather than silent.
- The shipped default rule has no maximum size, so under this chain it always
  fetches the largest release available. That follows from ADR 0007 making the
  maximum the "not the 4K one" control, and the rule's own UI has to say so.
- A `password` attribute that is present and not `0` excludes a release from
  automatic selection; an absent attribute means unknown and does not. The
  values are implementation-defined (`research/newznab.md` §2.3) so only a
  confession counts — and it is worth acting on because an encrypted archive is
  one of the two SABnzbd states that hang silently forever.
- The ranking is recomputed at every attempt over the releases not yet consumed,
  so releases that arrived since take part. Consumed is permanent per video and
  resettable by the user, and resetting it also resets the retry budget — that
  reset is the only way out of a video that stopped.
- Exhausting the retry budget (3 downloads per video, the first included) stops
  the video with a visible reason. Running out of unconsumed releases does not:
  the video keeps waiting, and the next release to enter the cache starts
  another attempt. They are different states and must read differently.
- A download that succeeds but whose file identifies as a *different* video
  consumes the release for the video it was fetched for, and the ranking moves
  on. It is not a failure — the file is filed under the video it actually is —
  so whether it draws on the same budget is a question for how failure is
  classified.
- The same ordering is the default sort of the release view, with the top
  release marked as the one automation would take, and excluded releases shown
  with their reason rather than hidden. A second ordering in the UI would make
  "why did it pick that one" unanswerable.
