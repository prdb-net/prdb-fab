# The container is given what it needs before it starts, and nothing else

Six environment variables, two mounts, one port. Everything else is answered in
the browser, because
[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)
spent its argument on getting onboarding down to a prdb key and a library root,
and a Compose file that asks the same questions again would take it back.

`prdb-ordeno` has already built this image and measured the traps in it. Its
answers are adopted rather than re-derived, and the three additions this tool
needs are the ones that follow from downloading: a second mount whose path has
to agree with SABnzbd's, a data volume that grows, and a file lane that may be
hours into a copy when the container is stopped.

## The admission rule

**A variable exists only where the answer is needed before the application can
start.** Everything else is a setting, and
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)
already decided where settings live.

This is the same shape as that ADR's own test and
[ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)'s,
and it exists for the same reason: the environment is the surface most likely to
accumulate things nobody argued for, because adding one is free and removing one
breaks somebody's Compose file forever.

Six pass:

| Variable | Why it cannot wait |
|---|---|
| `FAB_DATA_DIRECTORY` | the database is opened before anything is served |
| `ASPNETCORE_HTTP_PORTS` | the listener exists before the first request |
| `PUID`, `PGID` | the identity files are written under, fixed before the process drops to it |
| `UMASK` | the same, for the permissions those files get |
| `FAB_RESET_PASSWORD` | it exists precisely for when the browser cannot be reached (ADR 0010) |

The last one is worth stating as the rule's clearest case rather than its
exception: a password reset **cannot** be a setting, because the person who
needs it is the person who cannot sign in.

Everything else fails the test and is an onboarding step: the prdb key, the
library root, the SABnzbd URL, key, category and path mapping, every indexer,
every automation rule, every switch ADR 0020 admitted. `VISION.md` already
promised this — "changing an indexer key is a form, not a YAML edit and a
restart" — and the rule is how that promise stays true as things are added.

## The image, adopted from `prdb-ordeno`

`prdb-ordeno` is a sibling project that ships this image already. Its build is
measured rather than reasoned, so it is taken whole:

- Debian-based `mcr.microsoft.com/dotnet/aspnet:10.0`, with **`ffmpeg` from the
  distribution** rather than a static build maintained here.
  [ADR 0005](0005-the-first-release-files-into-the-jellyfin-layout.md) puts
  `ffprobe` in the runtime image from the start, and `ffmpeg` is how it arrives.
- **`util-linux`**, for `setpriv`.
- Multi-stage, with the frontend build and the .NET publish both on
  `$BUILDPLATFORM` and **no runtime identifier**, so one framework-dependent
  publish serves both architectures and the only thing emulated on an arm64
  build is `apt-get`.
- **linux/amd64 and linux/arm64**, because `VISION.md`'s user has a NAS.

Three of its decisions are traps that are only found once, and they are recorded
here with their reasons so that nobody undoes them tidying up:

**No `VOLUME` declaration for the data directory.** An anonymous volume makes a
forgotten mount look like it worked — until the container is replaced and the
password, the settings and the record of everything filed go with it. Failing at
the moment the mount is missing is the whole point.

**The entrypoint is `bash`, not `sh`.** `/bin/sh` here is dash, and dash drops
environment variables whose names are not valid shell identifiers rather than
passing them on. Every .NET logging category has a dot in it, so under dash the
one setting anybody is ever asked to change while diagnosing a problem never
reaches the application — and nothing says so, because `docker exec env` shows
the container's configured environment rather than the process's.

**`exec setpriv`, with no supervisor process.** The application becomes PID 1,
so `docker stop` reaches it directly. A supervisor between the two means the
signal arrives late or not at all, and the container is killed on the timeout
instead.

The entrypoint's own behaviour is adopted with it: refuse a `UMASK` that is not
octal and a `PUID`/`PGID` that is not numeric, rather than failing obscurely
later; reuse the name of an id that already exists in the image rather than
adding a second name for it, which is what makes `ls -l` inside the container
disagree with `ls -l` on the NAS; take ownership of **the data directory only**,
never the library, because a recursive `chown` over somebody's media is slow on
a NAS and is not this tool's business; say plainly and carry on when a share
refuses the `chown`; and leave everything alone when the container was started
with Compose's own `user:`, because that person has answered the question
already.

## What `docker stop` interrupts, and why ten seconds is enough

This is the one place the image argument differs from `prdb-ordeno`'s, and it
looks at first like a reason to ask for a longer stop timeout.

