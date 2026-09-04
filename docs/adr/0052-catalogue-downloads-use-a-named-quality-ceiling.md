# Catalogue Downloads use a named Quality ceiling

A Download started from a Catalogue card chooses one Release immediately. The
person sets a global **Preferred Download Quality**: `2160p`, `1080p`, `720p`,
or `480p`. The choice is a ceiling. The action first looks for that Quality,
then walks downward through the named ladder; it never silently substitutes a
known Quality above the ceiling. Within one Quality, ADR 0008's existing total
Release Ranking still decides.

Newznab does not guarantee a Quality field. For this deliberately
person-originated convenience action only, common Release-name tags therefore
act as a hint: `2160p`, `4K`, `UHD`, `1080p`, `FHD`, `720p`, and `480p`. A
Release without one of those tags remains the last fallback, in its existing
ranked position. This is not Identification evidence, does not alter automatic
selection, and does not claim the arriving file has that Quality; `ffprobe`
still makes the only authoritative measurement after the Download.

The default is `2160p`, which preserves the former one-click behaviour of
preferring the largest eligible Release when names carry no usable hint. The
setting belongs to the installation and takes effect on the next Catalogue-card
Download without a restart.

The card's primary Download control submits through the same durable manual
acquisition path as an exact Release choice. The overflow menu always links to
the Video's Release view, so inspecting and choosing a particular Release stays
available.

## Considered options

**Treat the preference as an exact requirement.** Rejected because one missing
Quality would turn a convenience action into a dead end even when a useful
lower Release exists.

**Choose the closest Quality in either direction.** Rejected because a person
who chose `1080p` may be setting a storage or bandwidth ceiling. Taking `2160p`
would violate that choice rather than fall back from it.

**Apply the same hint to Automation.** Rejected. Automatic selection has no
person present for the decision and ADR 0008's title-free rule remains the safer
boundary there.

**Remove unlabelled Releases from the one-click action.** Rejected because an
Indexer may omit the tag while still providing an otherwise eligible Release.
The UI states this limitation instead of pretending the hint is complete.

## Consequences

- The preference is exported with the rest of the installation settings.
- A higher known Quality can still be chosen explicitly from the Release view.
- A misleading Release-name tag may select a different Release, but can never
  decide Identification or the Quality recorded in the Library.
- ADR 0008 remains unchanged for Automation and for ordering Releases within a
  selected Quality; this decision adds the person-only grouping above it.
