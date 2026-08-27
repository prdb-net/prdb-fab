# Files are moved out of scan directories too

A scan directory is a mounted directory holding a collection that existed
before this tool did. Files identified there are **moved** into the library,
exactly as files from the download directory are, so that the collection ends
up in one place rather than two.

## Considered options

**Hardlink where possible, copy across filesystems.** The common answer in this
corner of self-hosting: the library gets an entry, the original stays put, and
the bytes are stored once. Rejected because it leaves the collection in two
shapes at the same time — the old tree and the new one — which is the state the
user installed this tool to get out of. It also only works within one
filesystem, so the behaviour would differ per directory.

**Copy.** Safe and uniform, but a large existing collection then occupies twice
the disk, which is the problem the user does not have room for.

## Consequences

The first run over a scan directory rearranges storage the user considers
sorted, and it leaves unidentified files behind in a tree that has been thinned
out. That makes two things load-bearing rather than optional: the run shows
what it would do before it does it, and the operation log can put every file
back where it came from.
