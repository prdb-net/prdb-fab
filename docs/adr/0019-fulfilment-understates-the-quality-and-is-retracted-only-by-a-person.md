# Fulfilment understates the quality and is retracted only by a person

A fulfilment report is a claim about a **library entry**, not about the file
that happened to arrive: it carries the highest prdb rung any file of that entry
actually clears, and never a higher one. It is withdrawn only when a person acts
inside the tool, never because the disk stopped answering. What has been
reported is remembered as a state per video **scoped to the prdb account** it
was reported to, which is what makes reporting idempotent without making it
impossible for the next account.

## The payload, and why so little of it

`POST /wanted-videos/fulfillments` offers five fields. Three are filled and two
are deliberately empty.

**Quality is rounded down, never up.** prdb's `VideoQuality` has three values —
`P720`, `P1080`, `P2160` — against the eight-rung ladder
[ADR 0011](0011-a-duplicate-is-decided-after-the-download-by-quality-label.md)
compares files by. Five of eight rungs cannot be expressed, so the channel is
lossy by construction and the loss always falls on the side of understatement:
the value sent is the highest prdb rung the file genuinely clears. `1440p`
reports `P1080`, `2160p` reports `P2160`, and anything below `720p` reports
`null` — which is not a workaround but the value the API models for *fulfilled,
quality not stated*, and which is exactly true of a `480p` file.

The direction matters because the wanted list is a shopping list. Someone
filtering for "fulfilled in 1080p" who in fact holds a `576p` file has been lied
to in the one direction that stops them replacing it. Understating costs at
worst a download the local duplicate check of ADR 0011 catches anyway.

**`fulfillmentByApp` is `Other`**, because the enum has no value for this tool.
It is never `Sabnzbd`: the field names the fulfilling *application*, and that
`Ordeno` sits in the same enum while downloading nothing at all settles that
reading. An own value is worth asking prdb for, and `Other` is the honest answer
until there is one.

**`fulfillmentExternalId` is empty.** Every candidate names a row prdb cannot
see — a local id, or an `nzo_id` in a SABnzbd the user may since have reset — and
`ADR 0009` would have to carry it through the backup unchanged for a benefit
nobody can state. One candidate is worse than useless: the `osHash`. `VISION.md`
makes fulfilment and confirmed hash assignments **two channels with two
switches**, and an `osHash` riding in a fulfilment report smuggles the second
channel's entire payload through the first.

**`fulfilledAtUtc` is ours, not the server's.** The API stamps its own time when
the field is absent, which would date a switched-on backlog of three hundred
entries to the same minute and destroy the only signal the field carries. The
real filing time is already on the library entry.

## The report is a function of the entry

Not of the arriving file. The value sent is the highest rung *any* file of the
entry clears, recomputed when the entry's set of files changes, and sent only
when the rung actually moves. prdb's coarse scale absorbs most churn for free —
a `1080p` entry gaining a `1440p` file does not move `P1080`, so nothing is
sent — and a replace under ADR 0011 can never move it, since a duplicate carries
the same label by definition.

The downward direction is reported too: leaving a `P2160` standing after the
file behind it is gone is the same lie the rounding rule refuses, merely arrived
at later.

## Retraction, and the disk that must not vote

`isFulfilled: false` clears the timestamp, the quality, the external id and the
app. It is sent for exactly two causes, and both are a person acting inside the
tool: the library entry is removed, or a misidentification is corrected and the
file refiled as a different video — where the retraction of the wrong video and
the report of the right one are one movement.

It is never sent because a filed path stopped resolving.
[ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md) chose
the careful side of this exact confusion when it made an unverified entry count
as held, and
[ADR 0017](0017-the-filed-path-is-computed-once-and-then-recorded-rather-than-recomputed.md)
says plainly that one `stat` cannot separate a tidy-up from a mount that did not
come up. A retraction rule reading the disk would, on a single bad mount, wipe
fulfilment marks across a whole library — remotely, and with no way back.

This is consistent rather than in tension with the downward report above.
Nothing scans the disk: ADR 0017 notices a missing file only while filing a new
copy, and only in its first case, where the scene directory is present and the
file inside it is gone. Its second case — the directory itself missing — files
nothing and reports nothing, becoming a review queue entry instead.

## Reported state, not an outbox

What has been sent is held as **the state prdb was last told**, one row per
held video, against the state it should be told. The pending set is then
computed rather than queued: held, locally wanted, desired ≠ reported, not
terminally closed.

An append-only outbox was the obvious alternative and is worse in the case that
matters. `ADR 0009` exports what was reported, so a restore replays it — and an
outbox replays stale intermediate states, while a last-known-state row can only
ever describe the end.

It also moves the eligibility test. The wanted list is fully local, so the check
is free, and running it at **send** time rather than at filing time means a
video that is filed before it is wanted gets reported for free once the wanted
feed says so, and a lagging local copy no longer loses a report permanently.
Only what the local copy calls wanted is ever sent: a report for anything else
burns one of fifty batch slots on a guaranteed `NotWanted`.

