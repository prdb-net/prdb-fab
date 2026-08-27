# The API is named actions, and a verdict is a success

`prdb-ordeno`'s contract is adopted whole: minimal-API endpoints grouped per
feature, an OpenAPI document emitted at build time and **committed**,
`openapi-typescript` generating types the frontend compiles against, CI failing
when the two drift, and requests staying plain `fetch` rather than a generated
client (its ADR 0014). Authentication is already settled by
[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)
— `401` and never a redirect, one anonymous state endpoint answering whether a
password is set, whether this caller is signed in, and which onboarding step is
next — and nothing here touches it.

Four things this tool has and that project does not, each decided against a
decision already made.

## Actions are named, because the log has to know which one happened

Every action that is not a field update gets an endpoint of its own —
`POST /api/review/entries/{id}/dismiss` rather than a `PATCH` carrying a state.
*Dismiss*, *Delete*, *File it as …*, *stop following*, *Abandoned*, *Run now*,
[ADR 0008](0008-between-releases-of-one-video-size-stands-in-for-quality.md)'s
reset, and the backup's export and restore are all of this kind.

The reason is
[ADR 0029](0029-the-operation-log-records-one-act-per-video-file-and-nothing-reads-it-back.md),
which records one entry per video file moved, relabelled, replaced or deleted,
**naming who acted and why**. Under a state field the log would have to infer
which act took place from the value written, and the four acts that share a
resource are not distinguishable that way — *Dismiss* leaves the file where it
is, *Delete* removes it, *File it as …* moves it, and each is a different
sentence in the log.
[ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
describes the surface the same way: the same two exits plus at most one further
action chosen by the reason. That is a set of acts, not a resource with fields.

The same argument settles the shape rather than only the names:
[ADR 0028](0028-downloads-are-a-table-of-their-own-and-the-release-view-answers-for-the-video.md)
puts *stop following* on a submission and not on a video, which a resource
hierarchy would have had to decide by nesting.

## The tool computes what a confirmation covers, and the request carries what was shown

[ADR 0011](0011-a-duplicate-is-decided-after-the-download-by-quality-label.md)
requires a confirmation to **name the file**, and ADR 0022 spells out that a
count is not a name — twenty selected files is twenty lines with their sizes.
ADR 0028 requires the same of *Abandoned*, which spends a retry budget.

**The backend computes the coverage**, because the frontend cannot. ADR 0028's
*Abandoned* covers downloads the list in front of the person does not show, so a
frontend building the list out of its own rows would be right by luck. The same
computation that produces the preview performs the act, which is the shape
`prdb-ordeno` already runs with `TryPlan` and `TryFile`: a preview computed by
different code from the act is a preview that can lie.

**The act then takes the identifiers that were shown, not the filter that
produced them.** This is the part that would otherwise be got wrong quietly: if
the request re-ran a selection, anything that arrived between the preview and
the click would be deleted without ever having been named, which is precisely
what ADR 0011's rule exists to prevent. Something in the list that has since
gone is reported rather than silently skipped.

## A verdict is a success

Everything the tool checked and can answer is **HTTP 200 with a typed verdict in
the body**: the governor deferring a person's search
([ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)),
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)'s
four connection verdicts, and a confirmation being required. Status codes keep
their own meanings — `400` the request was wrong, `401` not signed in, `404` no
such thing, `500` the tool is broken — expressed as `ProblemDetails`, which is
what ASP.NET Core produces anyway.

The deciding argument is
[ADR 0036](0036-the-frontend-takes-two-dependencies-and-the-address-bar-holds-what-is-linkable.md)'s
second dependency. TanStack Query treats a `503` as a failure: it takes the
error path, sets an error state, and **retries** — so expressing a deferral that
way would have the client automatically repeat the one request the governor just
declined to allow, and the frontend would have to unwrap an error to find a
perfectly ordinary answer. A deferral is literally what
[ADR 0032](0032-a-routine-with-a-work-set-is-due-when-the-set-is-not-empty.md)
says it is: not a state and not a failure.

