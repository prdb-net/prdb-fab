# Nothing local identifies anything, and a pre-name is only a reason to ask

Every identification in this tool is prdb's answer. Before a download that means
`POST /videos/identify` fed the release name, which reaches the two rungs of the
ladder a name can reach; after a download it means the same endpoint fed a hash.
What is held locally — the pre-names and titles of the videos the catalogue has
pinned — never identifies anything. It decides which of the hundreds of
thousands of cached releases are worth spending a request on, and that is all it
does.

## The authority, and why it is an endpoint about files

`POST /videos/identify` walks five rungs and the first that matches wins: OS
hash, perceptual hash, a stored file name, **the file name without its extension
read as a scene release title**, and finally **the site read out of the file
name**. The last two need no file at all. `filename` is the endpoint's only
required field and the document says of it exactly this: it "carries the lowest
rungs of the ladder".

So a release name goes out as `filename`, the release title with its extension,
and `osHash`, `pHash` and **`filesize` all null**. The size is the one worth
naming, because Newznab hands it to us in `newznab:attr size` and the field
exists on the request. It is not sent. A release's size is not a file's size —
it carries par2 overhead and may cover several files — and the only rung it
could serve is one that cannot fire without a hash. Sending it would be a lie
told for nothing.

What comes back is `videoId`, `confidence`, `matchedBy`, `candidates` and
`site`: the exact vocabulary
[ADR 0006](0006-acting-alone-needs-a-named-video-and-an-allowed-confidence.md)
requires before the tool acts alone. Two hundred names go in one request and the
whole request counts as one against the rate limit. Nothing sent is stored.

**`GET /predb/search-by-video` is not the authority.** It searches canonical
pre-names by keyword, groups by video, caps at 500 groups and has no paging —
one request per query, and the query would have to be a keyword *we* carved out
of a release name. That is release-name parsing, which is the thing this whole
decision exists to avoid doing locally. It is the right endpoint for a person
typing a search and the wrong one for a machine asking about a name it already
has in full.

`VISION.md` reserved this: *"Exactly how far pre-download matching reaches is a
design question to settle against prdb's API when this is built, not a promise
to make in advance. The architecture should assume it improves over time and not
bake in one route."* The split below is how that sentence is honoured — the
authority is one route, but the part that decides *what gets asked* is a
separate layer that can improve without the authority changing.

## Nothing local identifies anything

The catalogue already holds, for free, exactly the videos this tool cares about.
`CONTEXT.md` counts a wanted video as **pinned**;
[ADR 0013](0013-the-prdb-catalogue-is-a-cache-with-pinned-rows-repaired-by-re-reading.md)
repairs pinned rows by re-reading `POST /videos/batch`; and `preNames` is a
required field on `VideoDetailDto`. The pre-names and titles of every wanted
video and every library entry are therefore local, current, and cost not one
extra request.

The tempting conclusion is that a local pre-name hit *is* a match. It is not,
and the reason is ADR 0006: acting alone needs a named video **and** a
`confidence` from a listed set. `confidence` is prdb's word about its own
evidence. A string comparison here cannot produce one, and inventing a local
value to stand beside prdb's — `LocalExact`, or whatever it would be called —
would put a number the tool made up into the one gate that decides what gets
downloaded without being asked.

So the local layer is a **cost filter and never a matcher**. A hit means *this
release might be a video you want*; it justifies a request and decides nothing.
Every identification the tool ever records is prdb's, which is what makes ADR
0006's gate readable everywhere without a special case.

The error bill is deliberately lopsided. A false positive costs one two-hundredth
of a request. A false negative costs a wanted video the indexer walk never
reports — and that one is covered anyway, because the **wanted sweep**
([ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)) searches the
indexers for wanted videos by name and its results earn a request on provenance
alone.

## What the filter compares

Both sides are normalised the same way — lower case, every separator collapsed
to one, the extension dropped — and the test is **containment, not equality**:
the normalised pre-name occurs in the normalised release title. Equality would
miss almost everything indexers do to a title, and the whole point of the
lopsided bill above is that missing is the expensive direction.

Three things are compared, and not equally:

- a **pre-name** of a pinned video — a reason on its own;
- the **title** of a pinned video — a reason on its own, because not every video
  has a pre-name and not every indexer names after one;
- the **site title** — a reason only together with something else, never alone.
  Alone it would match every release that site ever put out and turn the filter
  into a pass-through.

That is the local counterpart of precisely the two rungs a name can reach at
prdb, `ReleaseName` and `Site`, and it promises nothing the authority behind it
cannot honour.

**The filter never parses a release name.** No release group, no resolution
token, no year, no season. The moment it began to understand what it reads it
would be a second matcher, and there is only one.

## Two routines, two lanes

