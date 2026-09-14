# What this tool would ask prdb for

`AGENTS.md` says prdb's public API is the only interface this project can
influence, and that a limitation nobody reports is a limitation nobody fixes.
This file is where a limitation is written down as something askable — a
concrete change, with the reason it is wanted and what is being done instead
until it exists.

Nothing here has been sent. Writing a proposal down and sending it are two acts,
and only the first one happens in a commit.

Each entry says what is missing, why the workaround is worse than the change,
and how the request could be answered without breaking anything already
published.

## Let Identification see user-preview hash bindings

**Status**: open, worked around.
**Raised by**: [ADR 0061](adr/0061-a-user-preview-belongs-to-one-file-and-is-served-only-while-prdb-still-shows-it.md).

### What is missing

`GET /video-user-images/by-os-hash/{basedOnFileWithOsHash}` is documented as
returning *publicly visible **unlinked*** user images: "Linked, hidden, denied,
and soft-deleted rows are excluded."

That excludes exactly the rows that carry a `videoId`. So the one endpoint
addressed by an osHash cannot answer the one question an osHash is asked —
*which Video is the file with this hash?* — and answers it emptiest precisely
where a moderated, linked binding exists.

The other direction works and is what this tool uses:
`GET /videos/{videoId}/user-images` filtered locally by
`basedOnFileWithOsHash`. It needs the Video id first, which means it can
confirm an identification and cannot make one.

### Why it matters

A user preview is submitted from a specific file and carries that file's osHash.
When such a row is linked to a Video and has passed moderation, prdb holds a
reviewed statement that a file with that hash is that Video — a statement it
already trusts enough to publish the picture under the Video. `POST
/videos/identify` walks a ladder whose first hash rung is the filehash corpus;
a file the filehash corpus does not know but a moderated user preview does is
resolvable today by a human looking at prdb's own website, and not by the API.

### What could be asked for

Either of these, in descending order of preference:

1. **Let `/videos/identify` consider the binding.** The ladder already reports
   which rung answered (`matchedBy`), so a new value — say `PreviewHash`,
   between `OsHash` and `PHash` — would let a client tell this apart from a
   filehash match without any change to the request. This keeps one
   authority for identification, which is what this tool would prefer:
   the client asks the same question and gets a better answer.

2. **A parameter on the by-hash list.** For example
   `?includeLinked=true`, defaulting to false so that nothing already published
   changes. This is a smaller change and a worse one, because it makes the
   client assemble an identification out of two endpoints and decide what to do
   when they disagree — which is a policy question that belongs on prdb's side
   of the line.

Neither needs a new resource, a new authentication scheme or a change to
`VideoUserImageDto`.

### What is done meanwhile

[ADR 0062](adr/0062-a-preview-hash-binding-is-evidence-and-prdbs-answer-is-still-the-authority.md)
uses only the bindings this installation has already been given by
`GET /videos/{id}/user-images` — the Videos somebody browsed or filed — and
never asks the by-hash endpoint to identify anything. That is correct and much
narrower than the capability would be: a file is resolvable only if its Video
happened to have been looked at already.

## Let a submitter find out whether a submission arrived

**Status**: open, not worked around — the gap is carried as an explicit
uncertain state.
**Raised by**: [ADR 0064](adr/0064-a-preview-is-published-only-after-it-has-been-explained-and-an-uncertain-upload-is-never-sent-twice.md).

### What is missing

`POST /video-user-images` takes a `multipart/form-data` body and returns `201`
with the new `videoUserImageId`. There is no idempotency key on the request, and
no way afterwards to ask whether a given submission landed.

Both halves matter together. A client whose request times out, whose connection
drops, or which is restarted mid-flight does not know whether prdb accepted the
upload. It cannot make the question safe in advance, because there is no key
prdb could recognise a repeat by; and it cannot answer it afterwards, because
the three read endpoints — by Video, by osHash, and by id — all return only
**publicly visible** rows, and a submission that has just arrived is in
moderation and therefore not publicly visible. So absence from every readable
endpoint is the ordinary state of a submission that arrived perfectly.

### Why it matters

The safe reading of an ambiguous outcome is *it may have arrived*, and the only
action compatible with it is to stop. A duplicate published picture cannot be
withdrawn by the client — there is no retraction in the API — so a retry risks a
permanent, visible, moderated duplicate under somebody's account in exchange for
saving one manual step.

That asymmetry makes an automatic retry indefensible, which means every
interrupted upload becomes a row a person has to decide about by hand. For one
installation that is a nuisance. For a client publishing a library's worth of
previews over a slow connection it is the difference between a background
channel and a chore.

### What could be asked for

Either of these, in descending order of preference:

1. **An idempotency key on the submission.** A client-generated value — a header
   or a form field — that prdb stores with the row and answers the second
   request with the first request's `201`. It makes a retry provably safe rather
   than probably safe, and it is the only shape that also covers the case where
   the duplicate request arrives while the first is still being processed.

2. **A way to see one's own submissions.** For example an
   `includeNotYetVisible` parameter on the by-hash list, or a
   `GET /video-user-images/mine`, scoped to the calling account and returning
   rows whatever their moderation state. It answers the question after the fact
   rather than preventing it, and it also gives a submitter somewhere to see
   what became of what they sent — which the API currently reports only for as
   long as a row happens to be public.

Neither needs a change to `VideoUserImageDto`, and the second one is additive to
an endpoint that already exists.

### What is done meanwhile

ADR 0064 records an interrupted upload as **uncertain**, keeps the generated
bytes, and never resubmits it automatically. The row is shown as a Brake on the
Status page, and sending it again is a person's explicit act. Where a later
ordinary read happens to bring the row back — once moderation has made it
public, `GET /videos/{id}/user-images` carries this installation's `osHash` for
that Video — the uncertainty resolves itself, but nothing waits on that: it
depends on a moderation queue with no promised timescale.
