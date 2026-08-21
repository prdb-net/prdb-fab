# Automation is a set of permissions over the wanted list

An automation rule permits a download and never forbids one, and in the first
release every rule draws from a single source of intent: the wanted list. Rules
are therefore unordered and cannot conflict — a release is fetched if any
enabled rule allows it and the pre-download gate of ADR 0006 lets it through,
and the download records every rule that applied.

Automation is the reason to run an unattended service at all, so its shape has
to be decided before anything else can rely on it. `VISION.md` names three
scopes — site, actor, wanted list — and then permits the first release to start
narrow. The narrow version is not merely smaller: a rule scoped to a favourite
site is an unbounded standing order, and it acts on a Site-Only Match, which by
definition has no video. Such a download cannot be checked against the library,
cannot be reported as fulfilled, and has no name to file under. The wanted list
is per-video intent, bounded by a list the user maintains, and it closes the
fulfilment loop.

A rule carries a minimum size, a maximum size and the indexers it may use.
There is deliberately **no quality condition**, although `VISION.md` says
"quality and size limits": quality is read from the file (ADR 0005), so before
a download it could only be parsed out of a release name, and refusing to trust
release names is the premise of this tool. A maximum size buys what "not the
4K one" practically means without believing a name. Quality survives as a
preference between releases that already qualify, not as a gate.

## Considered options

**Ordered rules with first-match-wins, or deny rules.** The familiar shape from
every mail filter. Rejected because it buys precision this domain does not
need and pays for it in explanation: with permissions only, "why was this
fetched" is a list of the rules that applied, and "why was this not fetched" is
one gate plus the size limits — never a walk through a priority order looking
for the rule that shadowed another.

**Favourite sites and actors as rule scopes from the start.** Closest to
`VISION.md`'s automation section, and genuinely useful. Rejected for the first
release because it is unbounded by construction and, through Site-Only Matches,
would put files on disk that no other part of the first release can reason
about. It returns as its own effort once the review queue and duplicate
handling have run in anger.

**A quality condition parsed from the release name.** Rejected: it reintroduces
the release name as authority, in the one place where being wrong is silent.

**Preview and confirm before the catch-up pass.** When a rule is enabled it is
matched against the whole cache, which can mean a great many downloads at once.
Rejected as a second confirmation UI: a cap on unfinished automatic downloads
bounds the same risk, and every decision is already logged and cancellable.

## Consequences

- Rules need no ordering, no priority, and no conflict resolution, and a
  download references all the rules that applied to it rather than one.
- Evaluation is triggered by two events — a release entering the cache, and a
  video joining the wanted list — and both are evaluated against the whole
  cache, so enabling a rule is a catch-up pass by design.
- The number of automatic downloads SABnzbd has not yet finished is capped
  (default 20); the remainder waits rather than being dropped.
- Disabling a rule is forward-only, but removing a video from the wanted list
  cancels its running download and deletes the job at SABnzbd — the wanted list
  speaks about a video, a rule only about future matches. Nothing already filed
  is removed.
- Rules are disabled rather than deleted, and a download keeps the names of its
  rules, so provenance survives a deleted rule.
- Automation refuses a video the library already holds in any quality, which is
  stricter than the duplicate definition, because the incoming quality is not
  knowable before the download.
- A failed download may be retried against another release a bounded number of
  times per video; exhausting that stops with a visible reason.
- Fulfilment reporting hangs off filing rather than off automation, so it works
  identically for a manual download, and carries its own switch.
- The sync status page gains a second silent-failure diagnosis beside ADR
  0006's: how many releases were held back by size limits or by the cap.
