# The wanted sweep asks with a title, from a reserved share of the budget

The sweep sends one search per wanted video per indexer, and the search term is
the video's **title** and nothing else — no pre-name, no site, no page two. It
is paid from a share of the daily query budget that the indexer walk may not
touch, because the walk runs more often and would otherwise take it all. What
comes back enters the indexer cache like any other release and is tied back to
nothing: [ADR 0023](0023-nothing-local-identifies-anything-and-a-pre-name-is-only-a-reason-to-ask.md)
settled that provenance is never evidence, and this decision only spends it as a
reason to ask prdb first.

## What goes out

`t=search` is the whole vocabulary. It is the one function all five surveyed
implementations carry, `t=movie` and `t=tvsearch` describe material this tool
never handles, and the request is otherwise the same shape the walk already
sends:

```
t=search&q={title}&cat={configured}&extended=1&limit=100&o=xml
```

`extended=1` because `newznab:attr size` is the only quality signal
[ADR 0008](0008-between-releases-of-one-video-size-stands-in-for-quality.md) has
before a download. `cat` is the per-indexer configured set, re-resolved by name
the way [ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md)
requires. **No `maxage`** — the sweep exists to reach what is old, and bounding
it by age would make it a second walk. **`offset` is never raised past 0**: a
search whose answer is on page two is a search whose query was too broad, and
paging would multiply the most expensive thing this tool does against an indexer
to buy results that fit the query less well than the ones on page one.

`q` is the video's title with everything non-alphanumeric collapsed to single
spaces. Three things are deliberately not in it.

**Not a pre-name.** A pre-name is the closest thing to what an indexer named a
release after, which makes it the obvious candidate and the wrong one. Sent
whole, it carries the resolution, the container and the release group, so it
finds the bit-identical release or nothing — and the whole point of the sweep is
the release that is *not* the one prdb happened to record. Cut down to its
distinguishing words, it would be release-name parsing, which ADR 0023 forbids
outright: the moment this tool begins to understand what a scene name says, it
is a second matcher, and there is only one. Pre-names keep the job that decision
gave them — a local cost filter over cached rows — and never leave the machine.

**Not the site.** As a second AND term it looks free and is not: prdb writes
`Brazzers Exxtra` where the scene writes `BrazzersExxtra`, and an AND term that
fails on a spelling makes the sweep return nothing at all. ADR 0023 already
priced this asymmetry — a false positive costs one two-hundredth of a prdb
request, a false negative costs a video nobody ever finds — and silence is the
expensive direction here too, doubly so because a sweep that quietly finds
nothing looks exactly like a video no indexer carries.

**Not a title too thin to search.** prdb's `title` is non-nullable, but not
every title carries a query: one word, or `Scene 3`, returns either nothing or
hundreds of rows that all enter identification and spend prdb's budget — the one
place where a broad query does cost something. A title that normalises to fewer
than two words or fewer than four characters is **not swept**, and the video
carries a **Brake** naming that reason. Not a Gap, because nothing is broken,
and not silence, because otherwise exactly those videos would go unsearched
forever with no way to notice. Nothing rescues them by appending the site, which
would be the rejected option arriving through the back door.

## What comes back

Results are upserted into the indexer cache under ADR 0015's identity, in the
same table and by the same rules as a walk's, and — as that ADR already
settled — **they do not move the watermark**, since counting old material as
progress would make the walk skip genuinely new releases.

A **new** row goes straight to `Awaiting` and skips screening. Screening exists
to decide which rows are worth a request; a sweep result already has its reason,
and running the local filter over it could only take that reason away. The row
carries a boolean saying **that** a search was the reason, never **what** was
searched for — ADR 0015 forbids recording the query, and a `videoId` written
here would be precisely the echo ADR 0023 refuses. The flag buys one thing:
ADR 0023's rule that swept rows are identified first, because someone is waiting
on those.

An **existing** row is upserted in its fields and **its identification state is
left alone**. A sweep hit is not a new fact about a name; it is the same question
asked louder. `Unknown` is never re-asked from this side, exactly as ADR 0023
requires, and `Matched`, `Ambiguous` and `SiteOnly` are equally untouched. This
is also where the sweep gets cheap: a video swept for the twentieth time
re-fetches the same releases and generates **no prdb request at all**, because
every row already holds its answer.

The flag lives only while the row is `Awaiting`, and is cleared when the row
leaves that state. It is a queue priority, not a property of the release. Kept
as a permanent column it would be a second, weaker claim standing beside the
identification state ADR 0023 made the tool's only cursor over the cache — and a
standing invitation to read provenance as evidence after all.

## The reserve

Walk and sweep draw on one number: the **daily query budget** per indexer, the
only interval-adjacent control [ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)
left the user, because Newznab quotas belong to their account and three of the
five surveyed implementations report nothing about them. ADR 0015 named the
collision — a re-walk "would displace the wanted sweep" — and did not resolve
it.

A precedence rule does not resolve it either. Whichever routine asks first wins,
and the walk asks four times as often, so on a busy day it drains the budget
before the sweep's turn comes and starves the only routine that reaches anything
older than the installation. So the sweep gets a **reserved share instead of a
priority**: half the daily budget, capped at what its cadence can actually
spend, and everything it does not use falls to the walk.

