# A setting exists where the tool cannot know the answer, and its form is the onboarding step

Everything editable after onboarding lives on eight routes under `/settings`,
and the connection forms among them are literally the onboarding steps, wrapped
differently. A value earns a place here only where the tool cannot read the
answer for itself, which is
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)'s rejection of
intervals stated as an admission rule rather than as a refusal. Every setting is
read fresh at each use and takes effect from the next one — with a single
exception, the library root, which is history rather than present because filed
paths are stored relative to it.

## What is allowed to be a setting

`VISION.md` fixes one end — the container is given only where its data lives,
which port and which user it runs as, so "changing an indexer key is a form, not
a YAML edit and a restart" — and ADR 0014 fixes the other by refusing to expose
a single interval. That refusal has a reason worth generalising: an interval
follows from a budget the tool reads off a response header, so exposing it
invites the user to break their own rate limit and then report it as a bug.

The rule that falls out is the admission test for this whole surface. **A
control exists where the answer lives outside anything the tool can observe.**
The rate limit is observable, so it is a fact on the Status page and not a
field. A Newznab quota is not — it belongs to the user's account, and three of
the five implementations surveyed report nothing about it at all — so it is a
field. How many usable releases a video has across *this* set of indexers is not
observable either, so the retry budget is a field. Applied consistently it
produces a short surface, and more usefully it settles future arguments about
adding to it without reopening this decision.

The eight groups:

| Route | Holds |
|---|---|
| **Connections** | the prdb key with its `userHash`; SABnzbd with URL, key, category and path mapping; the indexers as a list |
| **Identification** | the two confidence gates of [ADR 0006](0006-acting-alone-needs-a-named-video-and-an-allowed-confidence.md) |
| **Library** | the library root; the leftover deletion switch of [ADR 0005](0005-the-first-release-files-into-the-jellyfin-layout.md) |
| **Downloads** | the Preferred Download Quality ceiling of [ADR 0052](0052-catalogue-downloads-use-a-named-quality-ceiling.md) |
| **Automation** | the rules of [ADR 0007](0007-automation-is-a-set-of-permissions-over-the-wanted-list.md); the cap on unfinished automatic downloads; the retry budget |
| **Reporting** | the fulfilment switch of [ADR 0019](0019-fulfilment-understates-the-quality-and-is-retracted-only-by-a-person.md), and a named place for the second channel |
| **Account** | the password change of [ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md) |
| **Backup** | export, per [ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md) — restore stays in onboarding |

Routes rather than one long page with anchors, and the reason is
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)
rather than taste: every Gap carries a route to the form that fills it and every
Brake a route to the setting behind it. A Gap saying SABnzbd is missing has to
land on that form, not scroll a wall of everything else into view. The same
argument one level down gives **every rule and every indexer its own route**,
since the size-limit Brake belongs to one rule and the query-budget Brake to one
indexer — a route that lands on a list, leaving the user to find the row, gives
the argument back.

The two confidence gates get a group of their own rather than one each under
Automation and Library. ADR 0006 decided them as a pair — the gate before a
download must be the looser one, because that error costs only bandwidth — and
that relationship is unreadable when the two numbers sit on different pages.

## One form, two entry points

The connection forms are not written twice. SABnzbd, an indexer, prdb and the
library root are each **one** form that onboarding and settings wrap
differently: onboarding surrounds it with *skip* and *continue*, settings with
*save*. It is the shape
[ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)
chose for the release view, for the same reason — a field added later to one of
two implementations is a field missing from the other.

It also makes the verification question cheap. Editing a connection re-runs
ADR 0010's check with its four distinct verdicts, because it is the same code,
not because the check was rebuilt here. A correction verdict — a wrong key, a
tier without API access — refuses the save; a not-right-now verdict — `429`, a
`503`, a timeout — offers a retry. Nothing saves past a failure. ADR 0010
already rejected "warn and continue" for onboarding, and a **Gap** is the name
for something that broke while nobody was present: raising one while the user is
standing in front of the form only moves the discovery somewhere they can do
less about it.

The SABnzbd category is the one field this sharpens. It is **chosen from
SABnzbd's own list** rather than typed, read through `get_cats`, which is a read
and therefore leaves
[ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md)'s
*`addfile` is the only thing ever written to SABnzbd* intact. A typed category
SABnzbd does not know is not an error there — it falls back to the default, the
downloads land somewhere else, and the verified path mapping points at a
directory nothing ever arrives in. That also fixes the order within the form:
the category is answered before the mapping is verified, because it decides the
completed path ADR 0010 resolves.

