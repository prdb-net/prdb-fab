# A preview is published only after it has been explained, and an uncertain upload is never sent twice

[ADR 0061](0061-a-user-preview-belongs-to-one-file-and-is-served-only-while-prdb-still-shows-it.md)
settled what may be *asked* of prdb about a User Preview. This settles the other
direction: this tool holds the Video Files, it already knows each one's osHash
and measured Runtime, and it ships ffmpeg — so it can make the Sprite Sheet that
prdb's population is short of, and submit it.

Publishing is the first thing this tool does that puts content it produced in
front of strangers. Everything below follows from that one sentence: the switch
is explained before it acts rather than after, the payload is enumerated rather
than described, and an upload whose outcome is unknown is left alone rather than
tried again.

## The third Reporting channel, and the explanation that gates it

**'Publish generated previews to prdb' is a third independent switch under
Settings → Reporting, and it ships on**, beside the two
[ADR 0051](0051-both-reporting-channels-ship-on.md) already put there. The
argument for the default is the one that decision made and
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)'s
admission test confirms: whether somebody wants their previews published is not
observable from anything here, so it is a control; and a preview made from a
file this installation holds is exactly the fact this tool is positioned to know
and prdb has no other way of learning.

Where this channel is not like the other two is what a mistake costs. A
Fulfilment says *somebody holds this Video*, and a Confirmed Assignment says
*this hash is that Video*; both are statements, both are small, and neither is
shown to anybody but prdb's own machinery. A published preview is **a picture,
in public, under moderation, with no retraction**. That asymmetry is the whole
of the difference, and it is what the gate below is for.

**Nothing is published until the explanation has been in front of a person.**
`Installation.PreviewPublicationExplainedAt` is null until it has, and while it
is null no preview is generated for publication and nothing is uploaded. It is a
stamp rather than a flag for
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)'s
reason: *since when* is what makes the state readable, and an installation
sitting unexplained for a fortnight is a different sentence from one that has
been up for a minute.

The gate is a **Brake and not a Gap**. Nothing is broken — the tool is working
exactly as configured, deliberately not acting — which is the distinction
ADR 0018 drew and this is a clean instance of it.

### One form, two entry points

The explanation and the switch are one form, and it is reached two ways, which
is ADR 0020's shape for the connection forms applied to the one setting that
needs it:

- **A fresh installation** meets it as an onboarding step. `Publishing` is added
  to [ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)'s
  path after `LibraryRoot`, and it is **not skippable** — not because something
  breaks without it, but because *skipping* is the one answer the step must not
  accept silently. Answering it either way is what sets the stamp.
- **An installation that already exists** meets it in Settings → Reporting,
  where saving sets the same stamp. Its switch is on, as a fresh one's is, and
  its stamp is null, so it publishes nothing until somebody has been there. The
  shipped default is not reinterpreted and a decision already stored is not
  overwritten — ADR 0051's rule, unchanged — because there is no stored decision
  about this channel to reinterpret.

This is deliberately **not** an announcement banner over every surface. A
one-off interstitial would be a new class of surface with its own dismissal
state, its own place in the routing, and its own argument about what a refresh
does to it; the stamp plus a Brake reuses three things that exist.

### Onboarding gains a step that is not a Connection

Every step ADR 0010 has is a Connection, and this one is not, so it is worth
saying why it belongs there rather than being left to the settings route alone.
The path's job is to reach a working installation, and the first Video File a
fresh installation files is eligible work — so an installation that finishes
onboarding without having been told would publish before anybody had the chance
to say no. The step exists because the loop starts immediately after it, not
because the value is a connection.

`OnboardingStep` is stored as a string
(`FabDbContext`), so inserting a member before `Complete` moves nothing that is
already written.

## What is eligible, and what is not

**Newly filed, identified Video Files, and nothing else automatically.** A file
becomes eligible at Filing, which is where a Video id and an osHash are both
already recorded, and it includes a file filed after a person confirmed its
assignment in the Review Queue — that is a filed identified file like any other.

What is outside the automatic scope, each for its own reason:

