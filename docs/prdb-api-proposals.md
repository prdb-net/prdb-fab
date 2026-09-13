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
