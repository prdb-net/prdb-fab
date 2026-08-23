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
The report to prdb that a wanted video is now held, and at what quality. It is a
claim about the library entry rather than about any one file, and it understates
where prdb's coarser scale cannot express what is held.
_Avoid_: Completion, Satisfaction

**Confirmed Assignment**:
A person's answer to which video a file is, given in the review queue and kept
as a record of its own. It is what prdb cannot work out for itself, and it
outlives the entry that produced it.
_Avoid_: Submission, Vote, Contribution

**osHash**:
The hash computed from a file's size and its first and last 64 KiB, which
identifies that exact file whatever it has been renamed to.
_Avoid_: File hash, Checksum

**pHash**:
The hash computed from a video file's frames, which describes what the picture
looks like rather than what the bytes are.
_Avoid_: Perceptual fingerprint, Visual hash

**Pre-Name**:
A scene release title prdb records for a video. A video may have several or
none, and the same title is what an indexer names a release after.
_Avoid_: PreDB entry, Scene name, Release title

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

**Indexer Cache**:
The copy of the releases the indexers offer that the tool holds locally. Only
ever added to, bounded, and disposable — it can be thrown away and refilled,
which is what separates it from the library.
_Avoid_: Local index, Release database, Mirror, Corpus

**Indexer Walk**:
The routine that pages an indexer's newest releases into the indexer cache. It
sees only what is new, which is why the wanted sweep exists beside it.
_Avoid_: Crawl, Poll, RSS sync, Scrape

**Daily Query Budget**:
How many requests one indexer may be sent in a day, set by the user rather than
discovered, because a Newznab quota belongs to their account and most indexers
report nothing about it. Never prdb's rate limit, which is read from a response.
_Avoid_: Quota, Rate limit, Cap

**Watermark**:
How far an indexer walk has already come, and therefore where it stops asking.
Made of a post date together with a release identity, because either alone
stops too early. It says nothing about what the tool has looked at.
_Avoid_: Cursor, Bookmark, Offset, Position

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
Said of a release that was fetched for a video, whatever became of it. The
ranking never offers it for that video again.
_Avoid_: Failed, Tried, Blacklisted

**Retry Budget**:
How many downloads one video may be given before the tool stops fetching for
it. Spent by every download, whatever became of that download, and cleared only
by the user.
_Avoid_: Attempts, Quota, Limit

**Automation Rule**:
A standing instruction that permits releases to be downloaded without being
asked. A rule only ever permits: it never forbids what another rule allows, so
rules have no order and cannot conflict.
_Avoid_: Filter, Profile, Trigger

### Identification

**Identification**:
Deciding which video a release or a file belongs to. Always prdb's answer,
never one this tool worked out for itself — before a download from the name,
after one from the hash.
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

**Screening**:
The local pass over cached releases that decides which of them are worth asking
prdb about, by comparing their names against the pre-names and titles the
catalogue holds. It never identifies anything: a hit is a reason, not an answer.
_Avoid_: Matching, Pre-match, Filtering, Triage

**Identification State**:
What has been done to a cached release and what came of it. The tool's own
position over the indexer cache rather than a claim about what prdb holds — a
release nothing has remarked on is not a release prdb does not know.
_Avoid_: Match status, Verdict, Result

**Review Queue**:
Every video file the tool declined to move, waiting for the user to decide. Each
entry carries one reason for not having been moved, and that reason is what
decides which actions the entry offers.
_Avoid_: Inbox, Backlog, Unmatched

**Dismiss**:
Closing a review queue entry and leaving its file exactly where it lies. The
decision that unblocks the cleanup of a download directory without anything
being deleted.
_Avoid_: Ignore, Skip, Archive

### On disk

**Download Directory**:
Where SABnzbd leaves what it finished, and where nothing is expected to be
tidy.
_Avoid_: Incoming, Staging, Source directory

**Path Mapping**:
What turns a path as SABnzbd reports it into a path this tool can open. Needed
because the two see different filesystems whenever SABnzbd runs in its own
container.
_Avoid_: Folder mapping, Path translation, Remote path

**Collecting**:
Finding the video files a finished download left behind, by resolving the path
SABnzbd reports and looking at what is actually there. The end of a download
and the beginning of filing.
_Avoid_: Pickup, Harvest, Import

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
under the name and path the layout dictates, and writing the sidecar and the
poster that belong beside it.
_Avoid_: Import, Sorting, Organising, Ingest

**Filed Path**:
Where a video file was put when it was filed. Computed from what prdb said at
that moment and then recorded, so it is read from the record and never worked
out again — a correction prdb publishes later changes what the library displays
and not what anything is called on disk.
_Avoid_: Target path, Destination, Location

**Sidecar**:
The `movie.nfo` this tool writes beside a filed video file, and where the media
server reads the video's title, date and cast from. It wins over the file name
wherever the two disagree, which is what makes the name on disk cosmetic. Never
the `.nfo` an unpacker left in a download directory, which is a leftover.
_Avoid_: Metadata file, NFO, Manifest

