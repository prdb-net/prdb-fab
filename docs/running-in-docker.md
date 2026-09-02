# Running prdb-fab in Docker

Docker Compose is the supported way to run this. Not one option among several —
the way.

> **This is early software.** What is in the image today asks for a password,
> takes you through setting up, checks every connection you give it against the
> service it names, then keeps local catalogues of what prdb and your indexers
> say. It discovers and identifies Releases, submits a selected one to SABnzbd,
> follows the job, and files safely identified Video Files into the library.
> Status explains the whole loop, and two opt-in reporting channels can send
> local facts back to prdb. Permission rules can run that same Download path
> unattended for matched Wanted Videos.

## The quickstart

```yaml
services:
  prdb-fab:
    image: prdbnet/prdb-fab:0.14.2
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

Then `docker compose up -d`, and the tool is at `http://your-host:8080`.

**Pin a version rather than `latest`.** An unattended tool that moves files and
upgrades itself when the NAS reboots is a surprise, and the second half of that
argument is under *Stopping and updating* below.

## The first run

Open it in a browser. The first thing it asks for is **a password** — there is
no user name and no default credential, and the field you set it in is only
offered while no password exists. Whoever reaches the tool first sets it, so
start it on a network you control and set it before you do anything else.

After that it takes you through four steps, and each one is stored the moment it
is answered. Closing the tab costs nothing: it resumes where it was.

1. **Your prdb API key.** Required. Without it there is no identification, no
   wanted list, no artwork and no duplicate detection. It is checked against
   prdb before it is stored.
2. **SABnzbd** — its address, its API key, the category it downloads into, and
   where that category's finished folder is inside this container. Skippable;
   without it nothing will be downloaded.
3. **Your indexers**, one at a time. Skippable; without one nothing will be
   searched for. Each is checked with a real search rather than with a
   capabilities call, because most indexers answer that one to anybody.
4. **The library root**, the directory this tool files into. Required. It is the
   only directory the tool writes to, and it must be writable by the user the
   container runs as.

Each connection is checked against the real service before it is accepted, and
one that fails is not stored — there is no *continue anyway*, because a wrong
key is not a thing that gets better on its own. Skipping is a deliberate act
with its consequence spelled out at the moment you take it, and setting up does
not come back to ask again: what a skipped step left missing is filled in from
the settings afterwards.

Everything you answered is editable later under **Settings**, without a restart
and without editing this Compose file.

As soon as the prdb key and an Indexer are configured, background routines fill
the rolling **90-day Recent Window**. They read prdb Catalogue details, cache
every enabled Indexer's recent Releases and submit those Releases to prdb for
Identification without requiring a page visit or Manual Search. The first fill
is incremental and survives restarts; Status shows it as incomplete until prdb
and every enabled Indexer have each proved the whole interval.

## Automation, Downloads and the SABnzbd boundary

Automation is off until at least one rule is enabled under **Settings →
Automation**. Every rule is an independent permission over matched Wanted
Videos: it selects allowed enabled Indexers and optional minimum and maximum
Release size. Rules are unordered, may overlap, and never deny another rule.
They do not use favourite Sites, favourite Actors, or a Quality guessed from a
Release name.

Enabling or changing a rule immediately schedules a catch-up over matching
Releases already in the cache. There is no preview. The catch-up uses the same
background Decide work set as newly identified Releases and newly Wanted
Videos, and the default cap permits at most 20 unfinished automatic Downloads
in SABnzbd at once. Further work waits durably. Before every submission the
Video must still be Wanted, absent from the Library in every Quality, free of an
open Review Queue entry, inside the before-download named confidence set, and
within its three-attempt Retry Budget. Status and the Release view explain a
gate, rule, cap or other deliberate non-act.

Disabling a rule is forward-only. Deleting one asks for confirmation; existing
Downloads keep the copied rule name in their Origin. If a Video leaves Wanted,
an Outstanding or just-completed automatic Download becomes **Abandoned**
locally, no retry follows, SABnzbd is not changed, and anything already filed
stays in the Library. The exact safety model is in
[automation.md](automation.md).

Open **Releases** for a Video and choose **Download** beside an eligible
Release. The confirmation names the exact Release and its cost: every submitted
Release is one of that Video's three Download attempts. prdb-fab records the
attempt before it sends the NZB to the configured SABnzbd category, then asks
SABnzbd about outstanding jobs every five seconds. The **Downloads** table shows
the local state, SABnzbd's last status and messages, the Indexer, origin and how
long the job has been outstanding.

