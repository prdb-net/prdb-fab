# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Entries describe what changed for someone *using* the tool. A refactor that
moves a thousand lines but changes no behaviour is not worth an entry; a renamed
setting or a different default is, however small the diff.

The version names the user-facing contract — the settings, the API the browser
is built against, the export format, the layout written into a library — and not
the database schema, which is migrated forward at startup and which nobody acts
on. Until the first release the version is `0.x`, and a minor bump may break
things; see SemVer's clause on initial development.

**Migrations only go forward.** Once a newer version has started against a data
directory, an older image finds a database it does not understand. Copy `/data`
before changing the tag — the backup file is deliberately not the whole of it.

## [Unreleased]

Nothing released yet. What the image does today is let you set a password,
configure the tool, and correct what you configured. The loop does not run:
nothing is synced, searched, downloaded, identified or filed, and a finished
setup is a tool that is ready and idle.

New, and the whole of what a user sees:

- **A password**, set at the first run and never shipped as a default. One
  field, no user name, and the form that sets it is offered only while no
  password exists — so whoever reaches the tool first claims it. A sign-in is
  rate-limited, and the session behind it survives a restart and can be ended.
  Over plain `http` the password travels in the clear, which
  `docs/running-in-docker.md` says plainly rather than papering over with cookie
  flags.
- **A guided setup** that asks for the prdb API key, SABnzbd, the indexers and
  the library root, in that order. Each step is stored the moment it is
  answered, so closing the tab costs nothing and a restarted container resumes
  on the same step. Setting up finishes and does not come back.
- **Every connection is checked against the real service before it is
  accepted**, and one that fails is not stored — no *continue anyway*. prdb
  answers with four different verdicts rather than one message; SABnzbd is
  checked with a call that actually carries the key; an indexer is checked with
  a real search, because most of them answer a capabilities call to anybody. The
  path from SABnzbd's finished folder into this container is verified rather
  than collected.
- **SABnzbd and the indexers can be skipped**, with what that costs said at the
  moment you skip it — and what is missing is recorded rather than forgotten.
  The prdb key and the library root cannot be skipped.
- **Settings**, at `/settings`: the same forms again, wrapped in *save*, with a
  route per connection and one per indexer. Keys are write-only — the field is
  empty with a marker saying one is set, and saving it empty keeps the stored
  key. Nothing needs a restart.
- **Changing the password** ends every other session at once, and asks for the
  current one to do it.
- **`FAB_RESET_PASSWORD=true`** clears the password and every session on the
  next start and leaves every other credential standing, for when the password
  is lost and the browser cannot help. It has to be removed again afterwards,
  and the log says so.

Underneath it, and what the rest is being built on:

- **Four projects and one reference direction**, with `Core` holding the rules
  and reaching nothing. Five architecture tests fail the build on a violation
  that would otherwise compile and run: a dependency in `Core`, a library
  depending on the host, `Core` touching the filesystem, anything reading the
  clock without `TimeProvider`, and a whole URL in a log message.
- **SQLite opened in WAL**, with `synchronous`, `busy_timeout` and
  `foreign_keys` set on every connection rather than once at startup, because
  the pool hands a connection back in whatever state it was left in.
- **A schedule**: a routine row that is the only truth about what is due, one
  worker per lane, a run log capped at fifty runs per routine, and three
  outcomes — succeeded, failed, and interrupted by a restart. A tick that found
  nothing to do is not a run and is not recorded.
- **Logging to two places at once**: the container's output and a rolling file
  under `/data/logs/`, capped near a hundred megabytes, so that sending a log is
  copying a file. `Logging__LogLevel__Prdb.Fab=Debug` turns the tool's own
  reasoning on. The first line names the version that produced the log.
- **An API the frontend's types are generated from**, committed, with CI failing
  when the two drift apart.
- **An image for linux/amd64 and linux/arm64** that drops to `PUID:PGID`, leaves
  the rest of the media alone, and stops when it is asked to. CI starts it on
  both architectures rather than only building it.
