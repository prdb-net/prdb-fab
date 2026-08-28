# Downloads are a table of their own, and the release view answers for the video

Downloads get a route of their own, beside the library and the review queue: a
table of download rows, newest first, filtered by state. The per-video
questions — how much retry budget is left, which releases are consumed, and the
reset that clears them — are answered by the **release view**, which already
exists and is already per-video.

Two surfaces rather than one, because two different questions are being asked,
and neither of them is the dashboard's. **Nothing of `VISION.md`'s dashboard is
pulled forward.**

## Why the row is a download and not a video

[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)
left this open and made it necessary in the same breath: it carries one summary
line for outstanding downloads and refuses to be a second downloads view, and it
puts `fail_message` and `stage_log` "on its row" without saying where that row
is displayed. Both statements point at a surface, and the shape of that surface
is the first thing to settle.

A view of videos with their downloads underneath is the tempting one, because
the retry budget is per video and the wanted list is what the user thinks in.
It is rejected on the same ground
[ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)
used to keep the review queue out of the library, and
[ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
used to keep the download off the queue as a grouping level: **one list whose
rows mean two things, and whose actions mean two things, is worse than two
lists.** A video row would offer no action this surface has — *stop following*
belongs to one submission, not to a video — and a download row nested under it
would be a row inside a row with a different action bar.

The download is also the thing the data already is.
[ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md)
makes the download row the consumed state, carries the `nzo_id`, the four
states, the cause, the last seen SABnzbd status, `fail_message` with
`stage_log`, the absence count and *outstanding since* on it, and never deletes
one. Every fact this ticket has to display is a column on that row. A surface
whose row is the row is one query; a surface that groups them is a query plus a
rule about what a group means when its downloads disagree.

## What the video half needs, and where it already lives

The per-video questions are real, and they have a home already.
ADR 0012's **release view** is reached from a wanted video or from a library
entry, is ordered by ADR 0008's ranking, and shows every excluded release struck
through beside its reason. A **consumed** release is exactly such a case — a
release the ranking will never offer again for that video — so it belongs in the
rows that surface already draws, with the download's state and outcome as its
reason rather than in a second list of its own.

That places three things without inventing a surface:

- **The retry budget**, which is nothing but the count of download rows for the
  video, sits at the head of the release view as *n of three spent*.
- **ADR 0008's reset** — one operation, discard that video's download rows — is
  the action beside it. This is the destination
  [ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)'s
  spent-retry-budget Brake has been routing to without one existing.
- **The two end states ADR 0008 keeps apart** — *no releases left* and *retry
  budget spent* — are visible as what they are: an empty ranking, or a full
  ranking whose head is unreachable.

So the split is not two views of the same thing. The downloads table answers
*what has this tool sent to SABnzbd and what became of it*; the release view
answers *what is the state of play for this video*. The link between them is one
click in each direction.

## What a row shows

Newest first, paginated the way the library and the review queue are, with a
filter on the state — ADR 0022's one-facet filter, and the facet here is
ADR 0016's four states.

Every row carries the submitted name, the video, the release with its indexer,
the state, the last seen SABnzbd `status` beside it as the plain string it is,
*outstanding since* or the time it reached its terminal state, and the size the
indexer reported. A **Failed** row adds the derived cause in this tool's own
words — one of ADR 0016's six — and, expanded, the verbatim `fail_message` and
`stage_log`. Those two are shown expanded rather than in a modal because they
are the text a person copies into a forum post.

ADR 0016 requires the cause to say where it was seen and never to invent one:
**Vanished** reads as *not found in SABnzbd after three polls, and likely
deleted there*, and stops.

## The origin, which nothing has stored yet

`VISION.md` requires every automatic decision to be written down with the rule
that caused it, and
[ADR 0007](0007-automation-is-a-set-of-permissions-over-the-wanted-list.md)
requires *why is this on my disk* to always have an answer. No ADR has stored
that answer. ADR 0016's download table carries the release and the video but
nothing about what permitted the submission.

So the download gains an **origin**: the person who started it by hand, or every
automation rule that permitted it. The original singular rule was amended by
[ADR 0046](0046-an-automatic-origin-is-every-rule-that-permitted-the-download.md):
rules are permissions without order, so choosing one would invent a winner.
Each automatic Origin member stores the rule reference while it exists and
**the rule's name as it read at that moment**, because
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)
permits a rule to be deleted and a download row never is. The names are
displayed; the references are links while the rules still exist. The Download
and its Origin members are exported, since ADR 0009 cannot reconstruct either.

It is deliberately **not** called a cause. ADR 0016 spent that word on the six
reasons a download failed, and two fields called *cause* on one row, one saying
why it started and one saying why it stopped, is a glossary collision inside a
single table.

## What can be done to a row

**Stop following**, and nothing else.

It is available on **Outstanding** rows only, with multiple selection, and it is
ADR 0016's **Abandoned** cause reached by hand: the release is consumed, the
retry budget is charged, and the ranking names the next release. Behind a
confirmation that names the downloads it covers, because it spends budget.

**It does not touch SABnzbd.** ADR 0016 writes only `addfile` and nothing else,
ever, and that has to be said in the button's own words rather than left for
someone to discover: the job keeps running in SABnzbd, and what it produces is
no longer collected. Wording it as *cancel* would promise the one thing this
tool has decided it will never do to another application's queue.

