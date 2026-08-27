# prdb-fab

Fetch and Build (FaB): find your favourite content on Usenet with prdb's help,
download it through SABnzbd, and build a sorted library out of what arrives.

Self-hosted, Docker Compose, single user. A prdb API key is required.

> **This is early software.** What is in the image today asks for a password,
> takes you through setting up, checks every connection you give it against the
> service it names, and then keeps a local copy of the part of prdb you point it
> at — with two pages to look at it. It does not yet search, download or file
> anything.

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
picture per video. Two pages show it — **what's new**, which is where it lands,
and **wanted**, which is what you have marked in prdb. Marking happens there;
this reads that list and never writes to it.

Nothing is searched for, downloaded or filed yet.

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
