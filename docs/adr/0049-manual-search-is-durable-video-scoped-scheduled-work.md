# Manual Search is durable Video-scoped scheduled work

A person may explicitly search one or all enabled Indexers for one Catalogue
Video. The request records one Manual Search and its per-Indexer work before it
returns; a sync-lane one-shot routine performs one title query per selected
Indexer through the existing transport and Daily Query Budget. The browser
polls local state and never holds an Indexer request open.

This amends ADR 0014's statement that the user waits for no search and ADR
0024's assumption that the Wanted Sweep is the only title query. The earlier
boundary against remote work in a page read remains: a Manual Search is an
explicit named action, not a side effect of opening or refreshing a page. It
may use the unreserved part of the Daily Query Budget ahead of the Indexer Walk,
but it never spends the Wanted Sweep reserve.

Search provenance remains separate from Identification. The selected Video's
title justifies the query and each returned Release is associated with the
Manual Search so its progress can be explained. New Releases enter `Awaiting`,
settled Releases stay settled, and only prdb may attach a Video, Confidence and
`matchedBy`. Repeating the query therefore cannot turn its own echo into a
match.

## Consequences

- All enabled Indexers are the default scope; one enabled Indexer may be chosen
  explicitly. No arbitrary Newznab query enters the first-release interface.
- Manual Search does not make a Video Wanted. ADR 0048's manual Download remains
  the point that records Wanted intent.
- Recent Searches and their Release associations are disposable discovery
  state. They pin their Video and Releases only for their bounded retention and
  do not cross the Backup boundary.
- An interrupted request is not repeated blindly. Its per-Indexer work becomes
  visibly retryable, while work that had not begun is recreated from durable
  state after a restart.
