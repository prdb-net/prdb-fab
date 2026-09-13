# A Preview fetches the rest of a Video's pictures on sight, and they are evicted first

A **Preview** is a Video opened over the grid it was found in: the facts the
card shows, the Actors, the actions the card offers, and every picture prdb
publishes for it rather than the one the card shows. It is a sheet over a list
that stays mounted underneath, addressed by a query parameter on that list's own
address, and it is closed by a click outside it, by `Esc`, by its close control
or by the browser's back button.

The images it shows that are *not* the Video's chosen one get bytes **only when
a Preview asks for one**. They count against the unpinned ceiling, they are the
first thing evicted out of it, and nothing warms them, pins them, files them or
backs them up.

This takes back one sentence of
[ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md).
Everything else that decision settled stands, and so does all of
[ADR 0059](0059-the-artwork-cache-is-warmed-ahead-of-the-grid-and-a-serve-stops-writing.md).

## The sentence that is taken back

ADR 0030 rejected **Cache every image** in two clauses:

> no surface displays a second image, and the store becomes a multiple of the
> catalogue for rows nobody looks at

The first clause stops being true here, because this is the surface. The second
clause is still true, and it is what shapes everything below: the cost of
holding ten pictures for every Video in the Catalogue is not one this tool may
incur, so the pictures are fetched one at a time, by somebody looking at them,
and dropped before anything else when the disk is wanted back.

The choice that ADR 0027 and ADR 0030 made together — *the* image of a Video is
the first entry of `images[]` carrying a URL — is untouched. That image is still
what every grid shows, what filing copies as the Entry Image, and what the warm
pass fetches. A Preview adds pictures beside it and changes nothing about it.

## What a Preview may cost

**Opening one spends no prdb request.** Everything above the gallery — title,
Site, release date, Consensus Runtime, Actors, wanted and held state, and the
identity of every image — is already in local rows. ADR 0013 writes a Catalogue
Video from a `VideoDetailDto`, and `images[]` comes with it; ADR 0030 stores
every entry of that array as a row and only chooses one of them to hold bytes
for. So the knowledge is here and the bytes are not, and it is exactly the bytes
a Preview needs.

**A picture is fetched when it is scrolled into view, not when the sheet
opens.** A Video in this Catalogue carries about ten images where it carries
any, and opening a Preview must not be ten CDN fetches. The gallery is lazy the
way the grid is lazy — the same observer, the same margin, the same no-artwork
tile on an empty answer — so a Preview that is opened and stepped past has
fetched the one picture that was on screen.

**A grid walked with the arrow keys is a grid of one-picture Previews.** That is
the property that makes the sheet cheap enough to open by reflex, and it is
deliberate rather than incidental: any design where opening a Video prefetches
its gallery would make stepping through a page of twenty-four cards a fetch of
two hundred images.

## Why the ceiling needs one narrowing

ADR 0030 puts the pinned half of the cache outside the ceiling and never evicts
it, because a pinned Video's picture is the library grid's tile and the file
filing copies. `ArtworkEviction` implements that by joining the pinned Videos to
the image table on the *Video* — so every image row of a pinned Video is treated
as pinned.

That was harmless while only one image per Video ever held bytes. It stops being
harmless here: a held Video whose Preview somebody opened would put nine more
pictures outside the ceiling permanently, and the half `VISION.md` calls
disposable would grow with the library instead of with the browsing.

**So the pin narrows to the chosen image.** What a pinned Video protects is the
picture ADR 0027 chose, and nothing else. The rest of its pictures are evictable
like anybody else's, and `ChosenImages` — which already exists, and which is
already how a serve finds the image for a Video — is the clause that says which
is which.

## Why least-recently-served is enough, with nothing added

The obvious extra rule is *evict a second picture before a chosen one*. It is
not needed, because the existing order already does it.

A chosen image is served by every grid that lists its Video — What's New,
Search, Sites, Actors, Wanted — so its `LastServedAt` is refreshed by ordinary
browsing. A second picture is served when somebody opens a Preview and scrolls
to it, which happens to a tiny fraction of the Catalogue and does not repeat.
Sorting by `LastServedAt` therefore puts the gallery ahead of the grid on its
own, and a picture that has never been served at all — fetched but not looked
at — already sorts first.

Adding a category would mean a column saying which population a row is in,
which is the kind of stored fact ADR 0033 refuses when a clause already answers
it. The clause is `ChosenImages`.

