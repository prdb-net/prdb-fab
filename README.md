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
> fact back to prdb. Unattended Download automation is not in this version.

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
    image: prdbnet/prdb-fab:0.8.0
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
Setting up ends on your wanted list, with the first read of prdb's catalogue
already running behind it — there is nothing to wait in front of.

**Over plain `http` the password travels across the network in the clear.** On a
LAN you control that is the ordinary way to run this; reaching it from anywhere
else wants TLS in front of it.

The mounts, `PUID`/`PGID`, the log, updating, and what to do when the password is
lost: **[docs/running-in-docker.md](docs/running-in-docker.md)**.

## What it does once it is set up

It reads prdb, on its own, and keeps what it reads: the videos prdb publishes,
the sites and actors behind them, your wanted list and your favourites, and one
picture per video. **What's new**, **Sites**, **Actors** and **Wanted** show that
catalogue. Marking a video as wanted happens in prdb; this reads that list and
never writes to it.

Each enabled indexer is also walked continuously. Releases enter a local,
disposable cache, are screened against the catalogue, and prdb is asked to
identify the names worth asking about. A shared **Releases** table opens from
all four browse surfaces and shows the Indexer, size, first seen time,
Identification State, Confidence and `matchedBy`. Ambiguous answers remain
Candidates and a Site-Only Match remains a Site — neither is presented as a
Video.

The browser never turns a page load into an indexer query. It searches and
filters what the background routines have already cached. Each Indexer Cache is
bounded at **100,000 Releases**; the oldest examined Releases nothing points at
are evicted first, while unseen or still-wanted rows are never discarded to
hold the ceiling.

Each newly added Indexer starts with a **1,000-request Daily Query Budget**.
When there is searchable Wanted work, half of that budget, capped at 480
requests, is reserved for the Wanted Sweep; the rest carries the continuous
Indexer Walk. The first walk reads up to 90 days of history and may therefore
cost hundreds of queries or continue on a later day. It never spends past that
Indexer's daily budget.

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

From a Video's Releases table, **Download** fetches that exact NZB and submits
it to the configured SABnzbd category. The choice is recorded before the remote
write. prdb-fab then polls SABnzbd, shows its own Outstanding, Completed and
Failed state under **Downloads**, and automatically tries the next ranked,
unconsumed Release after a release failure. Each Video has a budget of three
Download attempts; its Release view shows the spent attempts, the next choice,
and a confirmed reset of that Video's local history.

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

Reporting under **Settings → Reporting** is off by default and has two
independent switches. Fulfilment reporting sends a wanted Video id, whether it
is held, when it was filed and the highest truthfully expressible quality.
Confirmed-assignment reporting sends the exact file metadata a person approved
in the Review Queue: Video id, osHash, size, filename, Release name and available
runtime, dimensions and codec. Turning either switch off makes no outbound
report for that channel; pending differences remain local, and reports prdb
already accepted are not retracted. See
[docs/privacy.md](docs/privacy.md).

This version's remaining boundary is exact: **prdb-fab never retries or deletes
a SABnzbd job**, and *Stop following* changes only the local record. The whole
manual path from a wanted Video through filing works; unattended Download
automation is still off.

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

MIT licensed. See [LICENSE](LICENSE).
