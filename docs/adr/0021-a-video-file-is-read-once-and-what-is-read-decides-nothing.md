# A video file is read once, and what is read decides nothing

`ffprobe` already opens every arriving video file to read its quality
(ADR 0005), and the same header answers a dozen other questions for the same
cost. What is kept out of that answer is decided by one rule: **a field is
stored only where the first release has a named reader for it** — a surface that
displays it, or a decision that reads it. A field nobody displays and nothing
decides on is a column that will be wrong for years before anyone notices.

Four fields clear that bar. Six do not.

## The admission rule, and why it can afford to be strict

ADR 0020 admitted a setting only where the answer lives outside anything the
tool can observe, and the surface became arguable rather than a matter of taste.
The same shape works here, and the obvious objection to it is real: the header
read costs the same whether it returns two fields or twelve, so the expensive
thing is not reading a field but **adding one later**, over a library that has
grown in the meantime.

That objection is answered by ADR 0014 rather than by generosity. A routine
already carries its own resumable position, its own lane and its own record of
the last success, and ADR 0015 already established one-shot routines with named
causes. Adding a field is therefore a one-shot routine that walks the library
and fills the column in — a known pattern, throttled, restartable, and not a
migration step. Strictness costs nothing once the way back is cheap, and it is
cheap here.

The library-wide re-read is the price, and it is paid deliberately: the
alternative is a schema carrying nine fields on the chance that one of them
finds a reader.

## What is read

**Runtime.** ADR 0012 puts it on the entry page and in the card's hover overlay,
so it had a reader before this decision. The stronger one is the review queue:
ADR 0011 shows the arriving file's quality and size against the filed file's,
and a runtime beside them is what separates a better encode from a five-minute
sample of the same scene. Read from `format=duration`, kept as whole seconds.

**Width and height.** The label is computed from them, but that is not why they
are kept — the entry page shows them, and `3840×1600` beside `2160p` explains a
scope encode that the label alone makes look like a mistake. `1920×1080` beside
`1080p` says nothing, and costs nothing to have said.

**Video codec.** The one contested field, and it earns its place from a sentence
in ADR 0011: *a user who wants the 4 GB encode over the 900 MB one is shown both
figures side by side and picks*. If the 900 MB file is HEVC, that is a different
decision than if it is h264 — the gap between the two numbers explains itself.
Without the codec the queue puts two figures side by side and withholds why they
differ. A short name (`h264`, `hevc`, `av1`) and nothing else.

**The `osHash`**, in the same pass. It is not an `ffprobe` field, but this is the
one place a video file is read, and ADR 0011 needs it for the *same bytes*
outcome. `VISION.md` puts it in the first release for exactly this reason: 64 KiB
from each end, free even on a large library, unlike the twenty-five frame decode
a perceptual hash costs.

## What is not read

- **Container** — it is in the filed path, which ADR 0017 already stores. A
  column for a substring.
- **Bitrate** — size divided by runtime, both of which are stored.
- **Audio codec and channels** — no reader. Surround is not a thing scenes are
  released in, and no surface has a place to put it.
- **Frame rate** — no display, no decision.
- **HDR flag** — it would be a quality dimension, and ADR 0011 fixed the ladder
  without one. A field that exists to be a gate, in a pass that has none.
- **Audio and subtitle languages** — Jellyfin reads the streams itself. Writing
  them into the sidecar would be a duplicate of what the file already says,
  which can go stale while the file cannot.

## The pass decides nothing

Every field here is **descriptive**. None of them may enter the release ranking,
which ADR 0008 closed, or the duplicate comparison, which ADR 0011 fixed as the
quality label and explicitly not the dimensions and not the size.