The cap makes the reserve self-limiting. ADR 0014's cadence — five videos per
run, every fifteen minutes — is 480 requests a day per indexer, so on a generous
budget the reserve is 480 and the walk keeps the rest; on a budget under 960 it
is half, and the sweep runs fewer than five videos per run. That shortfall is a
**Brake** on the search stage: the tool working exactly as configured, searching
more slowly than its schedule wants, with a route to the number that bounds it.
Not a Gap — nothing is broken, and the person who set the budget may have set it
correctly for their account.

Neither the share nor the cadence is a setting. ADR 0014's admission rule
applies unchanged: a control exists only where the answer lives outside anything
the tool can observe, and both of these follow from a number the user already
gave. The reserve also says nothing about an indexer's *own* quota — the
`newznab:apilimits` counters some implementations report are read and respected
per response, as the research already prescribes, and a `429` is ordinary
backoff.

## The ordering, and why an empty answer changes nothing

The last-searched timestamp ADR 0014 put on a wanted video is per **(video,
indexer)** pair, not per video. One timestamp per video would mean a newly added
indexer sees a wanted list that already looks freshly searched and never sweeps
it — against [ADR 0002](0002-releases-are-indexer-specific.md), which fixes a
release to an indexer and its id, so a search at one indexer says nothing about
another.

Least-recently-searched is the whole ordering, and **a fruitless sweep does not
demote a video**. Backing off after months of nothing would penalise exactly the
old, hard-to-find videos the sweep exists for, and reward the new ones the walk
would have delivered anyway. The round-trip time already grows with the length
of the list, which is the only self-limiting the ordering needs — and the
cheapening the question was really after happens at the other end, where an
unchanged cache row costs no prdb request. The sweep becomes cheap without the
ordering having to notice anything.

The swept set is every wanted video that is not currently **braked**: a retry
budget spent ([ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md))
or an open review queue entry holding the video
([ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md))
both mean nothing can be fetched, so a search spends the scarcest budget in the
system on a release nobody will take. Automation is **not** a criterion: a video
no rule covers is exactly the one whose releases a person browses and chooses
from, and `VISION.md` promises the cache answers rather than the indexer.

A video that leaves the wanted list, or is fulfilled, simply drops out of the
ordering. Nothing else is owed: ADR 0015 pins its cached rows only while it is
still wanted, so letting go hands them to eviction without anyone tidying up.

## Considered options

**Send a pre-name as the query.** The most tempting option by far — it is
literally the string an indexer named a release after. Rejected under *what goes
out*: whole, it matches only the bit-identical release; reduced to its
distinguishing words, it is the release-name parsing ADR 0023 exists to prevent.

**Add the site as a second term.** Rejected on spelling drift, which turns a
narrowing into a silence, and silence is the direction ADR 0023 priced as
expensive.

**Page the results.** Rejected under *what goes out*: it multiplies the most
expensive request the tool makes to buy worse-fitting rows, and a query needing
page two should have been a better query.

**Back off videos that sweep fruitlessly.** Rejected under *the ordering*: it
penalises the videos the routine was built for and rewards the ones the walk
already covers.

**Give the sweep priority over the walk instead of a reserve.** Rejected under
*the reserve*: priority is won by whoever asks first, and the walk asks four
times as often, which is the starvation this exists to prevent.

**One last-searched timestamp per video.** Rejected under *the ordering*: it
makes a new indexer inherit another indexer's search history, against ADR 0002.

**Keep the provenance flag after identification.** Rejected under *what comes
back*: a second claim beside the identification state, and an invitation to
treat provenance as evidence later.

**Let a sweep result identify as the video that was searched for.** Already
rejected by ADR 0023 and restated here because it is the mistake this ticket was
blocked on: the query put the word in the indexer's mouth, and the echo is not
an answer.

## Consequences

- **The schema gains three small things**: a last-searched timestamp per
  (wanted video, indexer) pair rather than per video; a boolean on ADR 0015's
  release row saying a search was the reason it is `Awaiting`, cleared when the
  row leaves that state; and neither is exported, the cache refilling itself and
  the ordering rebuilding from nothing in one round.
- **ADR 0014's wanted sweep is now fully specified** — its query, its paging, its
  ordering key and where its budget comes from. Its cadence and per-run count
  are unchanged, but they are now a *demand* the reserve may fail to meet.
- **ADR 0018 gains two Brakes** at the search stage: a daily query budget too
  small to carry the sweep's cadence, and a wanted video whose title is too thin
  to search. Both carry a count, a reason and a route — the first to the budget,
  the second to the video.
- **ADR 0015's open collision is closed**: the walk and the sweep divide the
  budget by reservation, and a walk that exhausts its share falls into that
  ADR's `maxage=` catch-up rather than eating the sweep's.
- **Ticket 24 is unaffected.** The sweep's query goes to an indexer, not to the
  local cache, so it adds no load to the title search that ticket is sizing.
- `CONTEXT.md`'s **Wanted Sweep** is sharpened to say what it searches with and
  that its results are identified like everything else.