The two halves have nothing in common. One is a join over new cache rows that
talks to nobody; the other is a prdb request that must pass the governor and is
paid for in units of two hundred. [ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)
gives them different homes:

- **Screening** runs in the **bulk** lane. It is a backfill's relative by volume,
  it may wait behind a repair pass, and its position over the indexer cache is
  the resumable cursor
  [ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md) requires.
- **Identification** runs in the **sync** lane, beside the prdb feeds, because it
  spends their budget and the same governor has to brake it.

Separating them means an empty prdb budget never stalls screening, and the
backlog is visible as a row count rather than as a routine that appears to hang.
A request is filled with up to two hundred releases from any indexer and any
reason; rows that got their reason from the wanted sweep go first, because
someone is waiting on those.

`includeVideoDetails` stays **off**. The filter's coverage is the pinned set, so
almost every match points at a video the catalogue already holds; for the
exception — a sweep result that turns out to be a different video — ADR 0013's
fetch-on-demand already exists, and two hundred full documents to save it is a
bad trade.

## Seven states, and no clock touches any of them

ADR 0015 made the identification state of a cached release the tool's only
cursor over the cache and left the state set to this decision. Splitting
filtering from identification means the honest count is seven, not the five that
would describe identification alone — two of them belong to the local half:

1. **`Unexamined`** — the walk wrote the row and nothing has looked at it.
   Screening's cursor points here.
2. **`Unremarkable`** — screened, and nothing local gave a reason to ask. **This
   is a statement about us, not about the release.**
3. **`Awaiting`** — a reason exists; identification's work list.
4. **`Matched`** — `videoId`, `confidence` and `matchedBy` written down.
5. **`SiteOnly`** — prdb reached the site and no video: a **Site-Only Match**,
   the outcome `CONTEXT.md` already has a word for.
6. **`Ambiguous`** — candidates and no video.
7. **`Unknown`** — prdb was asked about this name and answered nothing.

`Matched` and `Ambiguous` are final. **`Unknown` is never re-asked from this
side**, and that is the sharp end of the decision: prdb's answer about a name
does not depend on anything this installation does, so asking again is asking a
settled question. Nothing here runs on a clock — the same posture ADR 0015 took
towards a release disappearing upstream and
[ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md)
towards a download that looks stuck.

What *does* move a row is a **new fact arriving from the other direction**. A
video becomes wanted; a repair read of a pinned video brings back a pre-name
prdb has since added. Either way the new pre-name or title is searched **backwards
against the indexer cache**, and every row it reaches that is not already
`Matched` or `Ambiguous` — `Unremarkable`, `SiteOnly` and `Unknown` alike — goes
to `Awaiting`.

The two directions are what make the model closed. A new release looks itself up
in what we hold; a new pre-name looks itself up in what the indexers offered.
Both are local and free, and between them there is no case where the tool must
re-ask prdb a question whose answer cannot have changed. It also sharpens the
demand on ticket 24: searching the cache by title is no longer only the release
view's need, it is how every new fact reaches the rows that were written before
it existed.

`SiteOnly` earns its own state on thin but real grounds: it costs a column that
is in the response anyway, and it carries the one thing `Unknown` does not —
prdb knows the site, just not this release. It is honest in the release table and
it is a named outcome for ADR 0018's tally. **Nothing ever acts on it.** ADR 0006
forbids it, and
[ADR 0007](0007-automation-is-a-set-of-permissions-over-the-wanted-list.md)
ruled favourite-driven automation out of scope for exactly this reason.

## Provenance is never evidence

The wanted sweep builds a query out of a wanted video's own title. What comes
back looks like a match and is not one — the indexer was simply handed the word.

A sweep result runs through the same authority as everything else, with no
shortcut. **The tie-back to the video is the matcher's answer, never the subject
of the query**: if the sweep searched for X and prdb says Y, it is Y; if prdb
says nothing, it is nothing, and the sweep does not get to claim it. What
provenance is allowed to do is justify cost — it is a reason under *seven
states* above, and the strongest one, which is why those rows are asked first.

This is the rule ticket 23 was blocked on, and it is the whole of what this
decision owes it.

## Ambiguity before a download is nobody's question

There is a hole here and it is deliberate. A wanted video has a release
available, prdb cannot say which video the name is, ADR 0006 forbids acting, and
[ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)'s
review queue holds **video files**, so a release that was never downloaded has
nowhere to go. Nobody is told.

It stays that way. The question *which of these four videos is this release* is
not one a person can answer, because they would be answering from a name they
can already read — no file, no runtime, no hash, nothing the tool did not
already have when it asked. ADR 0011 settled the same shape once already: the
quality is read from the file, so nothing before a download decides anything.
Here it is identity rather than quality, and the answer is the same. The
question is answerable where the evidence is, and before a download there is
none.

