# Maintenance is explicit and notifications report attention

Library verification can be requested again for all files or only failed
answers. Invalidating its disposable answers makes the existing bounded Bulk
work set due, including after restart. The UI reports pending and unconfirmed
counts. No Library Entry, Video File, Fulfilment or Download is changed by a
check. This extends ADR 0009's Restore pass without changing what held means.

Collection previews show the remaining copy size and currently available
Library space. Known same-filesystem moves need no second video copy; unknown
devices are counted conservatively. The mover checks free space immediately
before a copy. Unknown free space does not mean zero, and a successful check
does not reserve capacity: normal write failures remain recoverable. This
extends ADR 0026's full-disk handling rather than replacing it.

Notifications are opt-in and use an explicitly configured HTTP(S) JSON webhook.
The payload contains source, attention/recovery state, stage ids and Gap counts,
and the Review Queue count. It contains no Video or Indexer names, paths, keys,
artwork, or arbitrary failure details. Brakes and cleared Gaps are excluded;
the notification routine's own Gap is excluded from its payload. The page still
shows delivery failures and the scheduler retries them. Redirects and HTTP
client logging are disabled, because a webhook URL may contain a secret.

Only a changed state is sent, with at least thirty minutes between successful
deliveries. A recovery message follows a previously delivered attention state.
Delivery deduplication survives restart. Delivery is at-least-once across a
crash between remote acceptance and recording it; an Idempotency-Key identifies
the transition for receivers that support deduplication. The destination and
enabled switch are exported. Local delivery evidence is disposable; Restore
observes and reports current conditions afresh when enabled.

ADR 0018's local Status response may be shared for up to five seconds, matching
its polling cadence. Overlapping callers share one read, failed reads are not
cached, and Run now invalidates the snapshot. A 10,000-Release fixture measures
SQL reads and verifies that ten overlapping cached reads perform zero additional
SQL reads; elapsed timings are diagnostic, not pass/fail thresholds. Status
still performs no remote work and all EF reads already default to no tracking.
