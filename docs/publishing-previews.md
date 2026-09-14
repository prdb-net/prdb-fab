# Publishing previews to prdb

prdb shows pictures its users made from their own files. This installation holds
files and bundles ffmpeg, so it can make those pictures and submit them. That is
the third Reporting channel, and it is the only thing this tool sends that
strangers see.

What each submission contains, to the field, is in
[privacy.md](privacy.md#published-previews). Why it works the way it does is
[ADR 0064](adr/0064-a-preview-is-published-only-after-it-has-been-explained-and-an-uncertain-upload-is-never-sent-twice.md).
This page is what the feature does and where its edges are.

## Nothing is published before you have read what it publishes

The switch is **Settings → Reporting → Published previews** and it ships on.
Until the form has been saved once, nothing is generated and nothing is sent —
whichever way the switch is left. Setting up a fresh installation asks the
question as its last step; an installation that was already running finds it in
Settings, and Status carries a Brake saying so until somebody has been there.

Saving the form is the answer. Turning the switch off is as complete an answer
as leaving it on.

## What is published automatically

**Video Files filed from now on, and identified by prdb.** A file becomes
eligible at Filing, which is where a Video id and an osHash are both already
recorded — including a file filed after somebody confirmed its assignment by
hand in the Review Queue.

Nothing else is automatic. In particular:

- **The Library you already hold** is published only by asking (below). Enabling
  the switch does not select it, upgrading does not, and restoring a Backup does
  not.
- **Unidentified files** are never published. prdb's API would accept a picture
  grouped by hash alone; this tool does not send one, because a row nobody can
  link is a row nobody benefits from.
- **A file with no measured runtime** is not eligible. The tile interval is
  computed from the runtime the probe recorded, and a single frame from an
  unknown position is a weaker picture than none.
- **A file whose bytes changed** since it was filed is passed over. Eligibility
  is a claim about one osHash, and publishing against the wrong one would be a
  picture of one file submitted as another.

## Publishing the Library you already hold

**Settings → Reporting → Published previews** shows how many files the Library
already holds that could have a preview, and what publishing them sends. The
request starts only from there.

It then takes up one file at a time in the background, well behind everything a
person is waiting for: a file filed while a request is draining has its own
preview generated and submitted first, whatever the queue looks like. Pausing
holds the work and gives nothing up. Cancelling gives up what has not been
sent — the files still waiting and the pairs already made — and takes back
nothing that has.

Asking twice is safe and asking again after a cancellation is safe: what is left
to take up is worked out from the publication history, so a file that was
submitted, declined or given up on is never offered a second time.

## Submitted is not published

prdb's `201` is the entry state of a moderation process. This tool says
**submitted** until a read endpoint has returned the row, and only then does the
page say it is publicly shown. A moderator declining a submission is the end of
it: the picture is discarded, and nothing regenerates or resubmits, because a
second identical upload does not answer whatever was objected to.

**prdb offers no retraction** for a submission it has accepted. Turning the
switch off stops future publications and unsent ones; it has no power over
anything already there.

## Uploads nobody can account for

If a request leaves and no answer comes back — a timeout, a dropped connection,
a container stopped mid-upload — this tool cannot find out whether it arrived.
prdb's read endpoints return publicly visible rows only, and a submission in
moderation is not one, so its absence proves nothing.

Such an upload is recorded as uncertain, its picture is kept, and it is **never
sent again on its own**. It appears as a Brake on Status and as a row on the
Published previews page with two buttons:

- **Send it again** — the only thing that publishes it if it never arrived, and
  a duplicate in a public gallery if it did. There is no retraction for either
  copy.
- **Leave it as it is** — the picture is discarded and the file is never offered
  again.

Where prdb later shows a picture made from that same file, the doubt settles
itself and the upload is recorded as having arrived. Nothing waits on that:
moderation promises no timescale.

## The numbers

| | |
| --- | --- |
| Tile | 320 × 180 |
| Tiles | `runtime / 10 s`, at least 24 and at most 400 |
| Grid | near-square, so a full sheet is 6400 × 3600 |
| Sheet ceiling | 8 MiB — a full one measures 1.5 – 2.7 MiB |
| Generation | one file at a time, every 5 minutes, or every minute while a Library request is running |
| Upload | one per minute, from a reserved share of the prdb request budget |
| Waiting | at most 50 generated and unsent; generation stops until they drain |
| Intent expiry | 30 days |

Generation is a background decode of a file that has already been filed. It
never happens inside a request, never while a download is being collected or
filed, and never delays either.

## Changing your prdb key

Intent and history are scoped to the account that made them. Changing the key
drops whatever was waiting to be sent rather than sending one account's queue
under another's, cancels an open Library request, and leaves the new account
having published nothing. What was already accepted stays at prdb, under the
account that sent it.

## Backup and Restore

The publication history crosses into the Backup, because it cannot be fetched
again: a submission in moderation is invisible to every endpoint prdb offers,
and without the history a Restore would republish a whole Library's worth of
previews as though none had ever been sent.

The generated pictures do not cross — they are remade from the file — and
**neither does a Library request**. A Restore arrives having published exactly
what the document says it published, with nobody's request outstanding. The
Library a document is restored onto may not be the Library it was written from,
so asking again is a person's act, as it was the first time.