- **The existing Library.** An installation with five thousand entries would
  otherwise begin a five-thousand-file decode and a five-thousand-item upload
  queue on the day it upgraded, having been told once. It needs an explicit
  bounded request, which the Library backfill answers, and turning the switch
  off and on again is **not** that request — re-enabling selects nothing that
  was not already waiting.
- **Unidentified files.** The API accepts an upload with no `VideoId`, grouped
  by hash alone, and this tool does not use that shape. A row nobody can link is
  a row nobody benefits from, and it would publish a picture of a file this
  installation could not itself name.
- **A file whose bytes changed.** Eligibility is a claim about one osHash. If
  the file at the recorded path no longer hashes to it — Replacing did its work,
  or somebody edited it — the intent is dropped rather than published against
  the wrong hash.

## What is generated, and the numbers it is bounded by

One deterministic output per eligible file, and *deterministic* is load-bearing:
the same file at the same output version produces the same sheet, so a rerun
after a crash is the same bytes rather than a second picture.

| | |
| --- | --- |
| Output version | 1 |
| Tile | 320 × 180, the Review contact sheet's size |
| Seconds per tile | 10 |
| Tiles | at least 24, at most `UserPreviewContract.Tiles` (400) |
| Grid | `ceil(sqrt(tiles))` columns, so the sheet stays near-square |
| JPEG quality | `-q:v 4` |
| Sheet ceiling | `UserPreviewContract.ASprite` (8 MiB) |
| WebVTT ceiling | `UserPreviewContract.AVtt` (256 KiB) |
| Generation | one decode at a time, `Bulk` lane, 5 min cadence |
| Waiting publications | at most 50 generated and unsent |
| Intent expiry | 30 days |

The tile count follows from the Runtime already recorded by the Probe:
`clamp(ceil(runtime / 10 s), 24, 400)`, and the interval is `runtime / tiles`.
A twenty-minute scene gets 120 tiles ten seconds apart; a two-hour one gets 400,
eighteen seconds apart; a three-minute one gets 24, seven seconds apart. Nothing
reads the file to work this out — ADR 0021's rule holds, and the amendment
ADR 0061 made to it is the one that applies: *the stored value is read again,
the file is not*, and the later decode is a decode for a new purpose rather than
a second Probe.

**The ceilings are the read contract's, not new ones.** ADR 0061 already bounds
what may be *taken* from prdb at 8 MiB and 400 tiles; producing something this
tool would itself refuse to display would be the clearest possible sign the
number was wrong. So the publication contract reads those constants rather than
restating them.

**The sheet ceiling was measured rather than assumed.** A 400-tile sheet at
320 × 180 and `-q:v 4` is 6400 × 3600 pixels and weighs 1.5 – 2.2 MiB across
flat, detailed and noisy sources — roughly a quarter of the ceiling. The near-
square grid is what keeps it from being 3200 × 7200, which is the same bytes in
a shape some decoders handle worse.

**Generation is never part of Filing or of the Probe.** ADR 0021 reads a file
once, synchronously, at collecting, and nothing here changes that: this is a
later decode of a filed file, in the `Bulk` lane, for a purpose that has nothing
to do with deciding anything about the file. Filing does not wait for it and
never fails because of it.

**The generated bytes are disposable and the queue is bounded by count.** They
live under `publications/`, they are deleted the moment an upload reaches a
final outcome, and generation stops while fifty of them are waiting. A count
rather than a byte ceiling because every waiting file is waiting for its own
upload: evicting the oldest would mean re-deciding which obligation to abandon,
and stopping means the backlog drains first. The stop is a Brake.

## Exactly what leaves

`POST /video-user-images`, `multipart/form-data`, and these parts and no others:

| Part | Value |
| --- | --- |
| `File` | the generated JPEG, sent as `preview.jpg` |
| `VttFile` | the generated WebVTT, sent as `preview.vtt` |
| `VideoId` | the prdb Video id the file is filed under |
| `BasedOnFileWithOsHash` | the osHash recorded by the Probe, 16 hex characters |
| `PreviewImageType` | `SpriteSheet` |
| `DisplayOrder` | `0` |