**Replacing**:
Putting an arriving video file in the place of the one the library holds at that
quality, and deleting the file it displaces. The only thing the tool does that
writes against filed content, and never something it does by itself — renaming
a filed file to carry its quality label writes against a name rather than
against content, and is not this.
_Avoid_: Overwrite, Upgrade, Swap

**Video File**:
A file on disk that carries a video.
_Avoid_: Media file, Asset

**Probe**:
The single reading of a video file, done once when a download is collected and
never repeated. It measures the file and describes it; nothing it produces
decides anything by itself.
_Avoid_: Scan, Analysis, Inspection, Media info

**Quality**:
The resolution class a video file was encoded at, read from the file itself
rather than from prdb. Named by a label — `1080p`, `2160p` — taken from a fixed
ladder, and compared only as that label, never as the pixel dimensions behind
it.
_Avoid_: Resolution, Format, Encode

**Runtime**:
How long a video file plays, read from the file because prdb publishes no such
figure. A property of the file rather than of the video: two files of one entry
may disagree, and neither is corrected against the other.
_Avoid_: Duration, Length, Playtime

**Duplicate**:
An arriving video file whose video the library already holds under the same
quality label. Never a release: before a download there is no file, and so no
quality to compare.
_Avoid_: Copy, Repeat

### Keeping up to date

**Sync**:
The continuous catching-up with what prdb, the indexers and SABnzbd currently
say.
_Avoid_: Refresh, Poll, Update, Crawl

**Catalogue**:
The copy of prdb's videos, sites and actors the tool holds locally. A cache of
what has been looked at rather than a complete copy, and never the library,
which is what the tool writes to disk.
_Avoid_: Mirror, Corpus, Metadata store, Cache

**Pinned**:
Said of a row the tool must keep because something local points at it — a
catalogue video behind a library entry, a wanted video, a download or a review
queue entry, or a cached release that was downloaded, consumed, or identified as
a video still wanted. What is not pinned may be dropped to keep the catalogue
and the indexer cache bounded.
_Avoid_: Locked, Retained, Held, Kept

**Repair**:
The part of the sync that stands in for a change feed that does not exist:
re-reading pinned videos to learn about corrections prdb published and artwork
it removed, neither of which is announced anywhere.
_Avoid_: Reconcile, Backfill, Re-sync, Refresh

**Routine**:
One named piece of work the tool runs on a schedule of its own, carrying its
own position so a restart continues it, and its own record of when it last
succeeded. Recurring or one-shot; a one-shot routine retires when it is done.
_Avoid_: Job, Task, Cron, Timer

**Wanted Sweep**:
The routine that searches the indexers for wanted videos directly, rather than
waiting for them to appear among an indexer's newest releases. The only way an
older video is ever found, since no indexer will order its results by when it
was indexed.
_Avoid_: Backfill, Deep search, Rescan

**Governor**:
What decides whether a prdb request is sent now or deferred, from the rate
limit read off the last response rather than from a number known in advance.
Every request passes it, including one a person asked for.
_Avoid_: Throttle, Limiter, Budget

### Getting set up

**Onboarding**:
The guided path from a fresh installation to a working one, with two entry
points: setting up fresh, or restoring a backup.
_Avoid_: Setup wizard, First run, Installer

**Connection**:
A configured route to one of the three outside services — prdb, SABnzbd, or one
indexer — together with the credential it is reached by and the verdict of the
last check against it. What a Gap names when one is missing or no longer
verifies.
_Avoid_: Integration, Endpoint, Service

**Password**:
The single secret the user signs in with, set during onboarding and belonging
to the installation rather than to an account. Never the passphrase a backup is
encrypted under.
_Avoid_: Credentials, Passphrase, PIN

### Knowing it works

**Status**:
The surface that answers whether anything is broken, cut into the six stages of
the loop. Never the dashboard, which answers what is happening.
_Avoid_: Health, Sync status, Diagnostics, System page

**Gap**:
A part of the loop that is missing or no longer usable — a connection never
configured, one that stopped verifying, or a routine that has failed enough
times to count. Named and carried where the user can see it, never silently
worked around.
_Avoid_: Warning, Error, Issue, Todo

**Brake**:
A place where the tool, working exactly as configured, is deliberately not
acting — carrying a count, the reason, and a route to the setting behind it.
Never a Gap: nothing is broken, and what it holds back may be exactly what was
asked for.
_Avoid_: Warning, Limit, Block, Throttle

### Keeping it safe

**Backup**:
The single file the tool exports, holding everything about this installation
that cannot be fetched again, and nothing that can be.
_Avoid_: Archive, Snapshot, Dump, Export

**Restore**:
Turning a backup back into a working installation, on an installation that
holds nothing yet.
_Avoid_: Import, Recovery, Migration

**Passphrase**:
What the secrets inside a backup are encrypted under, chosen when it is
exported and needed again to restore it. Never the password the user signs in
with.
_Avoid_: Password, Key