## Every outcome is terminal

`POST /wanted-videos/fulfillments` takes fifty entries and counts as **one**
request against the rate limit, which makes `PUT /wanted-videos/{videoId}` the
worse call even for a single entry. None of the four outcomes is a retry:

- **`Updated`** and **`Unchanged`** both mean prdb's copy now says what we say.
- **`NotWanted`** means the entry is gone or soft-deleted, and the API refuses
  to revive it. Never sent again — until the wanted feed reports the row alive
  again (`created` or `updated` with `isDeleted: false`), which clears the
  marker.
- **`NotFound`** means the video is gone from prdb. It never clears; ADR 0013's
  catalogue learns the same thing by its own route.

Only a transport failure retries, under
[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md)'s ordinary
backoff.

Filing writes nothing to prdb directly. It changes the desired state, and a
routine in the **sync** lane drains the difference at fifty per request every
fifteen minutes. That indirection is what makes the idempotence `VISION.md`
demands actually hold — a crash between prdb accepting and the tool recording
finds the row still there — and it keeps the cost under the governor. The
cadence buys latency only: with nothing pending the routine makes no request at
all, so it does not enter the idle budget.

## The account owns the record

**The reported state is scoped by `userHash`.** A key belonging to a different
prdb account therefore reports into an empty slate, and a return to the previous
account finds its own record intact.

This replaces the reasoning of
[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md),
which kept the record across an account change on the grounds that *not
re-reporting is the harmless outcome*. That was argued against an implicit
outbox, where the record only prevents sending something twice. Under a
last-known-state row the record is a **suppression key**, and keeping it
unscoped means the new account's wanted list is never served at all — the silent
permanent loss of the whole fulfilment loop, which is the opposite of harmless.
Both ADRs' conclusions survive verbatim: nothing is deleted, and the change is
confirmed rather than blocked.

## The switch, in both directions

Fulfilment ships on behind its own switch
([ADR 0007](0007-automation-is-a-set-of-permissions-over-the-wanted-list.md)).

**Turning it on sends the backlog**, through the same routine, with no special
path — but the count is named *before* the switch is thrown. "347 fulfilled
wanted videos will be reported to prdb" is `VISION.md`'s *stated plainly* at the
only moment it can change a decision; after the click it is a fact about the
past.

**Turning it off retracts nothing.** Off means stop sending, not undo. Both
readings are defensible, which is why the switch says which one it is — and the
destructive reading is the one that would surprise. The routine reads the switch
when it starts, so at most one batch of fifty follows the throw.

Reporting switched off with a non-empty pending set is a **Brake** under
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md), not
a Gap: the tool is working exactly as configured and therefore not acting. A
routine failing three times running is a Gap by ADR 0014's ordinary mechanism,
with no rule of its own.

## Consequences

- The payload is four facts — video, fulfilled, when, and a rung or *not
  stated* — plus a fixed application name. Short enough to print in the
  switch's own text rather than bury in documentation, which is what
  `VISION.md` asks for.
- The schema gains a reported-state row per video and account: the video, the
  `userHash`, whether prdb was told fulfilled, the quality rung last sent, the
  timestamp last sent, and a terminal marker for `NotWanted`/`NotFound`. It is
  exported, per ADR 0009. The desired state is not stored — it is read off the
  library entry and the local wanted list.
- The retraction rule is written here but one of its two triggers is not.
  Whether a misidentification can be corrected at all belongs to the review
  queue; if the first release has no reassignment, that trigger never fires and
  the rule still stands correctly.
- ADR 0010 and ADR 0013 are amended where they explain why the reported record
  survives an account change. Their conclusions are unchanged.

## Considered options

**Round the quality to the nearest rung.** Rejected: it sends `P720` for a
`576p` file, which is the overstatement the whole rule exists to prevent.

**Send no quality at all.** Rejected: the field is filled truthfully for three
of eight rungs, and those three are the ones most files land on.

**Report `Sabnzbd` as the application.** Rejected: it names the transport where
the field names the reporter, and `Ordeno`'s presence in the same enum shows
what the field is for.

**Put the `osHash` in the external id.** Rejected under *the payload*: it
crosses a channel boundary `VISION.md` drew on purpose.

**Retract when a filed path no longer resolves.** Rejected under *retraction*:
one failed mount would clear a library's worth of fulfilment marks remotely.

**An append-only outbox of pending reports.** Rejected under *reported state*:
it replays stale intermediate states through the backup.

**Send the report as part of filing.** Rejected: it puts an HTTP call on the
filing path, bypasses the governor, and loses the report on a crash between
accepting and recording — the exact failure `VISION.md` names.

**Drop the reported record on an account change.** Rejected in favour of
scoping: scoping satisfies ADR 0010's letter, costs one column, and serves the
user who moves back.