The **file** lane ([ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md))
may be hours into a cross-filesystem copy of a 40 GB release when the signal
arrives. Ten seconds is not enough to finish it, and no timeout anybody would
configure would be.

**No longer grace period is asked for, and the reason is a decision rather than
luck.** ADR 0026 made recovery a rule over the arriving file row: the intended
path is written **before anything on disk is touched**, and the row only reaches
`Filed` once the source is gone. So a copy killed halfway leaves the row in
`Filing`, and the next start either finds our bytes at the intended path and
completes, or finds nothing and starts the move over. Nothing depends on a
graceful shutdown, which is what lets the default stand.

What can be left behind is a `.filing-<download id>.part` in an entry directory,
and
[ADR 0017](0017-the-filed-path-is-computed-once-and-then-recorded-rather-than-recomputed.md)
chose that name so it is attributable rather than anonymous — invisible to
Jellyfin's scanner and unreachable by its grouping rule. The documentation says
what it is; nothing has to clean it up urgently.

## Two mounts, and the second one has to agree with SABnzbd

`/data` for the tool's own state, and **one media mount holding both the
download directory and the library**.

One mount rather than two, because a move within one filesystem is a rename and
a move across two is a copy, a verification and a delete — ADR 0026 priced that
at hours for a large release, and `VISION.md` requires the documentation to say
so "because that is the difference between instant and overnight". Which
directories under the mount are which is answered in the browser: ADR 0010
derives the download directory from the verified path mapping, and its library
step **refuses** a root that lies inside the download directory or contains it,
and **warns** without refusing when the two are on different filesystems.

**The example runs SABnzbd beside this tool with the same mount at the same
path.** That is the substantive recommendation of this decision, and it targets
the single most common failure in the whole tool.

ADR 0010 collects a path mapping because SABnzbd reports paths as it sees them,
and ADR 0016 records that nearly every report of "it downloaded and then nothing
happened" is either a broken mapping or a single-file release. When both
containers mount `/srv/media` at `/media`, the mapping is the identity and the
entire class of failure does not exist. It costs nothing to arrange for someone
setting up fresh, and the two are almost always deployed together.

Where the paths genuinely cannot agree — SABnzbd on another host, an existing
installation nobody wants to re-mount — the documentation has to teach the three
views of one filesystem: SABnzbd's container, this container, and the host. That
is a real explanation and it is the reason `docs/running-in-docker.md` exists
rather than a README section.

## What grows on the data volume, with numbers

[ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md)
makes this answerable for the first time, and it has to be answered because the
volume is the one thing the user provisions.

- **The operation log and the download rows** grow with the library and are
  never pruned ([ADR 0029](0029-the-operation-log-records-one-act-per-video-file-and-nothing-reads-it-back.md),
  ADR 0016). At a library of five thousand entries, tens of megabytes.
- **The indexer cache** is bounded at 100 000 rows per indexer
  ([ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md)).
- **The artwork cache** is what is pinned — proportional to the library and the
  wanted list — plus a 2 GiB ceiling for everything else
  ([ADR 0030](0030-artwork-is-cached-by-image-id-fetched-for-what-is-pinned-and-on-sight-for-the-rest.md)).
- **The catalogue** is bounded by its own row ceiling (ADR 0013).

A few gigabytes, and **none of it is in the backup**. That last part is the half
people get wrong in the other direction: a person who has backed up their data
volume has backed up more than the backup file holds, and a person who holds
only the backup file has everything that matters. Both need saying.

## Three documents, for three moments

**`README.md`** — what the tool is, and the one prerequisite `VISION.md`
requires to be stated *before* installation rather than discovered at first run:
without a prdb API key, setup cannot be completed. Then the quickstart.

**`docs/running-in-docker.md`** — the operational document, in `prdb-ordeno`'s
shape: the mounts, PUID/PGID and umask, network shares, the environment
variables, turning the log up, losing the password, tags and architectures,
stopping and updating, and what to do when something goes wrong.

**The onboarding itself** — and this is why the documentation can be shorter
than it looks. ADR 0020 made the connection forms *be* the onboarding steps,
with four distinct verdicts and a verified path mapping, so anything the tool
checks does not have to be documented: it is diagnosed at the moment it is
wrong, by the thing that knows. What must be documented is what cannot be
checked.