The one gate worth naming, because it is the tempting one, is a **minimum
runtime that refuses to file a sample**. It is rejected. prdb publishes no
runtime, so there is nothing to compare against; an absolute threshold is an
uncalibrated number of exactly the kind ADR 0011 refused for size, and it is
wrong for every genuinely short scene. The sample is not shut out, it is made
**visible** — four minutes beside `1080p` in the review queue tells the person
what no threshold could decide for them. The wish for the gate is the argument
for the field, and it stops there.

## When it is read, and what a failure means

**Every video file is read exactly once, at collecting**, before anything is
decided about it — before identification, before the duplicate check, before
filing. Not at filing: a file that fails to identify never reaches filing, and
that is precisely the file whose runtime the review queue needs most. ADR 0017's
shape holds here too — read once, then the record is the truth, and nothing
opens the file again to ask the same question.

`prdb-ordeno`'s four failure states are adopted unchanged, having been measured
against real files: the source was gone, there is no video stream with a size in
it, `ffprobe` could not open it at all, and it did not answer in time.

**Only the quality holds a file back.** ADR 0011 already makes an unreadable
quality its own outcome, and that is the whole of it. A missing runtime, a
missing codec — some containers simply do not report them — is `null` and
nothing more; the file is filed and the surface shows a blank. Any other answer
would break the rule above by the back door, with a descriptive field stopping
something.

## Considered options

**Store everything the same call returns.** Rejected above: it trades a schema
of guesses against a re-read the routine machinery already makes cheap. It is
also unfalsifiable — no future field can ever be argued out of the table,
because the argument for all of them is that they were free.

**Probe at filing, as `prdb-ordeno` does.** Rejected because that project never
downloads and has no review queue holding unidentified files. Here the file that
is *not* filed is the one the user has to judge, and probing at filing would
leave it the only file with nothing to judge by.

**Keep exact dimensions out, since ADR 0011 forbids comparing them.** Rejected:
storing is not comparing, and the entry page has a real use for them. The risk
is acknowledged — this is the column most likely to drift into a second
criterion — which is why the rule above is written as a prohibition rather than
left implicit.

**Leave the probe fields out of the backup**, on ADR 0009's test that a backup
holds only what cannot be fetched again. Rejected. "Fetched again" there means
retrievable from prdb or an indexer; these are *reconstructible*, and only by
touching every file in the library, which ADR 0009 asks of a restore nowhere
else. A restore that showed five thousand entries without runtimes until a
routine finished would undo what that ADR was for. The file row is exported
already (ADR 0011 puts the filed path and quality in it); four more fields on
the same row cost nothing.

## Consequences

- `CONTEXT.md` gains **Probe** — the single reading of a video file — and
  **Runtime**, which settles a word this repo currently uses two ways.
  ADR 0012's *duration* is corrected to *runtime* to match.
- The `ffprobe` call widens from `stream=width,height` to
  `format=duration` plus `stream=width,height,codec_name`, still a header read
  and still milliseconds. `prdb-ordeno`'s skip of `attached_pic` streams carries
  over unchanged — cover art is a video stream by `ffprobe`'s reckoning, and its
  600×900 would name a scene after its poster.
- The data model gains four columns on the video file row and one on whatever
  a review queue entry turns out to be, all exported. The `osHash` sits with
  them.
- [Ticket 17](../../.scratch/first-release-spec/issues/17-what-the-review-queue-holds.md)
  is unblocked and inherits a bound: a queue entry may display runtime,
  dimensions, codec, quality, size and the `osHash`, and nothing else read from
  the file.
- [Ticket 26](../../.scratch/first-release-spec/issues/26-what-the-sidecar-and-the-poster-carry.md)
  gains a closed door: the sidecar cannot carry audio or subtitle languages,
  because they are not read.
- Adding a field after the first release is a one-shot routine in ADR 0014's
  table, with a lane and a resumable position. That is decided now rather than
  discovered at the first schema change, which is what lets the list above stay
  short.
- The `osHash` is computed for every collected video file, including files that
  are never filed. A review queue entry therefore already knows whether its file
  is bytes the library holds.
