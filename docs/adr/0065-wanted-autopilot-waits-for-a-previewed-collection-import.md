# Wanted autopilot waits for a previewed collection import

A fresh installation receives a supplied Wanted permission during setup. It
waits for usable Connections, a complete Wanted baseline and preparation of the
existing collection. An upgrade preserves the current rules and starts no new
permission; an existing installation adopts this workflow explicitly.

This amends VISION's opt-in automation and deferred Scan Directories, ADR 0007's
manually authored permissions, ADR 0010's end of setup, ADR 0020's presentation
of Automation and ADR 0024's empty-search ordering. Additive custom permissions,
prdb Identification authority, Confidence, ignored Sites, held Videos, Review
Queue holds, Retry Budget and SABnzbd ownership remain unchanged.

## Preparation

The Library root and optional mounted Scan Directories are inventoried without
moving content. Bounded durable work records enumeration, probing and prdb
Identification. Inaccessible roots and interrupted enumeration are incomplete,
not empty. Symlinks do not expand the configured boundary; Downloads and already
recorded Library files are excluded. Unrecorded files under the Library root
are included without being mistaken for output of the current import.

A revisioned preview shows sources, identities, Confidence, measured Quality,
proposed destinations and exceptions. A person approves concrete moves and
explicitly resolves or leaves exceptions. Unknown files cannot reliably suppress
a later duplicate Download, and the preview says so. A genuinely empty completed
inventory needs no empty approval. Approval queues work; it does not move files
in a request. Filing rechecks the source and destination and retains the normal
copy/verify/delete, duplicate handling and Operation Log. No fake Download or
Indexer represents a local file.

All automatic submissions wait until approved moves and required decisions are
complete. A page visit or a timeout cannot open this barrier. Once ready, known
held Videos and unresolved candidate-specific holds remain effective. Manual
Downloads retain their explicit action semantics.

## Bounded permission

The supplied permission admits Videos by prdb Release Date over 90 UTC calendar
dates, today minus 89 days through today inclusive. Unknown and future dates do
not qualify. Once the complete initial Wanted baseline is known, the newest
100 eligible missing Videos are selected, ordered by Release Date descending
and stable prdb id. This is a durable selection, never a sliding top 100.
Completion, failure, removal and aging out do not refill it.

New recent Wanted intent after the baseline is admitted continuously. Persist
both admitted and excluded baseline membership so replay and metadata refresh
cannot turn an old backlog into new intent. Re-enabling does not reset the
batch. All enabled Indexers participate automatically, including later ones.
The existing unfinished Download cap (20) and per-Video Retry Budget (3) remain.
Custom rules remain independent permissions and may deliberately admit a wider
scope; disabling only the supplied permission must not claim all automation is
off. Each Download retains every permitting rule's named Origin.

Prioritize admitted recent work in Wanted Sweep and automatic decisions, with
fair due ordering. Empty searches record an attempt and wait before another
attempt; they must not monopolize the first batch forever. This changes ADR
0024's empty-answer ordering, not its search vocabulary or query budget.

## Lifecycle

Portable Backup carries user intent, provenance, decisions and frozen membership.
Restore invalidates local preparation readiness and rechecks mounted content and
unfinished moves. Disabling and deletion remain durable. Membership and baseline
are stamped by account; a newly encountered account needs its own complete
baseline. Credential rotation within the account never resets selection. None
of this expands the separate Catalogue or Indexer coverage promises of ADR 0050.

## Guided Automation surface

Automation leads with the three steps: check the existing collection, review
and import, then download Wanted. The current step and next action are visible
before advanced controls. Adoption shows the configured Library folder and
one action that starts preparation and grants the supplied permission after
preparation; optional additional folders stay collapsed. Successful adoption
opens collection progress directly. A link never leads back to the page already
being viewed. Custom rules and limits remain available under Advanced settings,
with the count of enabled custom rules visible even while collapsed.