A release failure spends that attempt and automatically selects the next
ranked, unconsumed Release while budget remains. A job SABnzbd rejects, reports
Failed, pauses as encrypted or unwanted, or no longer knows after three
successful polls is a release failure. An unreachable or globally paused
SABnzbd is instead an installation problem: it raises a Gap and spends nothing.
A failed request is unknown, never evidence that a job vanished.

When all three attempts are spent, the Release view says so. If eligible
Releases run out first, it says that separately. **Reset Download history** is a
confirmed action for that exact Video: it deletes its local attempt history and
makes those Releases eligible again. Any SABnzbd jobs remain untouched.

The only SABnzbd write prdb-fab performs is the initial `addfile`. It never uses
SABnzbd's retry or delete actions. **Stop following** likewise marks only the
selected local Download; the SABnzbd job continues under your control.

**Completed means SABnzbd has finished; Collected means its supported Video
Files have been handed to Filing.** Collection recursively recognises `.3gp`,
`.asf`, `.avi`, `.flv`, `.m2ts`, `.m4v`, `.mkv`, `.mov`, `.mp4`, `.mpeg`,
`.mpg`, `.mts`, `.ts` and `.wmv`. Every file is probed once, and only an Exact
or Strong identification proceeds by default. The gate is editable under
**Settings → Identification**.

Filing writes a Jellyfin Movies layout under the configured Library root:
`<Site>/<Site> - <yyyy-MM-dd> - <Title>/`, omitting the date segment when prdb
does not know it. A first Video File uses the entry name; multiple qualities
are grouped with ` - [<quality>]`. Each entry also gets `movie.nfo` and, when
cached artwork exists, `fanart.jpg`. Later prdb corrections refresh those two
files, but never recompute or rename a recorded Video File path.

A move on one filesystem is a rename. Across filesystems, prdb-fab copies to a
hidden `.filing-<download id>.part` beside the destination, flushes it, compares
every byte with the source, renames it into place and only then deletes the
source. A stopped container resumes from the durable intended path. Give the
container enough free space for one full temporary copy when downloads and the
library are on different filesystems.

Files that cannot safely proceed wait in **Review queue**. Dismiss leaves the
file untouched; Delete rechecks its exact path and size before deleting it.
Only Unidentified, Duplicate and Entry Missing rows additionally offer File as,
Replace and File as only copy respectively. **Library** shows held entries and
their recorded files; **Operation log** shows newest-first Filed, Replaced,
Deleted and Tidied acts.

After every Video File from directory-shaped SABnzbd storage has reached a
decision, tidy-up may remove only these fixed leftover types: `.nfo`, `.par2`,
`.sfv`, `.srr`, `.url`, `.txt`, `.jpg` and `.png`. It then removes empty
directories. This is enabled by default under **Settings → Library**. Unknown
files are retained, and a single-file storage path is never widened to its
parent directory.

The SABnzbd boundary is unchanged: prdb-fab performs only the initial `addfile`
and never calls SABnzbd retry or delete.

## Status and reporting

**Status** reads the local database every five seconds and never turns a page
view into a remote request. It follows the six stages Sync (prdb), Sync
(Indexers), Match, Decide, Download and File. A **Gap** needs repair and counts
in the headline. A **Brake** is a gate, budget or human decision doing exactly
what it was configured to do; it explains the choice and links to its owner,
but does not make a healthy installation look broken. The last-useful-act line
shows whether the loop has recently filed a file, started a Download or added a
Release to the cache. Recent Window gaps name a source whose initial fill has
not completed or whose last complete proof is more than a day old.

**Run now** only changes a routine's ordinary due time. The same lane executes
it, so prdb's Governor, an Indexer's Daily Query Budget, permanent refusals and
an empty work set still cannot be bypassed. A second click cannot overlap the
first request, and the accepted, deferred or refused result stays visible.

Reporting is disabled by default under **Settings → Reporting**. Its two
switches are independent: one reports Fulfilments for locally held Wanted
Videos, and one reports file-to-Video assignments that a person explicitly
confirmed in the Review Queue. Switching a channel off stops outbound reports
from that channel while leaving its pending local differences intact. It does
not retract reports prdb already accepted. The exact fields and boundaries are
documented in [privacy.md](privacy.md).

## The mounts

| Mount | What it is |
| --- | --- |
| `/data` | The tool's own state: the SQLite database, the log, and the cached artwork. |
| Your media | Whatever you mount your downloads and your library from. The paths inside the container are yours to choose. |

Two things are worth getting right.

**Keep `/data` on local storage.** SQLite on an SMB or NFS share is a way to
corrupt a database, not a way to back one up. Back the directory up by copying
it; do not run the tool out of a network share.

