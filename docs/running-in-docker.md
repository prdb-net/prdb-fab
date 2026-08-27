# Running prdb-fab in Docker

Docker Compose is the supported way to run this. Not one option among several —
the way.

> **This is early software.** What is in the image today starts, migrates its
> database, serves one page and turns one scheduled routine. It does not yet
> search, download or file anything.

## The quickstart

```yaml
services:
  prdb-fab:
    image: prdbnet/prdb-fab:0.1.0
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

## The mounts

| Mount | What it is |
| --- | --- |
| `/data` | The tool's own state: the SQLite database and the log. |
| Your media | Whatever you mount your downloads and your library from. The paths inside the container are yours to choose. |

Two things are worth getting right.

**Keep `/data` on local storage.** SQLite on an SMB or NFS share is a way to
corrupt a database, not a way to back one up. Back the directory up by copying
it; do not run the tool out of a network share.

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

Everything else the tool needs is answered in the browser and stored in the
database.

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
| `0.1.0` | A release. This is what documentation and Compose files should pin. |
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
