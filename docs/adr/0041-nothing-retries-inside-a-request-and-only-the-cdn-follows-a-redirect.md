# Nothing retries inside a request, and only the CDN follows a redirect

Four kinds of remote thing, four transports, and one rule that runs through all
of them: **the HTTP layer reports what happened and never decides what to do
about it.** Retrying, pacing and giving up all belong to the schedule, which
already owns them.

## Nothing retries inside a request

`Prdb.Sdk` ships Kiota's policy — three attempts, honouring `Retry-After` — and
its own documentation warns that it multiplies with an application's. It is
turned off (`PrdbRetryOptions.Disabled`, which `prdb-ordeno` already does at
every one of its call sites), and no resilience library is taken. Four separate
decisions already made say the same thing:

- **[ADR 0014](0014-one-schedule-of-routines-paced-by-a-governor.md) retries at
  the routine.** Its interval doubled per failure, capped at an hour, reset on
  success, with a `429`'s `Retry-After` obeyed exactly. A second layer under it
  would multiply the two, and silently — the outer one cannot see how many
  requests the inner one spent.
- **[ADR 0024](0024-the-wanted-sweep-asks-with-a-title-from-a-reserved-share-of-the-budget.md)
  gives each indexer a daily query budget the *user* sets.** An invisible retry
  counts three requests as one and turns that number into a lie, on the one
  control ADR 0014 admitted precisely because the tool cannot know the answer.
- **The governor reads the rate limit off every response.** A retry that
  swallows a `429` withholds from it exactly the information it exists to act
  on.
- **[ADR 0016](0016-a-download-is-followed-by-polling-and-a-failure-is-the-releases-or-the-installations.md)'s
  second outcome is already the right answer.** A failed request means the
  download's state is unknown: nothing applied, nothing counted, and five
  seconds later it is asked again. Three attempts with backoff would stretch one
  poll past the next cadence to reach a conclusion the next poll reaches anyway.

**A timeout is ADR 0016's case 2 and never case 3.** That ADR makes the
distinction between *the request failed* and *the id was genuinely absent*
load-bearing — collapsing them turns an unreachable SABnzbd into a pile of
consumed releases — and a timeout is the case most likely to be filed under the
wrong one. Three *consecutive genuine* absences are terminal; a timeout neither
increments nor resets that count.

`Microsoft.Extensions.Http.Resilience` therefore does not enter the project.
There is nothing left for it to do, and its presence would invite somebody to
give it work.

(*Amended by
[ADR 0043](0043-core-returns-its-reasons-and-the-log-is-a-file-the-user-can-send.md),
which changes nothing here and supplies the second half of the test this was
applying. A library is taken when it does something the platform does not and
its licence is one that cannot be withdrawn under this repository's MIT terms.
This decision declined on the first half alone; the second would have declined
it too.*)

## Four transports, and the timeout belongs to the transport

Four named clients from `IHttpClientFactory`: prdb, indexers, SABnzbd, artwork.
**One client for all indexers**, however many are configured — a client is a
transport, not an address, and the URL travels with the request.

The timeout follows the cadence rather than taste, and the SDK's own
hundred-second default is the example of what happens otherwise: SABnzbd is
polled every five seconds while anything is outstanding, so a timeout longer
than the cadence is a poll queued behind a poll. As starting values, revisited
if anything real disagrees: **SABnzbd 10 s**, **prdb 30 s**, **indexers 30 s**,
artwork **2 s on the display path** and **30 s for the backfill routine** —
[ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md)
is the one place that wants two different waits for one thing, because a slow
CDN must serve the no-artwork tile rather than hold a grid open.

**The prdb API key belongs to the request, not to the application.**
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)
makes it a setting somebody changes in a form while the container runs, so the
transport is shared and pooled while the client is built per use — `prdb-ordeno`'s
arrangement, and the reason the SDK offers an overload that reads its options on
every resolution.

### Only the CDN follows a redirect

The three transports that carry a credential refuse redirects; the artwork one
follows them, because it carries none. That is `prdb-ordeno`'s rule verbatim,
and it is sharper here.

For prdb it is not optional — the SDK refuses to build on a transport that
redirects, since a redirect it never sees is a redirect whose cross-origin rule
never runs, and nothing below strips `X-Api-Key`.

For indexers it is stronger still, and this is the part worth writing down: the
key is not in a header at all. Newznab puts it **in the query string**, and
[ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md) records that
the download URL embeds it — which
[ADR 0037](0037-credentials-are-stored-in-the-clear-because-there-is-nowhere-to-put-a-key.md)
then made load-bearing. A followed redirect hands that URL, credential included,
to whatever host the redirect names.

