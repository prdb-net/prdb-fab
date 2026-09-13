# A preview-hash binding is evidence, and prdb's answer is still the authority

A user preview carries the osHash of the file it was made from. When prdb has
**linked** that preview to a Video and moderation is showing it, prdb is
publishing a reviewed statement that a file with that hash is that Video. This
decision says when that statement may name a Video for an Arriving File, what
Confidence it records, and what happens when it is later withdrawn.

It answers only for the population
[ADR 0061](0061-a-user-preview-belongs-to-one-file-and-is-served-only-while-prdb-still-shows-it.md)
holds. It changes nothing about `POST /videos/identify`, which stays the
authority, and it is deliberately narrower than the capability would be if prdb
answered the question directly — which is what
[`docs/prdb-api-proposals.md`](../prdb-api-proposals.md) asks it to.

## What "server-validated" proves, and what it does not

The API document calls `basedOnFileWithOsHash` a *server-validated 16-character
OS hash*. Three claims can be read into that; only the first is in it.

1. **The syntax was checked.** Sixteen hexadecimal characters. This is what the
   phrase says, and the upload form's own pattern — `^[0-9A-Fa-f]{16}$` — is
   where it is enforced.
2. **The submitter said the image came from that file.** The value is attached
   to an upload by whoever made it. prdb holds no copy of the file, so it cannot
   have computed the hash itself.
3. **prdb asserts that the file with that hash is that Video.** Not documented,
   and for a row whose `videoId` is null not even claimed.

What makes (3) *nearly* true for a linked row is not the validation but the
moderation: a human moderator approved a picture, submitted from a file with
this hash, being published **under that Video**. That is a reviewed statement,
and it is what this decision treats as evidence. It is still not prdb answering
the question *what is this file*, which is why the answer prdb does give always
wins.

## Does `/videos/identify` already know?

The ladder is documented: **MD5, osHash, pHash, stored file name, release name,
site**, first rung wins, `matchedBy` names it. The osHash rung is documented
against the filehash corpus — the technical profiles clients submit — and
nothing anywhere in the document says the ladder consults
`VideoUserImage.basedOnFileWithOsHash`. `matchedBy` has no value that would
report it if it did.

**So the document says no.** It cannot be established live from here: that needs
a key, a real file whose hash is bound only by a user preview, and a request —
none of which is available while writing a contract, and the last of which is an
external act.

**The design is correct under either answer**, and that is the point of the
shape below rather than a happy accident. Preview-hash evidence is consulted
**only where prdb named no Video**. If the ladder already incorporates these
bindings, prdb answers first, the evidence is never reached, and nothing here
fires. If it does not, this fills exactly the gap. A fixture asserts the first
half: with an answer from prdb naming a Video, the evidence is not consulted
even when it names a different one.

## The policy, named

**Preview-Hash Evidence.** For an Arriving File with a stored osHash *H*, let
*E* be every user preview row this installation holds where

- the normalised `OsHash` equals *H* (`UserPreviewHash`, upper case, sixteen hex
  characters — a value that is not one matches nothing),
- `VideoPrdbId` is not null, and
- the row is **shown**: `UserPreviewModeration` says prdb is publishing it now.

Evidence is **sufficient** when *E* is not empty and every row in it names the
same Video *V*. It is **conflicting** when the rows name more than one Video,
and **insufficient** when *E* is empty.

The outcome is then decided against what prdb said about the same file:

| prdb's answer | Sufficient evidence for *V* | Outcome |
| --- | --- | --- |
| Names a Video | anything | **prdb's Video stands.** The evidence is not consulted. |
| `Ambiguous`, candidates include *V* | yes | **Assign *V*.** The tie prdb declined to break is broken by a moderated statement about this exact file. |
| `Ambiguous`, candidates exclude *V* | yes | **Nothing.** Review Queue, ambiguous as before. |
| No Video and no candidates (`None`, or a Site only) | yes | **Assign *V*.** |
| anything | conflicting or insufficient | **Nothing.** Review Queue, exactly as before. |

**Confidence is `Strong`, and never `Exact`.** `Exact` is what prdb says when its
own authoritative hash matched; recording it for an answer prdb did not give
would be putting words in prdb's mouth, in the column the user's admission gate
reads. `Strong` is what a moderated statement about this exact file, with
nothing corroborating it, is worth. The default After-Download gate admits
`Exact` and `Strong`, so the automatic outcome the user asked for happens by
default — and an installation that has narrowed its gate to `Exact` keeps its
narrowing, which is a person's decision and not this decision's to overrule.

**Provenance is `IdentificationRung.PreviewHash`**, a value that is explicitly
*not* one of prdb's. The enumeration quotes prdb's wire values, so this one
carries a number far outside their range and is documented as local — a later
SDK adding `Md5 = 5` must not collide with it, and a reader must not take it for
something prdb said.

**No human confirmation is invented.** A `ConfirmedAssignment` is a person's
answer given in the Review Queue, kept as a record of its own and reported to
prdb (ADR 0022, ADR 0023). An automatic assignment writes none, reports none,
and is not a Confirmed Assignment in any surface.

## Why this needs no request, and no upstream work

