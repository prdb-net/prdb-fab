# prdb-fab

Fetch and Build (FaB): find your favourite content on Usenet with prdb's help,
download it through SABnzbd, and build a sorted library out of what arrives.

Self-hosted, Docker Compose, single user. A prdb API key is required.

> **This is early software.** What is in the image today asks for a password,
> takes you through setting up, checks every connection you give it against the
> service it names, keeps local catalogues of what prdb and your indexers say,
> identifies cached releases through prdb, and can submit an identified Release
> to SABnzbd, follow it through completion, collect its Video Files and file
> identified arrivals into a Jellyfin-compatible library. It exposes that
> whole loop on Status and can report two separately enabled kinds of local
> fact back to prdb. Permission rules can run that same Download path unattended
> for matched Wanted Videos. Everything it holds that it cannot fetch again goes
> into one portable file, and a fresh container can be started from that file
> instead of from nothing.

## What you need

- **A prdb account with an API key.** Not optional: without it there is no
  identification, no wanted list, no artwork and no duplicate detection, and
  setting up cannot be completed.
- **Docker**, and storage you can mount into a container.

For downloading — which is the point, but not a condition of getting set up — a
Usenet provider with a working SABnzbd, and at least one indexer with API
access. Either can be skipped during setup and added later.

## Running it

```yaml
services:
  prdb-fab:
    image: prdbnet/prdb-fab:0.22.0
    container_name: prdb-fab
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      PUID: "1000"
      PGID: "1000"
      UMASK: "022"
    volumes:
      - ./data:/data
      - /srv/media:/media
```

`docker compose up -d`, then open `http://your-host:8080`.

The first thing it asks for is a password — there is no user name and no default
credential, and the field to set one is offered only while no password exists.
Whoever reaches the tool first sets it, so start it on a network you control.
After that it walks you through your prdb key, SABnzbd, your indexers and your
library root, checking each one against the real service before it stores it.
Setting up ends on your wanted list, with the first **90-day Recent Window**
already filling behind it — there is nothing to wait in front of. Status shows
when prdb and every enabled Indexer have completed that first proof.

**Over plain `http` the password travels across the network in the clear.** On a
LAN you control that is the ordinary way to run this; reaching it from anywhere
else wants TLS in front of it.

The mounts, `PUID`/`PGID`, the log, updating, and what to do when the password is
lost: **[docs/running-in-docker.md](docs/running-in-docker.md)**.

## What it does once it is set up

It reads prdb, on its own, and keeps what it reads: the videos prdb publishes,
the sites and actors behind them, your wanted list and your favourites, and one
picture per video. **What's new**, **Search**, **Sites**, **Actors** and
**Wanted** show that catalogue. Wanted and Favourite actions are written to
prdb through the same governed connection used by the background sync.

Each enabled indexer is also walked continuously. Releases enter a local,
disposable cache, are screened against the catalogue, and prdb is asked to
identify their names. The newest **90 days** are a standing local guarantee:
prdb Catalogue details, every enabled Indexer's Releases and prdb
Identification are filled and revalidated in the background without a page
first being opened. A shared **Releases** table opens from
all four browse surfaces and shows the Indexer, size, first seen time,
Identification State, Confidence and `matchedBy`. Ambiguous answers remain
Candidates and a Site-Only Match remains a Site — neither is presented as a
Video.

The browser never turns a page load into an indexer query. **Search Indexers**
is an explicit Video action: it records durable work, and the ordinary
scheduler queries all enabled Indexers or one selected Indexer while the page
shows progress. New results pass through prdb Identification before becoming
downloadable. Every other page search and refresh reads only local state. Each Indexer Cache is
bounded at **100,000 Releases**; the oldest examined Releases nothing points at
are evicted first, while Recent Window, unseen or still-wanted rows are never
discarded to hold the ceiling. The cache may exceed its ceiling when protecting
those rows requires it.