Keys are **write-only**. The field is empty with a marker saying one is set, and
saving it empty means unchanged; nothing is returned to the browser, masked or
otherwise. `VISION.md` calls these the crown jewels, and the only use for a key
coming back is copying it somewhere else — for which prdb and the indexer are
the source.

## The present, and the one piece of history

A setting changes underneath work that is already running, and the useful split
is not between kinds of settings but between two ways a value is used.

**Present.** Every setting is read fresh at each use and never cached, and a run
that has already begun finishes under the value it began with. That is only
[ADR 0007](0007-automation-is-a-set-of-permissions-over-the-wanted-list.md)'s
*disabling a rule is forward-only* generalised to the surface. The **path
mapping** belongs here, which is worth saying because it looks like history and
is not: it translates where SABnzbd's paths land in *this* container right now,
so a download that went outstanding under the old mapping is correctly resolved
under the new one when it is collected.

**History.** The **library root** alone. ADR 0009 stores filed paths relative to
it, so changing it mid-filing produces a record pointing into a directory
nothing was ever written to. Changing it is therefore refused while the bulk
lane holds filing work, with the form saying so and asking for another attempt
in a moment. A pending setting — saved but not yet in force — would be a state
nothing else in the design has and every reader would have to know about; the
wait is a filing's, seconds to minutes, not a download's.

The change itself is a re-rooting and reuses the machinery restore already
needs: the prefix is replaced, **nothing on disk moves**, and the confirmation
says that in those words. Afterwards the same background verification pass runs,
under ADR 0009's rule that an unverified entry counts as **held** — which is
what keeps a mis-typed root from reading as an empty library and, under ADR
0007, becoming a standing instruction to download the collection again. There is
no separate field for the download directory: it follows from the path mapping,
as ADR 0010 decided.

## Deleting, and what survives it

`VISION.md` promises indexers can be disabled without being deleted. Both exist:
disabling is the ordinary, unconfirmed act, and deleting is confirmed, because a
cancelled subscription should not sit in the list forever.

Deleting an indexer discards its cache — ADR 0015 built it to be disposable —
and **leaves the downloads standing**, since the download row *is* ADR 0016's
consumed state, is exported by ADR 0009, and answers "why is this on my disk"
about a file that exists whether or not the subscription does. The part that
needs deciding rather than following is the rule: a rule whose last permitted
indexer disappears permits nothing, silently. So the confirmation names the
rules that reference it, and a rule left with no indexer is **disabled rather
than left inert** — a disabled rule is visible on the Status page as a Brake,
and an inert one is exactly the silent failure ADR 0018 exists to prevent.

Rules follow the same structure, which is how ADR 0007 can say both that rules
are disabled rather than deleted and that provenance survives a deleted rule: a
download keeps every applicable rule's **name** as it read at submission, and a
reference for as long as the rule still exists. Deletion clears the live link
and never the copied name
([ADR 0046](0046-an-automatic-origin-is-every-rule-that-permitted-the-download.md)).

## The four controls

**The confidence gates** are a choice among pre-built named sets, each labelled
with the confidences it contains verbatim — `Exact` only, or `Exact` and
`Strong` (the default) after a download; those two plus `Probable` (the default)
before one. ADR 0006 is emphatic that this is set membership and not an order,
so a slider would be a lie about the structure, and free checkboxes would permit
`{Exact, Probable}` without `Strong`. Fixed sets make the floor at `Probable`
structural instead of validated, and make it impossible for `Ambiguous` — which
sorts numerically above `Exact` while meaning the opposite — to be selected at
all.

**The retry budget** is a number, default 3. Five attempts are absurd against
one indexer and three are thin against four, and the tool cannot see which case
it is in. ADR 0018's spent-budget Brake keeps its resume action beside this:
raising the number affects every video, resetting a budget affects one.

