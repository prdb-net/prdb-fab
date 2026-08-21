# prdb-fab

The language of a self-hosted tool that finds content on Usenet indexers, has
SABnzbd download it, and builds a sorted library out of what arrives, using
prdb as its only source of metadata.

## Language

### From prdb

**Video**:
The single scene prdb catalogues, and the unit everything here is organised
around.
_Avoid_: Scene, Title, Movie, Clip

**Site**:
The producer a video was released by.
_Avoid_: Studio, Label, Network

**Actor**:
A person credited on a video.
_Avoid_: Performer, Star, Model

**Wanted Video**:
A video the user has marked in prdb as one they want to have.
_Avoid_: Watchlist entry, Request, Wish

**Favourite**:
A site or an actor the user follows in prdb.
_Avoid_: Subscription, Follow

**Fulfilment**:
The report to prdb that a wanted video is now held.
_Avoid_: Completion, Satisfaction

**osHash**:
The hash computed from a file's size and its first and last 64 KiB, which
identifies that exact file whatever it has been renamed to.
_Avoid_: File hash, Checksum

**pHash**:
The hash computed from a video file's frames, which describes what the picture
looks like rather than what the bytes are.
_Avoid_: Perceptual fingerprint, Visual hash

### Indexers and acquisition

**Indexer**:
A Usenet search service the user has configured, queried through its
Newznab-style API.
_Avoid_: Tracker, Search provider, Source

**Usenet Provider**:
The service the articles are actually fetched from, held by SABnzbd rather than
by this tool. Never shortened to "provider" alone.
_Avoid_: Provider, Server, Host

**Release**:
One named package an indexer offers, identified by that indexer together with
the indexer's own ID for it. The same package offered by a second indexer is a
second release.
_Avoid_: Item, Result, Post, Grab

**NZB**:
The file that tells SABnzbd how to fetch a release.

**Download**:
One release handed to SABnzbd, and what became of it. Fetching a different
release for the same video afterwards is a second download.
_Avoid_: Job, Task, Fetch, Grab

**Release Ranking**:
The order over the releases of one video that says which of them is fetched
next. It is total and deterministic, so after a failure it names a next release
rather than only a winner.
_Avoid_: Candidate list, Shortlist, Score, Priority

**Consumed**:
Said of a release that was fetched for a video and did not leave that video
held. The ranking never offers it for that video again.
_Avoid_: Failed, Tried, Blacklisted

**Automation Rule**:
A standing instruction that permits releases to be downloaded without being
asked. A rule only ever permits: it never forbids what another rule allows, so
rules have no order and cannot conflict.
_Avoid_: Filter, Profile, Trigger

### Identification

**Identification**:
Deciding which video a release or a file belongs to.
_Avoid_: Matching, Lookup, Recognition

**Confidence**:
How strongly an identification is carried by the evidence behind it. A set of
named outcomes, not an order: `Ambiguous` sits above `Exact` in the API's
numbering while meaning the opposite, so confidences are matched against a
listed set and never compared.
_Avoid_: Score, Certainty, Probability

**Candidate**:
One of several videos that fit equally well, so that none of them can be
chosen.
_Avoid_: Suggestion, Guess, Possible match

**Site-Only Match**:
An identification that reached the site a release or a file belongs to and no
further. Its own outcome rather than a weak identification: there is no video,
so there is nothing to file.
_Avoid_: Partial match, Weak match

**Review Queue**:
Everything that could not be identified confidently and waits for the user to
decide.
_Avoid_: Inbox, Backlog, Unmatched

### On disk

**Download Directory**:
Where SABnzbd leaves what it finished, and where nothing is expected to be
tidy.
_Avoid_: Incoming, Staging, Source directory

**Leftover**:
A file in a download directory that carries no video — the `.nfo`, `.par2`,
`.sfv` and cover images an unpacker leaves behind. Never moved into the
library.
_Avoid_: Junk, Sidecar, Residue, Extra

**Scan Directory**:
A mounted directory outside the download directory whose files are candidates
for the library — typically a collection that existed before this tool did.
_Avoid_: Watch folder, Import directory, External library

**Library**:
The sorted collection this tool writes, and the only directory it owns.
_Avoid_: Collection, Target directory, Archive

**Library Entry**:
One video as the library holds it, together with the files that carry it. A
second quality of the same video is another file of one entry, never a second
entry.
_Avoid_: Item, Record, Copy

**Filing**:
Moving an identified video file out of a download directory into the library,
under the name and path the layout dictates.
_Avoid_: Import, Sorting, Organising, Ingest

**Video File**:
A file on disk that carries a video.
_Avoid_: Media file, Asset

**Quality**:
The resolution class a video file was encoded at, read from the file itself
rather than from prdb.
_Avoid_: Resolution, Format, Encode

**Duplicate**:
A release or a file whose video the library already holds at the same quality.
_Avoid_: Copy, Repeat

### Keeping up to date

**Sync**:
The continuous catching-up with what prdb, the indexers and SABnzbd currently
say.
_Avoid_: Refresh, Poll, Update, Crawl