**What grows on `/data`.** Three things: the database, the log, and the cached
artwork. The log is capped near a hundred megabytes and rolls. The database has
count ceilings with one deliberate floor: rows inside the 90-day Recent Window
are protected even when outside activity makes the cache exceed a ceiling.

| | Ceiling | What happens at it |
| --- | --- | --- |
| The local copy of prdb's catalogue | **50,000 videos** — tens of megabytes with their pre-names, credits and image records | The oldest rows outside the Recent Window that nothing points at are dropped, a few hundred at a time |
| Each Indexer Cache | **100,000 Releases per Indexer** | The oldest examined Releases outside the Recent Window that nothing points at are dropped; unseen and still-wanted Releases are also protected |
| Cached artwork | **2 GiB**, and only for videos you are merely browsing | The pictures served longest ago are deleted; the next time you scroll past one it is fetched again |

These ceilings are fixed rather than settings. None can reach what you have
marked as wanted or what you hold: those rows stay whatever the ceilings say,
and the artwork of a wanted video is not counted against the 2 GiB at all.
Reaching a ceiling is ordinary; disposable rows or pictures are read again if
they are needed again.

### Indexer queries and the Recent Window

The browser reads the Indexer Cache. Reloading, changing a filter or opening a
Release table sends no request to an Indexer; background routines own that
budget.

Each newly added Indexer starts with a **Daily Query Budget of 1,000 requests**,
reset at midnight UTC. When at least one Wanted Video has a searchable title,
the Wanted Sweep reserves half of the budget, capped at **480 requests per
day** — five titles every fifteen minutes. The Indexer Walk uses the remaining
share. With no searchable Wanted work, the walk may use the whole budget. One
Indexer never spends another's allowance.

The fast Indexer Walk extends the cache with newly visible Releases. Beside it,
a complete pass pages through **90 days** of the configured categories, 100
Releases at a time, and repeats at least daily. That second pass finds Releases
which became visible late or were missed during an outage. A busy Indexer can
cost hundreds of queries on its first day, and a very large history can continue
on the next. The saved page advances only after its batch commits, so a restart
resumes rather than beginning again, and neither pass exceeds that Indexer's
daily budget.

The Wanted Sweep searches older Wanted Videos directly by title, which is why
the two routines need separate shares of the same budget. Both write the same
cache; neither identifies a Release by itself. prdb remains the only authority
that assigns a Video.

The Releases view shows the last completion or failure of each Wanted Sweep,
Screening, Backwards Screening and Release Identification routine. **Run now**
makes one due immediately; it does not bypass the routine's lane, the Governor
or the Indexer's Daily Query Budget.

**None of it is in a backup**, and it does not need to be — every row and image
can be read from prdb or the Indexers again. A restored installation shows
browse surfaces and Release tables that fill in while the Recent Window proof
runs again.

**Mount your media at the same path your downloader sees it at, if you can.**
The path mapping is then the identity, and a mapping that does not resolve is
the most common failure this kind of tool has. Mounting one parent directory
that holds both downloads and library, as `/srv/media:/media` does above, is the
simplest way to be sure.

## PUID, PGID and umask

The container starts as root, works out which identity your files should belong
to, hands its own data directory over to that identity, and drops to it before
the application starts. It does not touch the ownership of your media: files
keep the owner they arrived with, and `UMASK` decides who can read what the tool
writes.

Set `PUID` and `PGID` to the user your library belongs to. `id -u` and `id -g`
on the host answer both.

## Environment variables

| Variable | Default | What it does |
| --- | --- | --- |
| `PUID` / `PGID` | `1000` | The identity the application runs as. |
| `UMASK` | `022` | The permissions on what the tool writes. |
| `FAB_DATA_DIRECTORY` | `/data` | Where the database and the log live. |
| `ASPNETCORE_HTTP_PORTS` | `8080` | The port inside the container. To reach it on another port, remap it on the host side (`-p 9000:8080`) instead. |
| `FAB_RESET_PASSWORD` | unset | Clears the password on the next start. See *If you lose the password* below, and remove it again afterwards. |

That is the whole list, and it is meant to stay that way: **a variable exists
only where the answer is needed before the application can start.**

Your prdb key, SABnzbd, your indexers and your library root are **not**
environment variables and never will be. They are answered in the browser and
kept in the database, which is what makes changing an indexer key a form rather
than an edit to this file and a restart. If you find yourself looking for
`PRDB_API_KEY`, there isn't one — that question is asked at the first run.

## The password, and the network it travels over

