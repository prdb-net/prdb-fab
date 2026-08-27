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

Nothing released yet. The repository holds its licence, its project
documentation, and the walking skeleton the first release will be built on.

The skeleton starts, migrates its SQLite database in the directory
`FAB_DATA_DIRECTORY` names — `/data` by default — serves one page, and turns one
scheduled routine in one lane. Nothing is searched for, downloaded, identified
or filed yet, and there is no onboarding and no password.

What is real underneath it, and is what the first feature will be built on:

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
