# The first release files into the Jellyfin layout

A video file whose identification carries is moved out of the download
directory into the library, in the Jellyfin layout — exact `yyyy-MM-dd` dates,
`<actor>` with a `<name>` child, quality suffixes — from the first release. What
comes later is *automatic* filing of everything downloaded regardless of how
well it identified, not filing itself.

Without this the first release cannot keep two of its own promises at once. It
offers a library view with artwork, search and filters, and a sorted library the
media server points at; if nothing files, that view describes files still lying
in the download directory and there is no sorted library to point anything at.

## Considered options

**File nothing; the library is a record only.** Files stay where SABnzbd left
them and the library view is a database over them. Rejected because it makes the
download directory permanent, which the tool is built to empty, and because the
separation between a directory allowed to be a mess and a directory the media
server reads would mean nothing.

**File into a simpler structure now, adopt the Jellyfin layout later.** Cheaper
to write, and it defers the layout rules. Rejected because adopting the layout
afterwards is a mass rename across the whole library — the single most dangerous
operation this tool could perform, arriving as a migration step, without the
dry run that protects every other bulk operation here. The layout is also not
open research: `prdb-ordeno` validated one against a real Jellyfin instance and
its rules apply unchanged, so building a second structure first is work spent to
create a migration.

## Consequences

- Identification and filing run per video file, so one download can produce
  several library entries and a download has an outcome per file rather than
  one overall.
- Quality is read from the file, so `ffprobe` is in the runtime image from the
  first release. Only the perceptual hash is deferred, not the tool that
  computes it.
- Filing needs a confidence threshold, which is the second place the scale is
  turned into behaviour.
- Only video files move. What the unpacker left behind is not carried into a
  directory the media server reads.
- A move that fails leaves the source untouched and surfaces as a retryable
  condition rather than a crash, and a cross-filesystem copy that breaks off
  removes its partial target.
- Scan directories do not follow from this and are not in the first release.
  They need a dry run, an undo across thousands of files and a review queue that
  survives that volume; ADR 0001 still governs them when they arrive.
