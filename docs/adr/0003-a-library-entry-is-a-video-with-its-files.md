# A library entry is a video with its files

The library is organised by video. One entry holds the one or more files that
carry that video, so a 1080p and a 2160p copy of the same video are two files
of one entry rather than two entries.

## Considered options

**One entry per file.** Simpler to write, and it needs no rule for what happens
when a second quality arrives. Rejected because the library view would show the
same video twice, and every "do I already have this" question — the one the
tool exists to answer — would need a grouping step over the top of it.

## Consequences

- The Jellyfin layout follows from this rather than fighting it: one directory
  per video, with the second quality distinguished inside it.
- A file that has not been identified cannot be a library entry, because there
  is no video for it to belong to. It stays where it is and appears in the
  review queue.
- Quality belongs to the file, not to the entry, and is read from the file
  itself.