The evidence is rows this installation already holds, put there by somebody
opening a Preview or by a Video File being filed. So the check is **one indexed
local query** and no prdb request at all: nothing to pace, nothing to
deduplicate, no cadence, and no new failure mode when prdb is unreachable.

That is also why **FAB-36 is not blocked on prdb**. The by-hash endpoint cannot
answer the question (ADR 0061), and asking prdb per unidentified file would be
the "unbounded client-side mirror" this contract was told to avoid — so the
narrower thing is done instead, and the wider capability stays a recorded
proposal rather than a prerequisite. The narrowness is real and worth stating:
**a file is resolvable this way only if its Video happens to have been looked at
or filed already.** A file of a Video nobody has ever opened has no evidence here
and goes to the Review Queue as it always did.

## When a file is looked at again

**Every run of the arrival routine**, over one set-based query rather than one
query per file. Three populations are examined and they are the whole of the
retry policy:

- **Files prdb has just answered about**, in the same run, with the answer in
  hand.
- **Files sitting in the Review Queue unidentified**, with a stored osHash. This
  is what makes evidence that arrives *later* count: somebody opens the Preview
  of a Video, its previews are read, and the file that has been in the queue for
  a week is identified on the next tick. No request is spent looking, so there
  is no cadence to tune and no reason to stop looking.
- **Files this policy has already assigned and that are not yet filed**, which
  is the withdrawal check below.

A file a person has decided about — dismissed, confirmed, filed — is not
re-examined. A person's answer outranks this one, which is ADR 0022's rule and
not a new one.

## Withdrawal and correction

The evidence is moderated, so it can go. What happens then depends entirely on
whether the file has been moved yet, and the line is absolute.

**Before filing.** The assignment is re-checked on every run while the file is
waiting to be filed. If the evidence no longer holds — every row withdrawn, or
the rows now naming two Videos — the Video, the Confidence and the rung are
taken off the row and it goes back to the Review Queue with its ordinary
unidentified reason. Nothing has been moved, so nothing has to be undone.

**After filing, nothing on disk moves.** No rename, no delete, no reassignment,
no second Library entry. A filed file is content the person now has, in a place
their media server knows, and a moderator withdrawing a picture is not grounds
for this tool to touch it. What happens instead is that the Video File is
**flagged for review**: a durable record saying *this file was identified
automatically from evidence prdb has since withdrawn*, carried per Video File
and surfaced where the file is. The flag goes on its own when the evidence
comes back — a restored preview is not something anybody needs to be told about
twice — and otherwise it stands. There is deliberately no *dismiss* action yet:
what a person does about it is decide, and the decisions this tool already
offers about a filed file are the ones on the Library entry.

That record is **exported**. It cannot be re-derived — the preview rows it is
about are gone by definition — and it is a statement about a file in the user's
library rather than a cache of something fetchable, which is the same test
`library_preview_asset` passes and `LibraryVerification` fails.

**A correction is a withdrawal and a new statement.** prdb relinking a preview
to a different Video arrives as an ordinary change-feed page: the old binding
stops being evidence, the new one starts. A file not yet filed is re-decided; a
filed one is flagged, and the flag says which Video the evidence now names so
that the person deciding has the fact in front of them.

## What this reconciles

**[ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)
— a Video File is read once, and what is read decides nothing.** Both halves
stand, and the second is the one that needs saying out loud. The file is not
reopened, re-probed or re-hashed: the osHash this reads is the one the single
probe already stored (ADR 0026 collects it, ADR 0061 normalises it). And what
was *read locally* still decides nothing — the decision is made by a statement
prdb published and a moderator approved, and the local reading contributes only
the sixteen characters that say which statement is about this file. A local
measurement being the *key* to somebody else's answer is not a local
measurement being an answer.

**[ADR 0023](0023-nothing-local-identifies-anything-and-a-pre-name-is-only-a-reason-to-ask.md)
— nothing local identifies anything, and a Pre-Name is only a reason to ask.**
This is the one that has to move, and it moves by exactly one clause.

What ADR 0023 refuses is this tool *inferring* an identity: matching a title,
trusting a release name, deciding that two files that look alike are the same
scene. Its worked example is the Pre-Name, which is a strong-looking local
signal that is only ever a reason to ask prdb. The rule protects against a tool
that quietly builds its own opinion of what a file is and then acts on it.

A preview-hash binding is not an inference. It is an **assertion prdb published
and a moderator approved**, held locally as a cache exactly like every other
Catalogue row, and applied by an exact match on a hash rather than by any
judgement. Nothing is guessed, nothing is scored, and no threshold is chosen.
The clause ADR 0023 gains is therefore narrow and it is written to stay narrow:

> An identification may also be made from an exact match between a Video File's
> stored osHash and the hash prdb publishes on a **linked, currently visible**
> user preview, where every such preview names one Video, and only where prdb's
> own answer named none. It records `Strong` and the `PreviewHash` rung. It is
> not a Confirmed Assignment and is never reported to prdb as one.

Everything else ADR 0023 settled stands: a Pre-Name is still only a reason to
ask, a file name still identifies nothing, a runtime still decides nothing
(ADR 0031), and the ladder is still prdb's.