The **source Video File never leaves**, and neither does anything that names it.
The multipart filenames are fixed strings rather than the file's own, because a
part name is the one field of a multipart body that carries a local name by
accident. No path, no original filename, no Release name, no audio, no probe
fields, no account secret beyond the `X-Api-Key` the request authenticates with,
and no frame not already in the sheet. The WebVTT carries timestamps and tile
coordinates; it names no file.

The privacy document states this list as it states the other two channels', and
the Reporting form shows it beside the switch in the same folded shape ADR 0051's
two use.

## Where an upload stands in the order of precedence

**`PrdbWork` gains `Publications`, between `UserPreviews` and `Repair`, held
back at 40 %** — and this *amends ADR 0061*, which wrote uploads down as
`Writes` before there was anything to weigh.

`Writes` at 5 % is sized for what is in it: a Fulfilment report and a hash
submission, both small, both rare, both a queued obligation that finishes in one
request. A publication is a queued obligation too, which is what ADR 0061 saw,
but it is not the same animal — it carries megabytes, it takes seconds on the
wire rather than milliseconds, and after a backfill there can be thousands of
them. Left in `Writes` a backlog would sit inside the reserve that exists to
keep the small obligations moving, and Fulfilments would drain behind it for as
long as it lasted. Identification and Verification are untouchable at zero and
would still go; everything a person notices would not.

So it goes below every feed, for ADR 0061's own argument about background work
nothing is waiting on: a preview published an hour late costs nothing at all. It
stays above `Repair`, which spends only what is left above half the limit.

The pacing beside the reserve: **one upload per run, `Bulk` lane, 60 s cadence**,
a routine with a work set (ADR 0032) so an installation with nothing to publish
spends nothing. `IdleProfile.RequestsAnHour` does not move.

## An ambiguous outcome, and why nothing is retried into one

The public contract **exposes no idempotency key**. `POST /video-user-images`
takes the multipart body above and nothing that would let prdb recognise a
second copy of the same submission as the same submission. That is a fact about
the API rather than a gap in this design, and every rule here follows from
refusing to invent a guarantee around it.

An upload ends in one of five states:

- **Sent.** `201`, carrying `videoUserImageId`, `moderationTargetId` and the
  two moderation strings. The id is kept. The bytes are dropped.
- **Refused.** `400`, `403`, `404` or `409` — prdb considered the request and
  declined it. Final, recorded with the status, and **never regenerated or
  resubmitted**. A `409` is treated as *this already exists* rather than as a
  transient collision, which is the reading that is safe whichever it means.
- **Deferred.** The governor said no, or prdb answered `429` or `503`. **Not an
  outcome at all**: nothing was sent, the row is untouched, and the routine
  comes back. This is ADR 0014's deferral, unchanged.
- **Uncertain.** The request left and no answer came back that says what became
  of it: a timeout, a dropped connection, a `500`, a restart mid-flight. It may
  have been accepted and it may not, and **nothing here can find out**.
- **Dropped.** The file is gone, its hash no longer matches, the switch was
  turned off, the account changed, or the intent expired. Nothing was sent.

**An uncertain upload is never retried automatically.** The argument is short
and it is the reason this state exists rather than a retry counter: the three
read endpoints return only *publicly visible* rows, and a submission that has
just arrived is in moderation and therefore not publicly visible. So a preview
this installation submitted a minute ago is absent from every endpoint that
could be asked about it, whether it was accepted or not. **Absence proves
nothing**, which means duplicate-free reconciliation cannot be proven, which
means a retry is a coin-flip between one picture and two.

What happens instead: the row keeps `Uncertain` with the reason and the time,
the bytes are kept so that a decision is still possible, and it is visible —
on the Status page as a Brake, and in the publication surface the Library
backfill brings with it. Sending it again is a person's act, taken knowing what
it risks. The alternatives were weighed below.