## What is unchanged, and is worth saying so

- **The per-file ceiling and the content check.** `ArtworkCeiling.AnImage`
  applies to a gallery picture as to any other; bytes that are not an image are
  not kept.
- **A dead URL is marked once.** `FoundDead` on a second picture means the same
  thing it means on a chosen one, and nothing asks again.
- **Nothing here passes the governor.** A picture is a `GET` against a CDN
  whose URL prdb handed out in a payload that came through the documented API.
  ADR 0030's argument applies unchanged.
- **Nothing here is in the backup.** Both halves fail ADR 0009's *cannot be
  fetched again* test, and a gallery picture fails it hardest.
- **ADR 0059's warm pass stays on the chosen image.** Warming ten pictures for
  every Video on the front of the Catalogue is the bandwidth cost ADR 0030
  refused and this refuses too. Six gigabytes of warm chosen images and a
  gallery filling the space under the ceiling behind them is the intended shape.

## Why the route is addressed by the image and not by the Video

`/api/artwork/{videoId}` answers with ADR 0027's choice and goes on doing so;
the grids keep the address they have, and a changed choice still needs no change
in the browser. That property depends on the address naming the Video rather
than the picture, and it is worth keeping.

A gallery is the other case. It knows exactly which pictures it wants, because
the Preview read told it, and *the fourth picture of this Video* is not a name
the cache has — positions shift when prdb publishes another image, and an
address that meant one picture on Tuesday and another on Wednesday cannot be
cached for a year. So a second route, `/api/artwork/images/{imageId}`, addressed
by the image's own id, which is also the name of the file on disk.

**Image URLs do not cross the API boundary.** The browser asks the tool for a
picture and never the CDN — that is ADR 0030's first sentence about the display
path, and the reason the timeout, the size ceiling and the dead-URL mark are
enforceable at all. So the Preview read names images by id and sends no URL,
which is the difference between a rule and a convention.

## Why the Preview is a sheet and not a page

[ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)
makes five surfaces artwork grids and keeps the Release view as one table
reached from a Video. Nothing in it says what happens between those two, and
what happens today is nothing: a Catalogue card is inert, and the only way into
a Video is the Release table, which is a different page about a different
question — what the Indexers have.

A Preview is the answer to the other question, the one asked in front of a grid:
*is this the scene I think it is?* It is worth answering without leaving the
grid, because the grid is where the person is and paging back to it is the cost
that stops them asking. So the list is not unmounted, not re-read and not
re-paged while a Preview is open, and closing one is four cheap gestures rather
than a navigation.

It is an address, because
[ADR 0036](0036-the-frontend-takes-two-dependencies-and-the-address-bar-holds-what-is-linkable.md)
puts what is linkable in the address bar and *which Video is open over which
page of which list* is linkable. It is also what makes the back button close the
sheet instead of leaving the list, which is the gesture a phone offers first.

## Considered options

**Make the Preview a route of its own, `/videos/{prdbId}`.** Rejected: a route
replaces the list, which means unmounting it, re-reading it on the way back and
losing the scroll position — the cost this exists to remove. A Video that
deserves a page of its own already has one for what it holds
(`/library/{id}`) and one for what the Indexers have (`/releases?video=`).

**A centred modal dialog rather than a right-hand sheet.** Rejected on what the
uncovered part of the screen is for. Both leave a margin; a sheet leaves it all
on one side, where it is a column of real cards that can be clicked to move the
Preview on, and a dialog leaves an even frame that is nothing but a place to
click to close. On a phone both are the whole screen and the question does not
arise.

**Prefetch the gallery when the sheet opens.** Rejected under *what a Preview
may cost*: it multiplies the cost of a glance by ten and makes walking a grid
with the arrow keys the most expensive thing in the tool.

**Take the pictures from `GET /videos/{id}/user-images`.** Deferred rather than
rejected. prdb publishes user-submitted previews with sprite sheets and paired
WebVTT, which is a better gallery than `images[]` and possibly a scrubbing strip
— and it is a request per Video against the governor, a second population in the
cache with its own moderation state, and a payload this tool stores nothing of
today. It is a decision of its own and wants making on its own.

**Let the Preview trigger a detail read when a Video has no images.** Accepted,
but as its own thing rather than as part of the gallery: see *The one request a
Preview may spend* below.