**`CONTEXT.md`'s *Identification*** said *always prdb's answer, never one this
tool worked out for itself*. That sentence is now false in a way worth being
precise about rather than left to be discovered, so the term gains the second
source and keeps the prohibition: prdb's answer, or a hash binding prdb
published — and never one this tool worked out for itself. A new term,
**Preview-Hash Evidence**, names the second source so that it can be referred to
without describing it each time.

## Sanitized examples

Ids are made up and the hash is not a real file's. In each case the Arriving
File's stored `OsHash` is `A1B2C3D4E5F60718`.

**Success.** prdb's answer:

```json
{ "ref": "0f8…", "confidence": 0, "matchedBy": null, "videoId": null, "candidates": [] }
```

The evidence held locally, one row, shown, linked:

```json
{ "id": "77…", "videoId": "44…", "basedOnFileWithOsHash": "A1B2C3D4E5F60718",
  "moderationStatus": "Approved", "moderationVisibility": "Public", "isDeleted": false }
```

Outcome: `VideoId = 44…`, `Confidence = Strong`, `MatchedBy = PreviewHash`, the
After-Download gate applied as always, no Confirmed Assignment written.

**Conflict.** Two shown, linked rows carry the same hash and name `44…` and
`55…`. Outcome: nothing is assigned. The file stays in the Review Queue,
unidentified, with both Videos recorded as candidates so that the person
deciding sees what the tool saw.

**No match.** No row carries the hash, or the only one that does has
`videoId: null` — an unlinked preview, which is what the by-hash endpoint
returns and which names no Video at all. Outcome: nothing changes; the file is
in the Review Queue exactly as it was before this decision existed.

**Overruled.** prdb answers `videoId: 66…`, `matchedBy: 0`, `confidence: 4`, and
the local evidence names `44…`. Outcome: `66…`, `Exact`, `OsHash`. The evidence
is not consulted, the disagreement changes nothing, and it is worth exactly one
line in the log.

## The contract the implementation is held to

- Read the arrival's **stored** osHash. Do not open, probe or hash a file.
- Normalise both sides with `UserPreviewHash.Normalise`. A value that is not
  sixteen hexadecimal characters matches nothing rather than matching loosely.
- Consult evidence **only** where prdb named no Video, and only rows that are
  linked and shown.
- Sufficient and unanimous, or nothing. Record `Strong` and `PreviewHash`.
- Hydrate the Catalogue detail for the Video the way the routine already does
  for one prdb named, so that the Library has what it needs.
- Go through the existing After-Download gate and the existing duplicate
  handling. Bypass neither.
- Write no `ConfirmedAssignment` and report nothing to prdb.
- Re-check before filing; flag after filing; never move, rename, delete or
  reassign filed content.
- Record the reason automatic identification did not proceed where the Review
  Queue can show it.

## Considered options

**Record `Exact`.** Rejected above: it would put words in prdb's mouth in the
column a person's gate reads, and an installation narrowed to `Exact` would find
its narrowing quietly worked around.

**Record a Confidence of its own.** Rejected. The values are prdb's wire
vocabulary and the gate is a set membership over them (`CONTEXT.md`: a set of
named outcomes, not an order). A sixth value would mean every existing gate
silently not admitting it, which is the automatic outcome being downgraded to
nothing by a data-modelling choice.

**Ask `/video-user-images/by-os-hash/{hash}` for every unidentified file.**
Rejected on the endpoint's own documentation: it excludes linked rows, so it
answers emptiest exactly where a Video could be named. It also spends a request
per file for an answer that cannot contain one.

**Mirror the global change feed to build a complete hash index.** Rejected for
ADR 0013's reason, and it is the "unbounded client-side mirror" this contract
was told not to build: the population is larger than the Catalogue, and holding
all of it to answer occasionally would make a table that is a multiple of the
one it describes.

**Wait for prdb to extend `/videos/identify`.** Rejected as a *blocker*, kept as
a proposal. Blocking on it would mean shipping nothing while an installation
that has browsed a Video already holds the answer for files of it; and the
narrow version costs nothing and is superseded cleanly if the proposal is ever
answered — at which point prdb names the Video first and this stops firing on
its own.

**Let evidence disambiguate nothing, and only confirm.** Rejected against the
epic's own words: the automatic outcome is required rather than downgraded to
visual assistance. Breaking a tie prdb declined to break, using a moderated
statement about this exact file and only among the candidates prdb itself
listed, is the narrowest form of that which is still an outcome.

## Consequences

- **`IdentificationRung` gains `PreviewHash`**, numbered far outside prdb's
  range and documented as not one of prdb's.
- **`CONTEXT.md`'s *Identification* is amended** and gains *Preview-Hash
  Evidence* beside it.
- **ADR 0023 gains one clause**, quoted above, and keeps everything else.
- **One new exported table** carrying the flag on a filed Video File whose
  evidence was later withdrawn. It is the second thing in this epic to cross the
  Backup boundary, and for the same reason as the first.
- **No new prdb request, no new routine, no new cadence.** The check is a query
  inside the routine that is already asking prdb about arriving files.