A **later** snapshot can settle it, and settling it is welcome where it happens:
once the row becomes publicly visible, ADR 0061's existing per-Video read brings
it back with `basedOnFileWithOsHash` on it, and a preview carrying this
installation's hash for that Video is proof enough that the submission landed.
That is an opportunistic resolution rather than a mechanism — it depends on
moderation, which has no promised timescale — so nothing waits on it and the
uncertain state is not designed around it.

**This is written down as something askable.** `docs/prdb-api-proposals.md`
gains the request: an idempotency key on the submission, or an endpoint that
lists the caller's own submissions including ones not yet visible. Either would
turn every paragraph above into a retry. Sending the proposal is a separate act.

## Accepted is not approved, and approved is not permanent

The `201` carries `moderationStatus` and `moderationVisibility`, and they are
the **entry state of a moderation process**, not a verdict. ADR 0061 already
established that these are open strings with no enumeration, so nothing here
parses them: they are recorded as the signature the submission started under,
for the same reason ADR 0061 keeps one — so that a later change is recognisable
as a change.

- **A successful submission is not public visibility.** The surface says
  *submitted*, never *published*, until a read endpoint has returned the row.
- **A denial is an end.** It does not regenerate, it does not resubmit, and it
  does not make the file eligible again. Whatever a moderator objected to is not
  something a second identical upload answers.
- **A withdrawal is not a reason to publish again** either. ADR 0061's
  lifecycle covers what happens to the row locally; nothing about it feeds back
  into this direction.

## Account changes, and the rest of what has to be said

- **Intent and history are account-scoped**, keyed on `userHash` exactly as
  Fulfilments are. Changing the prdb key does **not** carry pending uploads over
  to the new account: they are dropped, and the new account starts having
  published nothing. Silently sending one account's queue under another's key is
  the failure this prevents.
- **Turning the switch off stops generation and stops unsent uploads.** It
  retracts nothing: a publication prdb accepted stays there, and this switch has
  no power over it. The form says that in those words.
- **An intent expires after 30 days untouched.** Shorter than the Recent
  Window's ninety and longer than any plausible outage — a month-old unsent
  publication belongs to an installation that was off, and the file it describes
  has had a month to change.
- **A missing source file** at generation time drops the intent. It is not a
  Gap: a file that left the Library is the Library's business and is already
  reported there.

## What is durable, and what a Restore finds

[ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md)
runs the export boundary between tables, and this direction is mostly on the
exported side — which is the opposite of ADR 0061's population, for a reason
worth naming: everything there was a copy of something prdb publishes, and
everything here is a claim only this installation holds.

- **The switch and the stamp — exported.** Configuration, which ADR 0009 admits
  by definition. A Restore therefore arrives already explained, because the
  person it was explained to is the person restoring.
- **The publication row — exported**, when delivery builds it: which Video and
  which osHash were submitted, under which `userHash`, with what outcome and
  which id. It cannot be fetched again — a submission in moderation is invisible
  to every read endpoint — and without it a Restore would republish the entire
  Library's worth of previously sent previews as though none had ever been sent.
  This is the same argument ADR 0017 made for recording the filed path.
- **The generated bytes — not exported.** Disposable by construction, and
  regenerable from the file and the output version.

A document written before this existed restores with the channel on and the
stamp **null**, which puts it in the same position as an installation that
upgraded: explained before it publishes anything. The backup format is raised to
**3** for exactly that reason — defaulting an absent boolean would make it
`false`, which is the one value that is neither the shipped default nor a
decision anybody took.

## What this amends

**[ADR 0051](0051-both-reporting-channels-ship-on.md)** becomes three channels
rather than two. Its argument is unchanged and is the one used above — an
explained opt-out beats a useful report left silently local — and its rule that
existing installations retain their saved choices is respected by there being no
saved choice about this channel to retain. What it gains is the clause the first
two did not need: a channel whose payload is public is explained before it acts.

**[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)**
gains a third switch in the Reporting group, and its *one form, two entry
points* now covers something that is not a Connection. The admission test is
unchanged and this passes it: whether somebody wants their previews published is
not observable from anything here.