Each newly added Indexer starts with a **1,000-request Daily Query Budget**.
When there is searchable Wanted work, half of that budget, capped at 480
requests, is reserved for the Wanted Sweep; the rest is shared by Manual Search
and the continuous Indexer Walk. A resumable full 90-day pass runs beside the
head walk and repeats at least daily, so late Indexer visibility and outages do
not leave permanent holes. Its first pass may cost hundreds of queries or
continue on a later day. It never spends past that Indexer's daily budget.

**Status** lays the whole unattended loop out as Sync (prdb), Sync (Indexers),
Match, Decide, Download and File. Its headline counts only **Gaps** that need a
repair. A **Brake** explains work that is deliberately held by a configured
gate or budget and links to that choice without calling it broken. The liveness
line shows the last file filed, Download started or Release cached. The page
polls local state every five seconds; opening it never contacts prdb, an Indexer
or SABnzbd.

Every scheduled routine can be made due with **Run now** from Status. This is a
request to the normal scheduler, not a second execution path: the Governor,
daily Indexer query budget and empty work set still win, and the accepted,
deferred or refused answer remains visible beside the routine.

On a Catalogue card, **Download** immediately chooses the preferred highest
Quality available and submits it to SABnzbd; its overflow menu still opens the
Video's Releases table for an exact choice. **Settings → Downloads** sets the
highest Quality and lower-Quality fallback ladder used by that direct action.
Because Newznab has no dependable Quality field, common Release-name tags are
used as hints and an unlabelled Release is the final fallback. From the Releases
table, **Download** fetches that exact NZB and submits it to the configured
SABnzbd category. The choice is recorded before the remote write. prdb-fab then
polls SABnzbd, shows its own Outstanding, Completed and Failed state under
**Downloads**, and automatically tries the next ranked,
unconsumed Release after a release failure. Each Video has a budget of three
Download attempts; its Release view shows the spent attempts, the next choice,
and a confirmed reset of that Video's local history.

**Settings → Automation** adds unordered permission rules over the Wanted list.
A rule names its allowed enabled Indexers and optional minimum and maximum
Release size. There is no global switch: with no enabled rule, automation is
off. Enabling or changing a rule schedules a catch-up over already matched
Wanted Releases without a preview; the configured cap on unfinished automatic
Downloads (20 by default) bounds how much can be in SABnzbd at once, and the
remainder waits durably. The before-download Identification gate, an open
Review Queue entry, a held Library Video and the per-Video retry budget remain
independent brakes. No rule uses a favourite or a Quality parsed from a Release
name. See [docs/automation.md](docs/automation.md) for the complete safety and
Wanted-removal behaviour.

When SABnzbd reports Completed, prdb-fab collects supported Video Files,
identifies each one, and files allowed matches into
`<Site>/<Site> - <date> - <Title>/`. Moves within one filesystem are renames;
cross-filesystem moves copy to a hidden temporary file, compare every byte,
rename it into place, and only then delete the source. The entry receives
`movie.nfo` and cached `fanart.jpg`; catalogue repair keeps those two files up
to date without renaming a recorded Video File.

Anything that cannot safely proceed waits in **Review queue** with its evidence
and one reason. Every row can be dismissed or its exact Video File deleted;
Unidentified, Duplicate and Entry Missing add only their reason-specific File
as, Replace or File as only copy action. **Library** shows only held entries and
their qualities, and **Operation log** exposes the immutable Filed, Replaced,
Deleted and Tidied acts.

After all Video Files in a directory-shaped SABnzbd storage have reached a
decision, tidy-up removes only `.nfo`, `.par2`, `.sfv`, `.srr`, `.url`, `.txt`,
`.jpg` and `.png`, then empty directories. The setting is enabled by default.
Unknown files remain, and the parent directory of single-file storage is never
tidied.

