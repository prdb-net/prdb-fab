# Running prdb-fab in Docker

Docker Compose is the supported way to run this. Not one option among several —
the way.

> **This is early software.** What is in the image today asks for a password,
> takes you through setting up, checks every connection you give it against the
> service it names, and then keeps a local copy of the part of prdb you point it
> at. It does not yet search, download or file anything — so a finished setup is
> a tool that reads prdb and shows you what it read.

## The quickstart

```yaml
services:
  prdb-fab:
    image: prdbnet/prdb-fab:0.2.0
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
artwork. The log is capped near a hundred megabytes and rolls. The other two are
bounded by a number rather than by a duration, because how much disk a duration
implies is not something anyone can predict.

| | Ceiling | What happens at it |
| --- | --- | --- |
| The local copy of prdb's catalogue | **50,000 videos** — tens of megabytes with their pre-names, credits and image records | The oldest rows nothing points at are dropped, a few hundred at a time |
| Cached artwork | **2 GiB**, and only for videos you are merely browsing | The pictures served longest ago are deleted; the next time you scroll past one it is fetched again |

Neither is a setting, and neither can reach what you have marked as wanted or
what you hold: those are kept whatever the ceilings say, and the artwork of a
wanted video is not counted against the 2 GiB at all. Reaching a ceiling is
ordinary and costs you nothing but a picture fetched twice.

**None of it is in a backup**, and it does not need to be — every row and every
image can be read from prdb again. A restored installation shows a library that
fills in over the following minutes.

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
| `0.2.0` | A release. This is what documentation and Compose files should pin. |
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
