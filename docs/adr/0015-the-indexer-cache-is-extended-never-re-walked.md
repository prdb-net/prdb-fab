# The indexer cache is extended, never re-walked

The local copy of what the indexers offer is only ever added to. There is no
scheduled full re-walk: an indexer walk stops at a watermark made of the field
the server actually sorts by plus an identity it already holds, and every other
pass over an indexer is a one-shot routine with a named cause. Inside the tool,
novelty is not a timestamp at all — each cached release carries its own state,
and the routines that read the cache take rows in that state rather than rows
after a clock.

## Why the cursor is two different things

No Newznab implementation can order results by when it indexed them. The feed
is sorted by Usenet post date while `pubDate` reports the index-add date, so a
release backfilled today with a three-year-old post date sorts hundreds of pages
down. Paging until a known date is reached therefore both stops early and misses
that release permanently — the finding that made this ticket necessary.

The answer is to stop conflating "where do I stop asking the indexer" with
"what has this tool not looked at yet".

**Outward**, against the indexer, the walk pages against `usenetdate` — the
field the server sorted by, available only because `extended=1` is always sent —
and stops on either of two conditions: the page's oldest post date has fallen
below the watermark, or a release identity the cache already holds appears on
the page. Both, because each alone breaks: the date alone is defeated by
backfills, the identity alone by releases being deleted upstream. A short page
ends the loop as well, and `total` is never believed — three of five surveyed
implementations either cap it or omit it on an empty page.

**Inward**, a timestamp cursor would be wrong for a different reason. Walk
writes commit in batches (ADR 0004), so a row written during a matching run can
carry a first-seen instant already below the watermark that run sets afterwards,
and would never be looked at again. A silently skipped row is the most expensive
failure this cache has — it is a wanted video that is never found, and nothing
reports it. So each cached release carries an identification state, and the
matching routine takes the next rows that have not been looked at. That is also
the resumable position ADR 0014 requires of every routine.

The first-seen instant stays, for what it is good for: eviction order, the "new
at this indexer" sort in the release view, and whether a walk achieved anything.
It is data, not a position.

## Why nothing is ever re-walked

A scheduled full re-walk would spend an indexer's daily query budget re-reading
rows that are already there, and it would displace the wanted sweep — the only
routine that finds anything older than the installation. There are four causes
for a pass beyond the recurring walk, and no fifth without another decision:
bootstrapping a new indexer, a category set that grew, the missed window ADR
0014 already turns into a catch-up routine, and an indexer that was disabled or
broken for a long time.

That last one is deliberately not a special case. The walk runs normally, fails
to reach its watermark, hits the paging ceiling, and creates the same `maxage=`
catch-up over exactly the window it missed. Re-enabling an indexer needs no
logic of its own, and a walk broken for two days is the same case as an indexer
switched off for two weeks. Resetting the cursor instead would re-pull the whole
bootstrap window rather than the gap.

The bootstrap is bounded by age rather than by pages: `maxage=90` days, walked by
offset in the bulk lane, ending at a short page or at that boundary. It never
claims to have reached the bottom, because retention is not reliably reported by
anyone. Ninety days is enough because the walk was never how old material is
found — the wanted sweep searches for it by name.

## Why the bound is rows, and what it may not evict

The cache is capped at 100 000 rows per indexer, evicting oldest first seen
first — counts rather than durations, the same choice ADR 0013 made for the
catalogue, so the tool has one rule and not two. Pinning is the same idea on the
indexer side: a row is pinned while something local points at it — a download, a
consumed release, a review queue entry, or an identification against a video
that is still wanted. That last one matters most: a release identified as a
wanted video and not yet downloaded is exactly the row the cache exists to
produce, and it must not vanish because a walk pushed 100 000 unrelated rows in
behind it.

Eviction never touches a row that has not been looked at. If the ceiling cannot
be held without doing so, that is a **Gap** — the indexer delivers faster than
identification consumes — and not a licence to break the rule. Degrading
visibly is the shape ADR 0014 chose for a plan too small, and dropping a release
nobody ever examined is precisely the failure the row state was chosen to
prevent.

## Consequences

- **Release identity is derived by a three-step ladder**: `newznab:attr guid`
  when present, otherwise the last path segment of `<guid>` when it carries a
  scheme, otherwise `<guid>` verbatim — Spotweb's is a Message-ID and must not
  be path-split. The raw value is kept beside the derived one. The download URL
  is never part of the key, because it embeds the user's API key and changes
  when that key is rotated; it is a column that is rewritten. An item yielding
  no identity is dropped and counted, never keyed off its title.
- **A release re-appearing is an upsert** on that identity: title, size,
  categories and download URL are overwritten, first seen is not. A row that
  eviction removed and a later pass brings back is a new row with a new first
  seen, and is honestly matched again — which cannot cause a second download,
  since a consumed release is pinned and automation acts only over the wanted
  list (ADR 0007).
- **Wanted sweep results land in the same table** under the same identity and
  the same rules, with no record of which query produced them: ADR 0002 fixes a
  release to an indexer and an id, not to a route. The sweep does not move the
  watermark — it returns old material, and counting it as progress would make
  the walk skip genuinely new releases.
- **A release disappearing upstream is discovered when it is used**, not
  searched for. There is no maintained last-seen: verifying a row costs a query
  from the budget the wanted sweep needs, to answer a question that answers
  itself for free when the NZB fetch or the download fails, where consumed and
  the next release in the ranking (ADR 0008) already take over.
- **The caps document is stored per indexer** as the full `(id, name)` category
  tree and re-read weekly by a routine of its own. Configured categories are
  stored as ids but re-resolved by name, because the 6000 range disagrees
  between implementations: a number that moved under a name is followed
  silently, a name that disappeared is a Gap at that indexer rather than a
  quietly empty `cat=` list. A set that grew this way is a category extension
  and triggers its catch-up.
- **The schema gains the cache side**: a release row keyed by indexer and
  derived id, with the raw guid, title, size, categories, post date, index-add
  date, download URL, first seen, an identification state and a pin reason.
  Which states exist is not settled here — that belongs to how a release becomes
  a known video.
- **The search space is now a number.** ADR 0004 left open whether searching the
  cache by title needs SQLite full-text search; it is bounded at 100 000 rows
  per indexer, which is what that question was waiting for.