There is **no retry action**. ADR 0016 refuses `mode=retry` — it mints a new
`nzo_id` and destroys the history row, making every id recorded here permanently
unresolvable — and the retry against the next release is automatic anyway. A
button that re-fetched the same release would be asking for the failure again.

There is **no delete**. Download rows are the consumed state and the retry
budget, so removing one silently un-consumes a release; the one operation that
does that deliberately is the reset, and it lives on the video where its
consequence is visible.

A **Completed** row that has not been collected offers nothing either: ADR 0016
retries collecting every 60 seconds forever, and a broken path mapping is a Gap
with a route to the settings form. A button here would duplicate that route from
a place where the diagnosis is not visible.

## Where a row leads

Four links, and each of them exists because the question after *what happened to
this download* is a different question:

- **To the video** — the release view, with the budget and the reset.
- **To the review queue**, where any of the download's arriving files stopped
  with a reason. This is the link ADR 0022 asked for from the other side: it
  puts *release and indexer* on every queue row so that *what else did this
  indexer send me* can be asked, and the answer to that is this table filtered
  by indexer.
- **To the library entry**, where a file filed.
- **To the indexer's settings route**, since ADR 0020 gives every indexer one.

And one link inward, which is the other thing ADR 0022 left without a
destination: its **Brake** — automation held for a video because that video has
an open queue entry — routes to the entry. The review queue therefore has to be
addressable per entry, which is a filter on its existing table rather than an
entry page. That is the whole of what this decision adds to the queue.

## The dashboard is not pulled forward, and this is not it

`VISION.md` gives the dashboard downloads over time, what arrived recently, how
the wanted list is doing, how much of the library is identified, and what is
waiting in the review queue. **None of that is here.** Every one of those is an
aggregate over history, and this table is a list of rows with a filter.

The distinction is worth stating because the two would be easy to slide
together, and the slide has a cost: the first release would be shipping a
dashboard whose contents were never argued, under a name that avoided the
argument. What forced this surface into existence is two sentences in ADR 0018
about where a person reads `fail_message` and where they see the outstanding
list — and this answers exactly those two.

The dashboard stays out of the first release, unchanged.

## Considered options

**A video-centric view with downloads nested underneath.** Rejected above:
rows that mean two things, actions that mean two things, and a grouping rule
needed for the case where a video's downloads disagree. It also fails the case
the surface exists for — a person reading `fail_message` is looking at one
submission, not at a video.

**Put the downloads list on the status page after all.** Rejected: ADR 0018
argued the summary line precisely so that the page would not become a second
downloads view, and the argument was that a page carrying everything is a page
nobody reads. Reversing it here would make ADR 0018's central cut arbitrary.

**Pull the dashboard forward and put downloads on it.** Rejected under *the
dashboard is not pulled forward*. It is the option that looks like less work and
is more: it commits the first release to four aggregates nobody has specified.

**No surface at all — let the status page's summary line be the whole of it.**
Rejected: ADR 0018 explicitly stores `fail_message` and `stage_log` for a person
to read and then puts them nowhere, and *stop following* is an action ADR 0016
requires with no place to press it. The two decisions would be incoherent.

**A retry button that resubmits the same release.** Rejected under *what can be
done to a row*: `mode=retry` is unusable, and a fresh `addfile` of the same NZB
is a second download row for a release the ranking has consumed — the ranking
would have to be taught an exception, and the budget would be charged twice for
one release.

**Let the tool delete the SABnzbd job when following stops.** Rejected, as
ADR 0016 rejected it: it is the one destructive act available against another
application's state, and the job it would remove is often exactly the one a
password could still rescue.

**A separate list of failed downloads.** Rejected: it is the state filter with
extra navigation, and it splits the one table whose value is that a video's
whole download history reads in one place.

**Store the origin as free text only.** Rejected: it cannot be linked, and the
common case — a rule that still exists — is the one where the user wants to go
and look at it. ADR 0046 keeps both the reference and the copied name for every
permitting rule.

## Consequences

- **`CONTEXT.md` gains Origin**, defined against **Cause**, which ADR 0016
  already owns. **Download** is untouched.
- **ADR 0016's Download gains an Origin shape**: Person is recorded on the row;
  automatic permission is one exported child row per permitting rule, carrying
  a nullable live reference and an immutable copied name. The four states, the
  Cause and everything else on the Download are unchanged.
- **ADR 0018's spent-retry-budget Brake has a destination**: the release view of
  the video, where the reset is. So does ADR 0022's queue Brake: the review
  queue filtered to that entry.
- **ADR 0012's release view gains a head**: the retry budget as *n of three
  spent*, the reset beside it, and the download state on any release that has
  one. Its two entry points and one implementation are unchanged — this is one
  more line that differs between them, and it differs the same way the existing
  one does.
- **ADR 0022's review queue gains addressability per entry** and nothing else.
  No entry page, no new actions.
- **The navigation has three siblings**: Library, Review queue, Downloads. The
  review queue keeps its header count; the downloads table gets none, because a
  count of downloads is not a count of things waiting for a person, and
  ADR 0018's headline already carries the outstanding number.
- **Nothing here polls.** The table reads download rows, which ADR 0016's live
  lane keeps current at five seconds. Opening it contacts neither SABnzbd nor
  prdb, which is ADR 0018's *refreshing never causes work* applied to the
  surface it named.
- **A download whose video was later reassigned does not exist**, since
  reassigning a filed file is out of scope — so the video on a download row is
  the video it was fetched for, permanently, and the retry budget arithmetic
  ADR 0016 fixed needs no qualification here.