So no Brake and no surface — but not silence either: ADR 0018's confidence tally
is **per named outcome**, so `Ambiguous` and `Unknown` are counted at the Match
stage rather than being invisible.

The price is stated plainly: **a wanted video whose every release is ambiguous is
never fetched and never reported.** That is accepted, because the alternative is
inviting someone to guess and then filing what they guessed.

## Considered options

**Let a local pre-name hit be a match.** The cheapest possible design: the
catalogue holds pre-names, releases have names, join them and act. Rejected
under *nothing local identifies anything* — it needs a `confidence` that only
prdb can issue, and inventing one puts a number the tool made up inside ADR
0006's gate.

**Sync `GET /predb/latest` into a complete local pre-name index.** Structurally
the most attractive alternative by some distance: it is a genuine incremental
feed (`createdAtUtc` descending, `CreatedFrom`/`CreatedTo`, page size 500) and
`LatestPreDbItemDto` carries the linked video, so it would make the local layer
nearly complete and turn the whole question into a local join. Rejected **for
now, on a missing number**: prdb publishes no row count for its PreDb, so the
backfill cost is unknown by an order of magnitude; it would be a sixth feed
against ADR 0013's five; there is no `GET /predb/{id}` and therefore no repair
path; an entry linked to a video after we copied it stays unlinked with us
forever; and the feed has no `Category` filter, so the backfill would drag
movies and TV along. Kept as fog rather than as a ticket, because nothing in the
first release waits on it — it is an optimisation that can only be argued from a
measurement against real data.

**`GET /predb/search-by-video` as the authority.** Rejected under *the authority*
— one request per query, no batching, a 500-group cap, and it would require this
tool to carve a keyword out of a release name.

**Identify every cached release eagerly.** The reading ADR 0015's wording invites.
Rejected on arithmetic: a bootstrap is `maxage=90` against a cache bounded at
100 000 rows per indexer, which is up to 500 requests per indexer at 200 names
each, against an idle profile of about nine requests an hour. It would be weeks
during which the governor let nothing else through — and it buys nothing, because
ADR 0007 makes the wanted list the only source of intent, and a release matched
to a video nobody wants leads nowhere.

**Send the release size as `filesize`.** Rejected under *the authority*: a
release size is not a file size, and the only rung it could serve cannot fire
without a hash.

**Re-ask prdb about `Unknown` rows on a schedule.** Rejected under *seven
states*: prdb's answer about a name cannot change because of anything here, and
the case that would change it — prdb linking a pre-name later — reaches the row
from the pre-name side instead, which is free.

**Give the sweep's result the video it searched for.** Rejected under *provenance
is never evidence*: the query put the word in the indexer's mouth, and treating
the echo as a match is how a library fills with the wrong files.

**A surface, or a Brake, for ambiguous releases.** Rejected under *ambiguity
before a download is nobody's question* — there is no setting behind it and no
evidence a person could weigh, and ADR 0022 already has the place where this
question gets asked properly, once there is a file.

## Consequences

- `CONTEXT.md` gains **Pre-Name**, **Screening** and **Identification State**,
  and **Identification** is sharpened to say that it is always prdb's answer and
  never computed locally — which is the load-bearing rule of this decision and
  was nowhere written down.
- `VISION.md` is amended. Its graded promise says a release that "resolve[s] only
  to a candidate, or to nothing" becomes "a decision the user makes"; that is
  true after a download and false before one, and the sentence now says so.
- The cached release row gains an **identification state** with the seven values
  above and, where it is `Matched`, the `videoId`, `confidence` and `matchedBy`;
  where it is `Ambiguous`, the candidates; where it is `SiteOnly`, the site. None
  of it is exported — ADR 0015's cache refills itself.
- ADR 0014 gains two routines: **screening** in the bulk lane, carrying ADR
  0015's cursor over the cache, and **identification** in the sync lane, batching
  two hundred names per request.
- ADR 0015's open question is closed: which states exist, and that the state is
  the cursor rather than a claim about upstream.
- Ticket 23 is unblocked, with one rule to obey: the sweep's tie-back is the
  matcher's answer.
- Ticket 24's demand grows. Searching the indexer cache by title is not only the
  release view's need — it is how a newly wanted video and a newly arrived
  pre-name reach rows written before either existed, which makes it a background
  routine's query and not only a user's.
- ADR 0018's Match stage counts `Ambiguous` and `Unknown` among its named
  outcomes, and can show the number of releases awaiting identification.
- Two entries go to the prdb wish list: that identifying by name rather than by
  file be blessed or given its own endpoint, and that the pre-name feed be cut so
  it is usable — `Category` on `GET /predb/latest`, or `videoId` on
  `PreDbSummaryDto`, plus a `GET /predb/{id}` so pre-names have a repair path at
  all.
