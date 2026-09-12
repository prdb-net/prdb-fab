# Releasing

What a release of this tool is, what proves it works, and what a machine cannot
prove for us.

## What CI already holds

Everything below runs on every pull request and on every push to `main`, so a
release is mostly a matter of tagging something that was already green.

- **The suites.** `dotnet build` and `dotnet test` across the three projects,
  including the whole backup round trip: a populated installation becomes a
  document, the document becomes an empty installation with both mounts moved
  somewhere else, and every exported table is compared. A recorded document from
  an older format is restored beside it, so a renamed field fails a test rather
  than a restore.
- **The API contract.** The committed OpenAPI document and the generated
  TypeScript types are regenerated and compared. A changed endpoint that was not
  regenerated is a red build.
- **The image, on both published architectures**, each built and started on a
  native runner: it comes up and migrates, what it writes belongs to
  `PUID:PGID`, a dotted logging category survives the entrypoint, the rolling
  log lands on the data volume, `docker stop` reaches PID 1 — and the backup
  loop across two containers, where one writes a file and a second one restores
  it and signs in with the password that came back in it.

`docker/smoke-test.sh` is that last one, and it runs locally the same way:

```sh
docker buildx build --load --tag prdb-fab:local .
docker/smoke-test.sh prdb-fab:local
```

## What a machine cannot hold

Three of the four outside services this tool sits between are somebody's
account: prdb, an indexer, a Usenet provider behind SABnzbd. Nothing in a public
CI can have them, and a suite that faked them would be proving something about
the fakes. So the following is checked by hand, once, against real services,
before a release is tagged — and it is the shortest list that still covers the
loop end to end.

Start from an **empty data directory** on the tag being released.

1. **Onboarding completes.** The prdb key is accepted; a wrong one is refused
   with a sentence that distinguishes a bad key from an account whose tier has
   no API access. SABnzbd's path mapping verifies against a directory this
   container can actually open. One indexer is accepted on a real search.
   Setting up ends on the wanted list with the first sync visibly running.
2. **A manual download, all the way.** Find a wanted video, open its releases,
   send one to SABnzbd, and watch it through Downloads to Collected — then find
   the file in the library in the Jellyfin layout, with its `movie.nfo` and
   `fanart.jpg` beside it, and the move recorded in the operation log.
3. **An automatic download.** Add one permission rule over the wanted list, and
   confirm a matched release is fetched without being asked, that Status names
   the rule that permitted it, and that removing the video from Wanted abandons
   the download locally without touching the SABnzbd job.
4. **A visible failure at each stage.** Turn off SABnzbd and confirm Status says
   so rather than the queue going quiet; give an indexer a wrong key and confirm
   the gap names it; point the library root at a directory the container cannot
   write and confirm it is refused before it is stored.
5. **Backup and restore against real content.** Export, start a second container
   with the same library mounted at a *different* path, restore, answer the two
   roots with the new paths, and confirm the library reads the same and the
   verification pass confirms rather than counts. Then do it once with the
   library deliberately not mounted, and confirm nothing is deleted, nothing is
   downloaded again, and Status carries the count.

Step 5's second half is the one worth not skipping. It is the only check that a
mis-mounted library reads as *held and unconfirmed* rather than as an empty
library, and an empty library under an automation rule is a standing instruction
to fetch the collection again.

## Cutting the release

1. `CHANGELOG.md` gets its section, written for somebody using the tool rather
   than reading the diff. A renamed setting or a different default belongs in it
   however small; a refactor does not.
2. `VersionPrefix` in `Directory.Build.props`, and the pinned tag in
   `README.md`, `docker-compose.yml` and `docs/running-in-docker.md`, all move
   together.
3. Merge, then tag `vX.Y.Z` and publish a GitHub release. The publish workflow
   builds both architectures from the tag and pushes `X.Y.Z` beside the commit
   SHA and `latest`.

Before 1.0 a minor version may change behaviour, and the changelog says so at
the top. The version names the user-facing contract — the settings, the API the
browser is built against, the backup format, the layout written into a library —
and not the database schema, which is migrated forward at startup and which
nobody acts on.