The credentialled transports still never follow a redirect. One real Newznab
dialect exposed a narrower case the original research missed: an Indexer
configured at an HTTPS API can advertise the same host's enclosure as HTTP,
then redirect it to the identical HTTPS address. In that case the first request
is built as HTTPS on the configured Indexer's host and port; the HTTP address is
never requested and no redirect is followed. An HTTP enclosure on another host
is refused because the configured connection supplies no evidence that it can
be upgraded safely. A deliberately configured HTTP Indexer remains HTTP.

The other place that would have forced a redirect remains ruled out: the
Newznab research rejects `t=get` as a route to discover an enclosure, because
it is a 302 to the enclosure URL the tool is already holding.

## The governor sits in two places, doing two different jobs

[ADR 0035](0035-core-holds-the-rules-infrastructure-holds-the-rows-and-the-filesystem.md)
placed it — decision in `Core`, socket in `Infrastructure`, never `Host` — and
[ADR 0038](0038-a-lane-is-one-worker-and-the-routine-row-is-the-only-truth.md)
sent the mechanism here with one constraint from
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md): it may not
be a wait inside a lane, because a routine waiting on it holds a lane in order
to do nothing.

**The lane asks before it starts a routine.** A deferred routine does not claim
the lane at all, which is what that constraint asks for. **Inside a run the
routine asks per request and ends the run when refused**, rather than waiting —
the row stays where it is and the routine meets it again on the next tick, which
is what ADR 0032 already says a deferral does.

**A `DelegatingHandler` on the prdb transport does the two things no caller
should be able to forget**: read the rate limit off every response, and refuse a
request that carries no clearance from the governor. That is what keeps this
from being a policy applied at call sites, which ADR 0035 warned about — a call
site cannot be added without it, because the transport rejects one that tries.

A person's search (ADR 0022) asks the same service and gets ADR 0040's answer: a
verdict with HTTP 200, not an error. The artwork transport passes none of this,
as ADR 0030 requires.

## The tool says what it is

A `User-Agent` of product name and version, on all four transports.

This is not a formality. The Newznab research found that NNTmux ships middleware
blocking specific User-Agents outright, and its conclusion is to set an honest,
identifiable one. A tool that downloads unattended against somebody else's
service should be legible to whoever runs it, and the version is what makes a
report about it actionable — ADR 0034's pinned tags already make it exact.

No contact URL: `VISION.md` carries no operational address, and a dead link is
worse than none. No browser-shaped agent, which is the behaviour the blocked
names in that research are aimed at.

## What is not cached at the HTTP layer

[ADR 0013](0013-the-prdb-catalogue-is-a-cache-with-pinned-rows-repaired-by-re-reading.md)
uses `If-None-Match` on `GET /sites` and records that a cache hit answers `200`
with a body rather than `304`, which must not be read as a change. That is
handled where the ETag lives — as a row, with the cursors — and not by an HTTP
cache. A response cache that turned that `200` into something the calling code
never saw would defeat the exact sentence that ADR wrote down.

## Considered options

**Keep the SDK's retry, and take `Microsoft.Extensions.Http.Resilience` for the
rest.** Rejected under *nothing retries inside a request*: it makes ADR 0024's
user-set budget wrong, hides `429`s from the governor, and duplicates ADR 0014.

**One named client per configured indexer.** Rejected: a client is a transport,
and N of them buys N handler chains to the same kind of endpoint.

**One transport for everything, with per-request timeouts.** Rejected: the
timeouts differ by an order of magnitude and the redirect rule differs by
credential, so one transport would express neither.

**Let the indexer transport follow redirects, for compatibility.** Rejected: the
credential is in the URL, so a redirect leaks it. The only endpoint that needed
one is already ruled out. Upgrading a same-host HTTP enclosure before the first
request handles the observed compatibility case without weakening this rule.

**A response cache in front of `GET /sites`.** Rejected under *what is not
cached*: ADR 0013 wrote down a behaviour that only makes sense to code that sees
the `200`.

## Consequences

- **`CONTEXT.md` is unchanged.** A transport is an artefact.
- **`Directory.Packages.props` gains no resilience package**, and the absence is
  a decision rather than an omission — worth a comment where somebody would
  otherwise add one.
- **ADR 0016's three outcomes now have a transport-level rule to hold them
  apart**: a timeout is case 2, and no retry may convert one case into another.
- **ADR 0038's remaining constraint is discharged.** The governor's mechanism is
  fixed; what is left is writing it.
- **Ticket 09 inherits the other half.** How a failed request is *expressed in
  code* so that ADR 0016's three outcomes cannot be flattened by accident is
  still open; this decision fixes only that the transport does not flatten them
  first.
- **The timeouts are starting values.** They are the one part of this that a
  real installation can disagree with, and disagreeing is how they get corrected.
