# Releases are indexer-specific

A release is identified by the indexer it came from together with that
indexer's own ID for it. The same package offered by a second indexer is a
second release, and the two are never merged into one. Where they belong to the
same video, that is what relates them.

## Considered options

**Merge releases across indexers by name or hash.** It produces the shorter,
tidier list a person would draw by hand. Rejected because the merge is a guess:
release names differ in punctuation, repacks and proper tags, and a hash is
available for a minority of releases. A wrong merge hides a working release
behind a broken one, and unpicking it later means splitting rows that other
records already point at.

## Consequences

- The same content can appear several times in a list of releases. Grouping is
  the UI's job, done over the video the releases identified as, and it is
  honest about the alternatives rather than hiding them — which is what a retry
  after a failed download needs.
- Duplicate detection cannot run on release identity. It runs on the video, and
  on the hash of the file once there is one.
- An indexer's ID is only unique within that indexer, so nothing may key a
  release by that ID alone.
