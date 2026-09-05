# The Library is ordered by what arrived, and the order is the person's to change

The Library grid is ordered by **when Filing moved the files in**, newest
first, and a person can change that order to the Video title in either
direction. The choice lives in the address bar as `sort`, the way ADR 0036
requires of anything linkable.

`library_entry`.`FiledAt` gets an index. `catalogue_video`.`NormalisedTitle`
does not, and the reason is measured rather than inherited.

## Why the order was never decided before

ADR 0012 settled what the Library *shows* — held Videos and nothing else — and
settled the Release view's order in the same breath, because ADR 0008 had given
that view a ranking with an argument behind it. The Library's own order was
never argued. It sorted by title because a grid has to come back in some order
and the title was there.

That default was also the odd one out. Of the five browse surfaces ADR 0012
names, What's New, Wanted and Catalogue Search's default are all newest-first;
the Library was the only Video grid ordered alphabetically, without anything
saying why it should be.

## Why what arrived is the default

A Library is asked *what have I got* and *what turned up*, and only one of those
two questions is answered by an alphabetical list. `FiledAt` is the one column
in the schema that can answer the second — the Catalogue's dates say when prdb
published a Video, never when this installation came to hold it.

The counter-argument is that a title order is stable while a recency order
shifts under a person who is paging. It does not survive contact with the
mechanism: a newly filed Video inserts itself into an alphabetical list too, and
displaces every page after it just the same. What's New has lived with exactly
this since it existed.

Someone who knows which Video they want does not scan for it in either order.
They type it into the title filter, which sits directly above the grid.

## Why there is a choice at all

Catalogue Search is the only other surface offering an order to choose, so
adding one here makes two out of twenty-nine rather than joining a pattern.
That is deliberate: the Library is the only surface where two different orders
are *equally honest*. What's New is a feed and a feed is chronological. The
Release view has a ranking that makes a claim. The Library has two keys and
neither can speak for the other, which is the same reason ADR 0012 refused to
impose a single Site ordering on the Release view.

Four values, because both keys are worth having in both directions — the oldest
Library Entry first is how a person finds what has been sitting there longest.
Release Date is deliberately absent: it is a property of the Catalogue rather
than a statement about what is held, and it can be added later without breaking
anything.

## Why the title order gets no index

ADR 0025 measured that a trigram index on `catalogue_video` cost +119 % on the
most continuously written table in the schema, and
`CatalogueSchemaTests.No_normalised_column_is_indexed` has held that line since.
That measurement was taken for a `LIKE '%needle%'` lookup, which no B-tree can
serve at all; an `ORDER BY` is the case a B-tree does serve, so the number does
not transfer and the question had to be measured again.

It was, on a database built from this model, at 1,000,000 Catalogue Videos over
20,000 Library Entries, medians of seven warm runs:

| grid query | no index | with `(NormalisedTitle, Id)` |
|---|---|---|
| title order, first page | 199.3 ms | **5.3 ms** |
| title order, last page (417) | 233.1 ms | **1776.6 ms** |
| title order, Site filter | 5.8 ms | 5.5 ms |

The index does not merely fail to help. An ordered index scan makes
`catalogue_video` the driving table, so the query walks a million rows and
discards 99.9 % of them; the first page stops early and every deep page pays for
the whole walk. **The last page becomes 7.6× slower than with no index at all.**

The good half cannot be kept. Once the database carries statistics the planner
drops the index entirely and produces the same plan and the same time as if it
did not exist — 41.0 ms against 40.9 ms on the first page. What it costs
meanwhile is 12.7 % of the file and +71 % on inserts, +75 % on updates, on the
table that is written more than any other.

So ADR 0025's boundary stands, and it is not being reinterpreted to let this
through. It holds here for a second, independent reason.

## Why the filed order gets one

`library_entry(FiledAt, VideoId)` is used unconditionally, needs no statistics
to be chosen, and is covering — the sort disappears from the plan, and
`library_entry` drives the join instead of being joined into:

| grid query | no index | with `(FiledAt, VideoId)` |
|---|---|---|
| filed order, first page | 26.4 ms | **0.4 ms** |
| filed order, last page | 88.2 ms | **27.0 ms** |
| filed order, Site filter | 5.6 ms | 5.6 ms |

It costs 1.25 MiB at 20,000 entries — 0.4 % of the file — and no measurable
write time, because ADR 0026's Filing writes a `library_entry` row once and
never again. None of ADR 0025's argument applies: the column is not a
normalised one, the table is small, and it is nearly write-free.

Two things it does not fix, recorded so nobody looks for them later. A deep page
still joins every skipped entry, because an `OFFSET` cannot be pushed under an
inner join — that is the 27 ms. And the Site-filtered grid never touches either
index; it is driven by `IX_catalogue_video_SiteId` at 5.6 ms whatever else is
true.

## Considered options

**A fixed order with no control, chosen properly this time.** The honest
minimum, and the cheapest. Rejected because it answers the question by throwing
away one of two orders that are both correct, and because ADR 0012 already
declined to do that to the Release view.

**Keeping the title as the default and offering filed order as an option.**
Rejected because the default is the whole decision for almost everyone; an
option nobody finds is not an answer.

**Adding the title index and running `ANALYZE` only when it helps.** Attractive
for about as long as it takes to state: the two are mutually exclusive by
construction, since statistics are exactly what makes the planner reject the
index. There is no configuration in which both are working.

**Sorting by `Title` with a `NOCASE` collation instead of by the comparison
form.** Rejected because `NOCASE` folds ASCII only, which is a smaller
correction than the one already available for free — ADR 0025's comparison form
is on the row, required, lower-cased, and written by the same function
everywhere.

**Ordering in memory after materialising the page.** Rejected on sight: it
either sorts a page that has already been chosen by a different order, which is
not sorting, or it materialises the whole Library to sort it.

## Consequences

- `LibraryEntrySort` is an enum with four values, ordered in one place, mirroring
  what `CatalogueBrowse.Ordered` does for `CatalogueVideoSort`. The endpoint
  takes it with `FiledAtDescending` as the default, and the default is omitted
  from the address bar rather than written into it.
- The control is a fifth field in the Library's existing filter toolbar. It does
  not bring Catalogue Search's summary sentence with it: that surface has no
  lede and this one does, and *what this page is* is worth more than *what you
  are currently looking at*.
- Every order carries a tiebreak, so that two Library Entries cannot swap places
  between two requests for the same page. `FiledAt` ties on `VideoId`, the title
  on `Id`.
- The Library keeps sorting on `NormalisedTitle` rather than on `Title` when the
  title order is chosen. Accents are not folded — SQLite has no collation here
  that would, and this ADR does not pretend otherwise.
- Once the page query is measured in tenths of a millisecond, the unfiltered
  `COUNT` that every render performs — 14.4 ms at a million Catalogue rows — is
  the dominant cost of drawing the Library. That is the next thing worth
  looking at, and it is not another index on an order.
- Statistics matter to the title order and to nothing else here. That is
  [ADR 0056](0056-the-database-analyses-itself-at-startup-and-again-on-a-schedule.md)'s
  subject, not this one's.
