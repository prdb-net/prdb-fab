# The status page reads the loop, and a brake is not a gap

The page earlier ADRs call the sync status page is renamed **Status**, because
it carries filing, both confidence gates, retry budgets and library
verification, none of which is Sync as `CONTEXT.md` defines it. It is cut into
the six stages of `VISION.md`'s loop rather than into connections. It verdicts
on **Gaps** alone, and everything that holds work back while working correctly
is a **Brake** — a second class that is counted, routed to the setting behind
it, and never allowed to colour the verdict.

## Why two classes

Six tickets put two very different things on this page. One is the **Gap**:
SABnzbd unreachable, a path mapping that does not resolve, an indexer rejecting
its key, no indexer configured at all, a prdb plan that does not carry the
schedule. The other is the pile of silent-failure diagnoses each of ADR 0006,
0007, 0008 and 0009 contributed one of: the confidence gate blocked everything,
releases held back by a size limit, releases waiting on the cap of twenty
unfinished automatic downloads, videos whose retry budget is spent, library
entries a restore could not confirm.

Merging them is tempting, since the user whose wanted video never arrives does
not care which of the two stopped it. It is rejected, and the reason is what the
page is for. A size limit doing exactly its job would then light the page up
every day, the user would learn that the page is always red, and the day
SABnzbd actually goes away they would not look. A page that cries wolf is worse
than no page, because `VISION.md` sells it as the thing that replaces checking
by hand.

So a Gap asks to be fixed and carries a route to the form that fixes it. A Brake
carries a count, the reason, and a route to the setting behind it — and may well
be exactly what was asked for. The one boundary case is settled by ADR 0014 and
not reopened here: a plan too small to carry the schedule is a **Gap**, because
nobody chose it.

## The headline is two facts, not a verdict

If Brakes count into the headline, it is never green for anyone with a size
limit and the number is worthless. If they do not, the page reports "everything
is working" to an installation whose gate is set too high and which has
downloaded nothing for a week — precisely the failure ADR 0006 raised this
ticket to prevent.

The headline is therefore two facts rather than one judgement:

1. **The Gap count.** "Everything is working", or "2 things need attention".
   Gaps only. Brakes are counted separately, below, and never colour this.
2. **A liveness line** naming the last thing the tool actually achieved: the
   last file filed, the last download started, the last release added to the
   indexer cache.

The second catches the too-high gate, the exhausted budgets and the empty wanted
list without painting a correct configuration as broken — and without the tool
inventing a threshold it cannot know. Whether six days without a download is
wrong depends on the user's list, and only they can say. This is ADR 0016's *no
clock* applied to the page: show the elapsed time, do not judge it.

A traffic light is rejected for the same reason, which is also why the page is
not called Health.

## Why the loop and not the connections

Cut by connection, the page has a leftover pile: both gates, the size limit, the
automation cap and the spent retry budgets belong to no connection at all. Cut
by the six stages `VISION.md` numbers — Sync (prdb), Sync (indexers), Match,
Decide, Download, File — every routine and every condition has exactly one home,
and the page answers the question the user actually has, which is not "what is
broken" but "where does it stop".

This does not conflict with rolling a Gap up to what fixes it. That is
de-duplication; this is placement. An indexer whose key is rejected fails both
its walk and its wanted sweep, and ADR 0014 raises a Gap per routine — but the
page shows **one** Gap, with one route to the indexer form and the affected
routines named underneath, sitting at *Sync (indexers)*. The headline count is
then a count of repairs rather than of symptoms.

## What each stage carries

Healthy, each stage is one line per thing: when it last succeeded and what it
returned. The routine table of ADR 0014 is the detail behind that line, not the
page.

- **Sync (prdb)** — the five feeds and repair; the rate-limit budget read from
  the last response as a plain fact; a Brake when the governor actually deferred
  requests in the window; a Gap on a permanent refusal, on three consecutive
  failures, or when the plan does not carry the schedule.