**[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)**'s
path gains a step, its first since it was written, and the first that
configures no Connection. Its shape is untouched — mandatory or skippable,
answered in two writes, a closed tab costs nothing — and *going back is not
offered* still holds, because the settings route is where a decision is changed.

**[ADR 0061](0061-a-user-preview-belongs-to-one-file-and-is-served-only-while-prdb-still-shows-it.md)**
put uploads in `Writes` before there was one to weigh; they are `Publications`
at 40 %. Everything else it decided about this population stands, and its
ceilings are read by the publication contract rather than restated.

**[ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)**
is untouched and is worth saying so about, because generation opens a filed file
again. It is a later decode for a new purpose, not a second Probe: nothing is
re-measured, nothing is re-hashed, and nothing read from the file decides
anything. The interval comes from the stored Runtime, which is ADR 0061's
amendment to it applied again.

**[ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md)**'s
document gains two installation fields and the format is raised to 3.

## Considered options

**Retry an uncertain upload once.** Rejected, and it is the closest call here.
The appeal is real: most ambiguous outcomes are failures, so most retries would
be correct and the queue would drain without a person. But *most* is the whole
problem. The cost of being wrong is a duplicate picture in a public gallery,
under somebody's account, that this tool cannot delete — the API has no
retraction — and the cost of being right is a row that a person clears in one
click. An asymmetry that steep decides itself.

**Wait for the read endpoints to settle an uncertain upload, and retry if it
stays absent.** Rejected because the premise is false, and it is worth writing
down since it is the design everybody reaches for first. Absence from those
endpoints is the ordinary state of a submission in moderation, so a timeout
would have to be a guess at how long moderation takes — a number nobody here
can see, which is ADR 0020's test failing.

**Put uploads in `Writes`, as ADR 0061 wrote.** Rejected above: the reserve is
sized for small rare obligations and a publication backlog would occupy it.
ADR 0061 wrote that line before anything was weighed, and said so — it was
written *so that nobody would insert a kind later*, and this is the ADR
correcting it rather than an implementer quietly choosing something else.

**Publish unlinked previews for unidentified files.** Rejected. The API allows
it and it is tempting, because an unidentified file is exactly the one whose
hash somebody might later link. But it publishes a picture of a file this
installation cannot name, to a population ADR 0061 already established cannot be
queried usefully by hash, and it puts moderation work in front of a row nobody
asked for.

**Send a still rather than a sheet where a Runtime is missing.** Rejected. A
`null` Runtime is a file no interval can be computed for, and a single frame
from an unknown position is a weaker picture than none. The file is not
eligible, and it becomes eligible if a Runtime is ever recorded for it.

**A dialog confirming each publication.** Rejected: it turns a background
channel into an interruption, and ADR 0051 already settled that an explained
opt-out is the right shape for Reporting.

**Ship the channel off and let people find it.** Rejected — shipping it on was
asked for explicitly when this work was scoped, and the gate above is what makes
that defensible. A switch nobody finds is not consent either way.

## Consequences

- **`CONTEXT.md` gains one word**: *Preview Publication*.
- **`PrdbWork` gains `Publications`** between `UserPreviews` and `Repair`, and
  `PrdbBudget` a 40 % reserve for it. ADR 0061's sentence putting uploads in
  `Writes` is amended.
- **`OnboardingStep` gains `Publishing`** after `LibraryRoot`, mandatory, and
  the path's length changes for the first time since ADR 0010.
- **`InstallationRow` gains two columns**, both exported: the switch and the
  stamp.
- **The Backup format is raised to 3**, with an upgrade step that supplies the
  shipped default and a null stamp.
- **A third Brake** on the Status page: publication waiting to be explained, and
  later a full generation queue and an uncertain upload.
- **`docs/privacy.md` gains the third channel**, with the payload enumerated.
- **`docs/prdb-api-proposals.md` gains a second entry**: an idempotency key, or
  a way to see one's own submissions before moderation makes them visible.
- **Generation and delivery inherit their numbers** from
  `Core/Sync/PreviewPublicationContract.cs` rather than from this prose, on the
  same rule ADR 0061 set: a contract written twice is one that will disagree
  with itself.