There is one password and no user name. It belongs to this installation rather
than to an account somewhere, it is stored hashed, and it is never shipped as a
default: an installation nobody has set one on is an installation anybody who
reaches it can claim.

**Over plain `http`, the password is sent across the network in the clear.**
That is worth saying without hedging. On a home LAN that you control, this is
the ordinary way to run a tool like this and the risk is one you can weigh.
Reaching it from anywhere else — a laptop on someone else's wifi, a port
forwarded through your router, a VPS — means the password, and every key you
type into the setup forms, are readable by anything on the path.

The session cookie is `HttpOnly` and `SameSite=Strict`, and it is marked
`Secure` when the request arrived over `https`. None of that encrypts anything:
those flags limit what a browser does with a cookie, not what a network can
read. If the tool has to be reachable from outside your own network, put
something in front of it that encrypts the connection — a reverse proxy holding
a certificate, a VPN, a tunnel. This tool does not terminate TLS itself, and
there is no recipe for one here: how you expose it is your own choice to make,
and a topology written down in this repository would read as the topology.

There is no API token and nothing mechanical can sign in. A browser session is
the only credential there is, which also means no health check or script can
reach anything except `/api/health`, which answers only that the process is up.

## If you lose the password

It is recovered at the host rather than over the network, because a second way
in over the network is a second way to configure wrongly. Start the container
once with the variable set:

```yaml
    environment:
      FAB_RESET_PASSWORD: "true"
```

On that start the tool **clears the password and ends every session**, logs
loudly that the variable should now be removed, and drops the installation back
into *set a password*. Open it in a browser and set a new one.

**Then remove the variable and restart.** While it is set, every start clears
the password again — including the one you just set.

Everything else survives untouched: your prdb key, SABnzbd, your indexers, your
library root and how far setting up had got. Losing the password costs the
password, and nothing else.

Changing a password you still know is done in the browser instead, under
**Settings → Account**. That asks for the current one and ends every other
session at once, which is the lever to pull if you suspect a session you did not
open.

## The log

The container writes what it did and why at a level that suits a tool left
alone, in two places at once: the container's own output, and a rolling file
under `/data/logs/`. The file is there so that *send me your log* is a matter of
copying a file out of the directory you already mounted, rather than knowing
what `docker logs` is. It is capped near a hundred megabytes and rolls itself.

When something needs explaining — and particularly when the answer is "nothing
happened", which is the hard case to tell from a failure — the tool's own
reasoning can be turned on:

```
-e 'Logging__LogLevel__Prdb.Fab=Debug'
```

That adds the lines that say why a run did nothing, and one line per request
from the browser with it. Worth turning off again afterwards: Debug is a great
deal of log for a tool that runs for months.

The first line of every log names the version that produced it. Please leave it
in when you send one.

**Secrets never appear in the log**, at any level. No key, no passphrase, and no
URL — what is written is which connection was being used and to which host,
never the address itself, because an indexer's address carries its key.

## Tags and architectures

Images are published for `linux/amd64` and `linux/arm64`, which covers x86 NAS
hardware and the ARM boards and newer Synology models alike.

| Tag | What it points at |
| --- | --- |
| `0.14.2` | A release. This is what documentation and Compose files should pin. |
| `latest` | The tip of the default branch. Fine for trying the tool out, a poor idea for something that runs unattended. |
| `<commit sha>` | Exactly one commit. Useful for reproducing a report. |

Anonymous pulls from Docker Hub are rate-limited per IP address. A NAS that
pulls on a schedule, or a household behind one address, can run into that: the
symptom is a pull that fails with `toomanyrequests`, and the fix is to log in to
Docker Hub on the host or to pull less often. It is not a broken image.

## Stopping and updating

`docker stop` sends `SIGTERM`, which the tool receives directly and acts on: it
finishes what it is in the middle of rather than being killed once the timeout
runs out. That will matter more later, when what it is in the middle of is
moving one of your files.

To update, pull the new version and recreate the container. The schema is
migrated at startup, and a migration that cannot be applied stops the tool
rather than letting it run against a database it does not understand.

**Copy `/data` before you change the tag.** Migrations only go forward: once a
newer version has started against your data directory, an older image finds a
database it does not know. That is what pinning a version protects you from
going forward, and what a copy protects you from going back.

Read the release notes first. Before 1.0, a minor version may change behaviour.

## When something goes wrong

Start with the log — `docker logs prdb-fab`, or the newest file under
`/data/logs/`. Then, if you are reporting it, turn `Logging__LogLevel__Prdb.Fab`
up to `Debug`, reproduce it, and attach the file.