**Cache the whole of `images[]` for every Video, as ADR 0030 considered.** Still
rejected, and for ADR 0030's second clause rather than its first. Ten pictures
for 47 000 Videos is a multiple of the Catalogue in bytes for pictures nobody
opens, which is what the ceiling exists to prevent.

## The one request a Preview may spend

About one Video in ten on the front of the Catalogue carries no image row at
all. `GET /videos` sends no images — that is why the images feed exists — so a
row written from the list feed arrives without them and acquires them when the
feed or a repair read reaches it. The gap closes on its own; it does not close
while somebody is looking at the sheet.

So a Preview opened on a Video with **no image rows at all** asks prdb for that
Video's detail. The shape is
[ADR 0054](0054-an-actor-catalogue-fill-is-explicit-bounded-and-durable.md)'s,
applied to one Video:

- **Explicit.** It happens because a person opened a Preview. Nothing sweeps for
  candidates, and a Video that has any image row never triggers it.
- **At most once per Video, durably — and nothing new is recorded to say so.**
  `CatalogueVideo.LastReadAt` already says when a Video was last read from prdb
  in detail, and the detail write moves it. So *read recently and still no
  pictures* **is** the answer *prdb publishes none*, kept where it already
  belongs; a second column saying *asked* would be ADR 0033's stored fact with
  two writers and no reader that would notice them disagreeing. A row a list
  feed wrote carries no stamp at all, which is exactly the Video worth asking
  about — and the repair pass already reads that same absence as *take this one
  first*.

  The word is taken for a **week**. Not forever, because a Video may acquire a
  picture later and the images feed carries only what is newer than its cursor;
  not shorter, because a Video that genuinely has none is the ordinary case and
  must not cost a request every time somebody glances at it.

  While the read is outstanding the **routine row is the record**, so reopening
  the sheet a hundred times schedules once and a restart does not forget.
- **Refusable, with a place in the order of precedence.** `PrdbWork` is an
  ordered enum whose declaration order *is* ADR 0014's precedence. This goes
  below `Writes` and above the feeds: somebody is sitting in front of it, which
  argues for high, and it is the only kind a person can cause by clicking, which
  argues against the top — a write is a queued obligation and this is a glance.

  Its share is **8 %**, which is not a step on the five-point staircase the
  feeds are on. The staircase was laid out before this existed, and moving every
  number on it to keep the steps round would change what each feed is held back
  from, for nothing. Between a write and a feed is where this belongs, and eight
  is between.
- **Not in the request's path, and asked for by a POST of its own.** The sheet
  never waits on prdb: it renders what it has, and the gallery fills in when the
  read lands. A GET that scheduled work as a side effect would make *refreshing
  never causes work* a rule the code contradicts in the one place it is easiest
  to read, so the person's act is a request of its own, the way ADR 0054's fill
  is.

  The Preview read says whether one is outstanding, so an empty gallery can say
  that something is being done about it and the sheet can re-read while it is —
  a local query, not a prdb request. The routine sits in the **Sync** lane
  rather than the Bulk one for the reason ADR 0049 put manual search there: the
  Bulk lane is where work goes that nothing waits on.

ADR 0018's rule is intact. What that rule protects is the governor and the
indexers' daily budgets against a person who presses reload; this is not a
reload, and ADR 0022 already drew that line — work a person asked for is not the
same as a page reading itself.

## Consequences

- **`CONTEXT.md` gains *Preview*.** A Video seen without leaving the grid. It is
  not the Release view, which is what the Indexers have, and not the Library
  Entry, which is what is held on disk.
- **Two routes.** `GET /api/catalogue/videos/{prdbId}` answers one Video from
  local rows; `GET /api/artwork/images/{imageId}` answers one named picture with
  bytes. The second is ADR 0040's stated exception for a route an `<img>` tag
  asks for, the same as the two artwork routes already there.
- **`ArtworkEviction`'s pin narrows to the chosen image**, which is a change in
  behaviour for a pinned Video with more than one picture — and there are none
  with bytes today, so nothing in an existing installation moves.
- **The Catalogue card becomes a control.** It is inert on five surfaces today;
  the artwork and the title become the way into a Preview. The Library card
  keeps its link to `/library/{id}`, which is a page about files rather than
  about a Video.
- **A new kind of prdb request exists**, with a place in ADR 0014's order and a
  share held back from it. It is the first one a person causes by browsing.
- **The frontend gains no dependency.** ADR 0036 allows two, the sheet is built
  from what is already there, and the gallery reuses the grid's image loader
  rather than adding a second one.