This is the same distinction ADR 0018 draws on the page one level up. A **Brake**
is the tool working correctly and holding something back; expressing it as an
error at the transport layer would contradict the page it feeds.

## Two endpoints for two very different reads, and nothing embedded

ADR 0022 puts a review queue count in the header of **every** page.
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md) has a
status document polled every five seconds that must cause **no work** — no feed
fetched, no indexer queried, no contact with SABnzbd.

They are two endpoints. The count is a small one under a fixed query key that
every page shares — one request for the whole application, which is what ADR
0036 took TanStack Query for. The status document is the second, polled only
while the status page is open.

**Embedding the count in every response is rejected**, and not on tidiness: it
couples every response shape in the API to a counter that has nothing to do with
it, and it would put ADR 0018's no-work requirement everywhere at once. That
requirement stays checkable only while there is one place the read happens.

## Paging is offsets, and the page is in the address

ADR 0022 and ADR 0028 both paginate "the way the library is". That is offsets
with a page number, and the page number lives in the address.

ADR 0036 already made this rule for the whole frontend — anything worth linking
to lives in the address, not in component state — and a cursor list with
infinite scroll has no page anybody can send. The usual argument for cursors is
a list growing underneath somebody paging through it, and the only table in this
tool that grows that way is the indexer cache, which nobody pages through:
ADR 0025 established that there is no surface where a person types against it.
The library is what is held, the review queue is small, and downloads are newest
first.

## What this leaves to the build

The route table is ADR 0036's and follows from the surfaces. The exact endpoint
names, the response record shapes, and where `openapi.json` is written and
regenerated are the skeleton's, decided by writing them; ADR 0036's note that
generated types need **named** response types on the backend is the one
constraint carried into that.

**How the five browse surfaces share code stops being an open question here.**
[ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)
makes What's New, Sites, Actors, Wanted and Library artwork grids over different
populations, and whether that is one component parameterised five ways or five
sharing a card is a question about how a class is written. It is answered by
writing it, in the feature work the foundation exists to carry.

## Considered options

**`PATCH` with a state field.** Rejected under *actions are named*: ADR 0029's
log would have to infer the act from the value, and four acts that share a
resource are not distinguishable that way.

**The frontend builds the confirmation list from the rows it already has.**
Rejected: ADR 0028's *Abandoned* covers rows that surface does not show.

**The act re-runs the selection instead of taking identifiers.** Rejected:
anything arriving between preview and click would be acted on without having
been named, which is ADR 0011's rule broken by a race.

**`503` with `Retry-After` for a deferral.** Rejected under *a verdict is a
success*: TanStack Query would retry the request the governor declined, and the
frontend would unwrap an error to find an ordinary answer.

**One endpoint serving both the header count and the status document.**
Rejected: the count is read on every page and the document only on one, so a
single endpoint would either poll the whole document everywhere or make the
document lazy in a way ADR 0018's no-work rule cannot then check.

**Cursor paging with infinite scroll.** Rejected under ADR 0036's address rule:
there would be no page to link to, and no table here has the growth that
motivates cursors.

**A generated client rather than generated types.** Rejected as `prdb-ordeno`
rejected it: a runtime library in a bundle where `fetch` and a type declaration
do the whole job.

## Consequences

- **`CONTEXT.md` is unchanged.** An endpoint is an artefact, not a concept the
  language needed.
- **`Host` owes named response types**, which ADR 0036 flagged as the cost the
  generated types impose on the backend rather than on the frontend.
- **The operation log's completeness gains a second guard.** ADR 0035 made it a
  property of the project layout; named actions make it a property of the API
  surface too, since there is one endpoint per act to log.
- **Every action that spends something or removes something has two calls**,
  and the second carries what the first named.
- **The fog patch about the five browse surfaces is closed** rather than
  graduated: under this map's depth rule it is build work, not a decision.
- **Ticket 09 inherits a narrower question**: how a failure is expressed *in
  code* is still open, but how it leaves the process over HTTP is fixed here.