- **Sync (indexers)** — per indexer, the last walk and the last sweep with **two
  numbers, results seen and rows added**, because their difference is the
  diagnosis: a walk seeing a hundred and adding none has reached its watermark
  and is healthy, while a walk seeing none has an indexer returning nothing, and
  those read identically if only the second is shown. The daily query budget as
  a fact, and a Brake when it is exhausted — the most load-bearing Brake on the
  page, since ADR 0014 makes the wanted sweep the only route by which an older
  wanted video is ever found.
- **Match** — when identification last ran, and how much of the indexer cache is
  still unidentified. (*Generalised by
  [ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md):
  this line was the first instance of a rule, and now it is the rule. A routine
  paced by a work set rather than a clock is drawn as **two facts** — the size
  of the set and when an item last completed — because an empty tick is not a
  run, so *last success* would age forever on a tool that is merely caught up.
  An empty set is neither a Gap nor a Brake. The file lane adds what is being
  filed and since when, read off the `Filing` row ADR 0026 already writes.*)
- **Decide** — the two gate tallies and the automation Brakes. (*Amended by
  [ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md),
  which adds one: automation held for a video because that video has an open
  review queue entry. It routes to the entry rather than to a setting, which
  is the first Brake whose destination is a decision waiting on a person — and
  it exists because without it the sample case is an unattended loop bounded
  only by the retry budget.*)
- **Download** — SABnzbd reachability, whose Gap ADR 0016 raises on the first
  failed contact rather than after three; the four installation conditions as
  Gaps; a Brake for videos whose retry budget is spent; and one summary line for
  outstanding downloads.
- **File** — collecting, the review queue count, and the restore Gap below.

One-shot routines still running — the first walk of an indexer, the What's New
backfill, a catch-up window, the restore verification — show progress and are
explicitly not Gaps, which is the distinction ADR 0013 drew and ADR 0014 kept.

## The gate diagnosis

`CONTEXT.md` says a **Confidence** is a set of named outcomes and never an
order. Any slider, threshold marker or bar axis would assert the ordering the
project explicitly denies, so the display is a **tally per named outcome** with
a mark on those the gate admits.

There are two tallies, not one, because ADR 0006's two gates see populations
with nothing in common: the gate before a download sees releases, the gate after
it sees files. Each covers the last **seven days** — the one fixed window on
this page that is derived rather than invented, since the ticket's own test is
whether someone who has not looked in a week can tell. When nothing passed, the
plain sentence ADR 0006 demanded sits above the tally: no release passed the
download gate in seven days, and this is what came back instead.

## Downloads, and what stays off the page

Outstanding downloads appear as **one line** — how many, the oldest with its
*outstanding since* and its last seen SABnzbd status — and the full list lives
on its own surface. ADR 0016 declares nothing stuck by elapsed time, so a person
reading that line is the only place a stall exists at all, which is why the
signal belongs here; repeating the list would make this a second downloads view.

`fail_message` and `stage_log` do not appear here. They are verbatim text for
somebody who already knows which download they are looking at, and they belong
on its row.

## Clearing, retrying, and the absence of a dismiss

**Nothing on this page can be dismissed.** Every entry leaves because the world
changed, never because it was acknowledged. A dismissible condition is one the
user hides once and never sees again, which is the same failure as the page that
is always red.

- A Gap on a **stopped** routine — ADR 0014 stops a routine on a permanent
  refusal — never clears by itself: it carries a route to the form and a resume.
- A Gap on a **backing-off** routine carries no action, because the routine is
  already retrying, and the page says so instead of offering a button that adds
  nothing. *Run now* remains available and skips the backoff, but never the
  governor.
- A **Brake** has no retry, only the route to the setting behind it — with one
  exception that has a real action: the spent-retry-budget Brake leads to
  ADR 0008's reset, which ADR 0016 made one operation, discarding that video's
  download rows.

## What the page remembers