Reporting under **Settings → Reporting** is on by default and has three
independent switches. Fulfilment reporting sends a wanted Video id, whether it
is held, when it was filed and the highest truthfully expressible quality.
Confirmed-assignment reporting sends the exact file metadata a person approved
in the Review Queue: Video id, osHash, size, filename, Release name and available
runtime, dimensions and codec. Turning either switch off makes no outbound
report for that channel; pending differences remain local, and reports prdb
already accepted are not retracted. See
[docs/privacy.md](docs/privacy.md).

The third switch is the only one whose payload strangers see: **published
previews** submits a scrubbing sheet generated from a filed Video File, which
prdb shows under that Video after moderation. Nothing is generated or sent until
the form saying what it publishes has been saved once, and prdb offers no
retraction for a submission it has accepted. Only files filed from then on are
published automatically; the Library you already hold is published only by
asking, from **Settings → Reporting → Published previews**, which is also where
an upload whose outcome could not be established is decided. See
[docs/publishing-previews.md](docs/publishing-previews.md).

The SABnzbd boundary remains exact: **prdb-fab never calls SABnzbd retry or
delete**, and *Stop following* changes only the local record. Removing a Video
from Wanted abandons its unfinished automatic Download locally, does not retry
it, and leaves the SABnzbd job and anything already filed untouched.

## Backup and restore

**Settings → Backup** writes one file holding everything about your installation
that the tool cannot fetch again: your settings, every indexer with its address
and key, the SABnzbd connection and its path mapping, your prdb key, every
automation rule, the review queue, and the local record of what was downloaded,
what was filed where, which releases are used up and what has already been
reported to prdb.

What is *not* in it is everything that can be fetched again — the indexer cache,
cached artwork, prdb's catalogue, and the video files themselves. It is a file
rather than an archive of your library, and small enough to keep several of.

**The file is plain JSON and the credentials in it are readable.** Nothing here
encrypts it, deliberately: whatever you already back up with — restic, borg, an
encrypted share — encrypts everything it carries under a key you manage, and a
second passphrase underneath that is one more thing to lose at the moment you
need the file most. So treat the file exactly as you treat the data volume, and
put it somewhere that encrypts it. Being readable is also what makes it useful
when a restore will not complete: you can open it and see what is in there.
[ADR 0057](docs/adr/0057-the-backup-travels-in-the-clear-and-whatever-carries-it-encrypts-it.md)
has the whole argument.

Restoring runs on a container that holds nothing yet, before a password exists —
the login credential is inside the file, so a fresh installation offers *Restore
a backup* as the second way to begin. It asks once where your library and your
downloads are mounted **in this container**, prefilled with wherever they were
on the machine that wrote the file, and re-roots every recorded path to match. It
refuses an installation that already holds an indexer, an automation rule or a
library entry, and says which. There is nothing to merge into an installation you
are already using.

Afterwards, outstanding downloads are picked up at SABnzbd by their job id where
it still knows them, and a background pass checks that the library is where the
library says it is. Until it has, entries count as held, so automation will not
decide to fetch them again — and nothing is deleted or re-fetched over a file
that is missing, because a library mounted somewhere else looks exactly the same
from here. What it could not confirm is a count on Status with a filter on the
Library behind it.

## Configuration

Six environment variables, two mounts, one port — and that is the whole of it.
Your prdb key, SABnzbd, your indexers and your library root are answered in the
browser and kept by the tool, so changing an indexer key is a form rather than
an edit to a YAML file and a restart.

## Reading further

- What it is for, and what it is deliberately not: [VISION.md](VISION.md).
- What changed between versions: [CHANGELOG.md](CHANGELOG.md).
- Why it is built the way it is: [docs/adr/](docs/adr/), one decision per file.
- Working on it: [CONTRIBUTING.md](CONTRIBUTING.md).
- How a release is proved and cut: [docs/releasing.md](docs/releasing.md).

MIT licensed. See [LICENSE](LICENSE).