**The daily query budget** counts **HTTP requests** to one indexer, because that
is the unit an indexer counts itself. Its default is **empty**, meaning
unbounded — inventing a number for a quota the tool cannot see is the mistake
ADR 0014 avoided with the intervals. The window is **UTC midnight** and the form
says so, since anyone setting this against their indexer's quota needs to know
whether the two windows line up. ADR 0014 fixed an order of precedence for prdb
scarcity but none for indexers; this adds it. When the budget runs short the
**indexer walk** yields before the **wanted sweep**, because the walk sees only
what is new and what is new will be there tomorrow, while ADR 0014 makes the
sweep the only route by which an older wanted video is ever found at all.

**Reporting** carries two switches, not one. ADR 0019 decided the first and
warned explicitly that its text must not read as covering the second; building
one switch now would mean adding the second later beside a control users have
already learnt. What the hash-assignment channel sends is ticket 17's, since the
review queue produces it — here it is a named place. The fulfilment switch names
the count of videos that would be sent **before** it is thrown, which ADR 0019
requires because `VISION.md`'s *stated plainly* can only change a decision
beforehand: held wanted videos with no reported-state row under the current
`userHash`. Turning it off states in one sentence, without a dialog, that what
was already reported stays at prdb and is retracted only by a person. The same
count belongs in the confirmation ADR 0010 already demands when a key for a
different prdb account is entered, where the new account starts with the whole
backlog.

## Considered options

**A global automation switch above the rules.** Rejected. A rule is a
permission, so "no enabled rule" already *is* the off state, and a second switch
is a second answer to "why is nothing downloading" that ADR 0018 would have to
carry as its own Brake. Worse, ADR 0007 makes disabling forward-only: a master
switch that also fails to stop anything in flight promises more by its name than
it does, and it would be pressed precisely by someone who believes it stops
something.

**One long settings page with anchors.** Rejected: see above — an anchor
scrolls, but it brings the rest of the page with it, and every Gap and Brake
route lands the user in a surface they then have to read past.

**Returning a stored key, in clear or masked.** Rejected. Masking to the last
few characters buys recognition, which is a problem for someone holding many
keys per service; there is one user with one key per service.

**A fixed library root, with a move going through backup and restore.**
Rejected. Remounting a NAS is ordinary, and routing it through an export and a
restore into an empty container is a heavy path for a prefix change — especially
when ADR 0009 already built exactly the re-rooting the change needs.

**Intervals, and "run now" here.** Intervals stay rejected by ADR 0014. "Run
now" lives on the **Status** page beside the routine it belongs to: there is no
list of routines in settings, since none of their cadences is a setting, and
inventing one to host a button would be a surface for a control that does not
exist. Whoever presses it does so because the Status page just showed them
something stalled. It is a deliberate exception to ADR 0018's *refreshing never
causes work*, and it is safe only because ADR 0014 has it set the due time and
nothing else, so a forced run still passes the governor.

**The indexer rank as a typed number.** Rejected in favour of the list position.
ADR 0008 needs a total order and nothing more, and a number field is one where
gaps and duplicates appear and then need a resolution rule nobody asked for.

## Consequences

- **Every rule and every indexer needs a stable, addressable route**, because
  ADR 0018's Brakes point at one row rather than at a list.
- **The schema gains only values, not shape**: the two gate sets, the retry
  budget, the automation cap, the leftover switch and the reporting switches as
  installation-wide settings, plus a rank and a daily query budget on the
  indexer row. All of it is exported — it is configuration, which ADR 0009's
  test admits by definition.
- **`get_cats` is added to the SABnzbd client**, and it is a read. ADR 0016's
  restriction is about writes and is untouched, but the sentence "`addfile` is
  the only thing ever written" now needs its verb read carefully.
- **ADR 0014's precedence order is extended to the indexers**: under a short
  daily query budget the walk yields to the sweep.
- **A rule left with no permitted indexer becomes disabled**, so removing an
  indexer can visibly stop automation for a video — which is the intent, since
  the alternative is a rule that permits nothing while presenting as active.
- **Ticket 17 inherits the second reporting channel** with its place already
  built and its name already fixed.
- **`VISION.md` is not amended.** Its automation paragraph predates ADR 0007 and
  still names site and actor scopes that are out of scope, but that drift is
  ADR 0007's rather than this decision's; "automation is off until the user
  turns it on" stays true, with enabling a rule being the act that turns it on.
- **`CONTEXT.md` gains two terms**: **Connection**, which ADR 0010 and ADR 0018
  both lean on without ever naming, and **Daily Query Budget**, which needs a
  name because the tool now has three budgets — prdb's rate limit, the retry
  budget per video, and this one.
