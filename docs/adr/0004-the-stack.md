# The stack: .NET 10, SQLite through EF Core, React

The backend is .NET 10, local state is SQLite accessed through EF Core with
migrations applied at startup, and the frontend is React that builds to static
assets the backend serves.

## Why .NET

prdb's hashing library, `Prdb.Hashing`, exists in C# and nowhere else, and the
values it produces have to be bit-identical or they match nothing at all.
Anything but C# means porting and then maintaining that against a specification
this repository does not own. `Prdb.Sdk` is a C# package on the same terms, and
prdb itself is .NET 10, so the toolchain already exists next door. This reopens
only if hashing reaches a second ecosystem.

## Why SQLite

This is a single-user application delivered as one container. A database that
costs the user a second container and a password before the first sync runs has
broken the promise the tool was installed for, in exchange for concurrency that
one process and one writer do not need. EF Core is here for its migrations; a
schema that will change for years is the part nobody should rebuild by hand.

## Why React building to static assets

The runtime image carries no Node. A framework with a server-side runtime of its
own would add a second runtime to the image for a UI that is a handful of
screens over an HTTP API.

## Consequences

- SQLite takes one writer at a time, and this tool writes more than a filing
  tool does: the indexer cache is refilled continuously and is by far the
  largest table. Sync work commits in batches, and a transaction never spans an
  HTTP request to an indexer or to prdb.
- Whether searching that cache by title needs SQLite's full-text search is a
  question this decision creates and does not answer.
- The database file lives in the mounted data volume, so a rebuilt container
  keeps its state — and the backup export has a defined place to read from.
- Migrations run at startup against a database the user cannot be expected to
  restore. A failing migration stops the tool rather than running against a
  schema it does not understand.
- `dotnet build` and `dotnet test` are the verification commands, against an SDK
  version pinned in `global.json`.