The current state alone would report a fully green page to an installation that
was broken for four days and recovered an hour ago — which fails the week-long
question by omission. So a **cleared Gap stays visible in a weakened form**, and
its retention follows the material rather than a new clock: it is shown for as
long as the routine that raised it still holds the failure in ADR 0014's log of
its last fifty runs. A five-second routine therefore forgets faster than a daily
one, which is correct, since four days of a five-second routine failing is
already visible everywhere else. Anything beyond that — runs over time, failure
rates — is the dashboard's question, not this page's.

## Restore verification

It has three phases and only the last was open. While the pass runs it is a
one-shot routine in the bulk lane showing progress, and ADR 0014 already says
that is not a Gap. When it finishes with entries it could not confirm, it **is**
a Gap: ADR 0010 makes the library root one of the two things onboarding must
verify, and files that are not where the record says they are is that connection
no longer verifying.

It is raised **once, on the library** — so many of N entries could not be
confirmed — and it does not guess whether the cause was a mis-mounted share or
files the user deleted, because it cannot tell. Its route is not a new surface
but the **library under an "unconfirmed" filter**, which costs nothing given
ADR 0012 already requires filed path, quality and size to be readable without
touching the filesystem. It clears when nothing is unconfirmed. ADR 0009's rule
that unconfirmed entries still count as **held** is untouched, and this Gap is
the only place that decision becomes visible.

## Refreshing

The page polls a local endpoint every five seconds while it is open, matching
the live lane's cadence, and the elapsed times tick on between polls so "4
seconds ago" does not freeze. No server push: a second transport for the same
answer, for one user on one host, buys nothing.

**Refreshing never causes work.** No feed is fetched, no indexer queried and no
contact made with SABnzbd because somebody opened the page. The page reads state
and the routines produce it — otherwise the tool would have a second,
unbudgeted path to prdb and the indexers, and ADR 0014's governor would be
bypassed at exactly the moment a person is impatient.

## Consequences

- **`CONTEXT.md` gains a section.** **Brake** and **Status** are added and
  **Gap** moves beside them under *Knowing it works*, since Gaps are now raised
  at runtime far more often than during onboarding. Gap's definition widens to
  include a routine that has failed enough times to count.
- **`VISION.md` is amended.** The page is renamed throughout, and its section
  states the Gap/Brake split and the liveness line. Earlier ADRs still say
  "sync status page"; they are records of when they were written and are not
  rewritten.
- **The data model gains what the page reads.** The gate tallies need the
  outcome of every identification kept with its time for seven days, and the
  automation Brakes need the reason a release was not downloaded to be counted
  rather than discarded — neither is exported, both being recomputable. The
  liveness line needs nothing new: the last filed file, the last download and
  the last cached release are already rows.
- **A downloads surface is now required and is not the dashboard.** The full
  outstanding and failed list has to live somewhere, and `VISION.md` does not
  put the dashboard in the first release. That is left open here. (*Answered by
  [ADR 0028](0028-downloads-are-a-table-of-their-own-and-the-release-view-answers-for-the-video.md):
  a table of download rows on a route of its own, beside the library and the
  review queue, with `fail_message` and `stage_log` on the row and *stop
  following* as its one action. Nothing of the dashboard is pulled forward. The
  per-video half — the retry budget and its reset — goes to ADR 0012's release
  view instead, which is where this page's spent-budget Brake now routes.*)
- **Everything on the page is derived.** There is no condition table: a Gap and
  a Brake are both computed from routine rows, download rows and counts at read
  time, so nothing has to be kept in step and a restart invents no state.

## Considered options

**One class of condition.** Rejected above: a correct configuration would report
problems daily until the page stopped being read.

**A traffic light, or calling the page Health.** Rejected: both promise a single
verdict over things that do not reduce to one, and green would be a lie exactly
when the gate is too high.

**Cut the page by connection.** Rejected: the gates, the size limits, the
automation cap and the retry budgets belong to no connection, and "where does it
stop" is the question actually being asked.

**Let the page dismiss or snooze a condition.** Rejected under *the absence of a
dismiss*.

**Keep the name "sync status".** Rejected: `CONTEXT.md` defines Sync as catching
up with what prdb, the indexers and SABnzbd say, and more than half of what the
page carries is not that.