**The Compose file in the repository is an example to copy, not a file to run**,
and it is written that way. It pins a version rather than `latest`, for
`prdb-ordeno`'s reason and one of our own: an unattended tool that moves files
and upgrades itself the next time the NAS restarts is a surprise — and this one
also *downloads*.

## What has to be said out loud, because it cannot be discovered

The list, gathered from the decisions that each left a requirement here without
a document to hold it:

1. **The prdb key is not optional**, and setup cannot complete without a working
   one. Before installation (`VISION.md`).
2. **`FAB_RESET_PASSWORD`** is the only way back in, it is set for one start and
   then removed, and there is no second sign-in path and no trusted proxy header
   (ADR 0010).
3. **A backup passphrase cannot be recovered**
   ([ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md)).
4. **The tool owns the sidecar and the entry image and overwrites both**, so a
   hand edit does not survive — by filing, or by the next repair pass that finds
   a difference ([ADR 0027](0027-the-sidecar-and-the-entry-image-are-overwritten-until-they-match-the-catalogue.md)).
5. **The download directory and the library must be different directories**, and
   should share a filesystem unless overnight is acceptable (`VISION.md`,
   ADR 0026).
6. **Leftovers are deleted** from the download directory once nothing in it is
   still undecided, under a switch that ships **on** (ADR 0005), and the list is
   fixed rather than something patterns are written into.
7. **PUID, PGID and umask**, which `VISION.md` names as the thing people get
   wrong, together with the fact that filed files keep the umask's permissions
   and the media keeps the owner it arrived with.
8. **What grows on the data volume, and that none of it is in the backup.**

## Considered options

**A Compose file the repository is meant to be run from.** Rejected, as
`prdb-ordeno` rejected it: it invites `git pull` as an upgrade path, it cannot
know anyone's mounts, and a file that is almost right is worse than an example
that is obviously one.

**`latest` as the documented tag.** Rejected under *three documents*: the tool
downloads and files unattended, and a version that changes when the NAS reboots
changes what it does to somebody's disk without anybody choosing a moment.

**Ask for a longer `docker stop` timeout so a copy can finish.** Rejected under
*what `docker stop` interrupts*: no configurable timeout finishes a 40 GB copy,
and ADR 0026 already made the interruption safe. Asking for one would suggest
that the safety depends on it.

**Environment variables for the prdb key and the library root, so a fresh
container comes up configured.** Rejected under the admission rule, and it is
the tempting one because it makes a demo one command long. It puts the key in
the Compose file and in the process environment — the objection ADR 0010 already
made against the password — and it creates two sources of truth for settings the
UI also writes.

**Declare a `VOLUME` for `/data`.** Rejected: it converts a missing mount from
an immediate failure into a silent one that surfaces when the container is
replaced.

**A second image without `ffmpeg`, for people who do not want it.** Rejected:
ADR 0005 reads quality from the file, so there is no configuration in which this
tool works without `ffprobe`. Two images would be one that works and one that
does not.

**A reverse-proxy recipe, TLS, or a hardening guide.** Rejected as out of scope
here: `VISION.md` settles the posture — "whoever exposes this to the internet
has made their own choice" — and a recipe in this repository would read as an
endorsement of one topology.

**Bare-metal and Kubernetes instructions.** Rejected: `VISION.md` calls Docker
Compose "the supported way to run it. Not one option among several — the way",
and documenting a second path is how that stops being true.

## Consequences

- **`VISION.md` gains one sentence**: run SABnzbd and this tool with the same
  media mount at the same path, and the path mapping is the identity. It is the
  cheapest thing anyone can do to avoid the failure ADR 0016 names as the most
  common.
- **`CONTEXT.md` is unchanged.** No new term: the image, the Compose file and
  the documentation are artefacts, not concepts the language needed.
- **The image is fixed**: Debian, `ffmpeg` from the distribution, `util-linux`,
  no Node at runtime, no `VOLUME`, a bash entrypoint that `exec setpriv`s, two
  architectures, one framework-dependent publish.
- **Six environment variables**, and a rule that decides the seventh when
  somebody proposes it.
- **Two mounts and one port**, with the media mount's shape recommended rather
  than enforced — the tool works with two mounts and says so during onboarding.
- **The implementation writes the files.** This decision fixes what they contain
  and what they must teach; the `Dockerfile`, the entrypoint, the example
  Compose file and `docs/running-in-docker.md` are the build's work, and
  `prdb-ordeno`'s are the reference to start from.
- **The map reaches its destination.** This was the last open question, and
  nothing is left to decide before implementation starts.
