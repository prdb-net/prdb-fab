# An Actor Catalogue Fill is explicit, bounded and durable

The Catalogue keeps every Actor and the complete current profile carried by
the Actor change feed. It does not mirror every Video credited to every Actor.
An Actor surface instead offers an explicit **Actor Catalogue Fill**: it pages
the latest Videos for that Actor by Release Date, reads their full details, and
stops after 500.

The fill is scheduled bulk work, not a live page request. Its page position and
progress are durable, every prdb request goes through the shared governor, and
the Actor surface continues to read only the local Catalogue while showing the
progress. A restart resumes the same fill. Requesting it again replaces the
previous result with the current latest 500.

The current result points at its Videos and therefore pins them under ADR 0033.
This matters for older Videos already present in the Catalogue: merely reading
their details again would not otherwise stop normal Catalogue eviction from
removing them. Replacing the result on a later fill bounds the additional pinning
obligation at 500 Videos per Actor the user has explicitly prepared.

A global Video mirror was rejected by ADR 0013 because both its initial read and
its permanent repair obligation are unbounded. A complete per-Actor filmography
has the same problem for Actors with large histories and gives no predictable
request cost. A live request from the Actor page was rejected because it makes
ordinary browsing depend on prdb availability and bypasses the scheduler's
pacing, persistence and progress model.

The bound makes the maximum remote work knowable: five list pages of 100 Videos
and, when every row needs details, ten detail batches of 50. It is a useful
recent slice rather than a claim that the Actor's filmography is complete.
