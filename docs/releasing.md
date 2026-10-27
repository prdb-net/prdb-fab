# Releasing

How CI validates a release and how to publish it.

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

## Release validation

Successful CI is the required release validation. Manual tests against live
prdb, indexers or SABnzbd are not required before tagging or publishing a
release. No exception or additional approval to omit them is needed.

CI covers the application contracts, migrations, backup and restore, and
container startup on both supported architectures. It does not prove that a
particular user's external accounts, network or storage mounts are configured
correctly. Live checks may be useful for investigating a specific problem, but
are optional and are performed only when explicitly requested. They do not gate
routine releases.

## Cutting the release

1. `CHANGELOG.md` gets its section, written for somebody using the tool rather
   than reading the diff. A renamed setting or a different default belongs in it
   however small; a refactor does not.
2. `VersionPrefix` in `Directory.Build.props`, and the pinned tag in
   `README.md`, `docker-compose.yml` and `docs/running-in-docker.md`, all move
   together.
3. Confirm that all required CI checks pass, then merge. Tag `vX.Y.Z` and publish a GitHub release. The publish workflow
   builds both architectures from the tag and pushes `X.Y.Z` beside the commit
   SHA and `latest`.
4. Verify that the publish workflow succeeds and that the versioned image is
   available for both architectures before reporting the release complete.

Before 1.0 a minor version may change behaviour, and the changelog says so at
the top. The version names the user-facing contract — the settings, the API the
browser is built against, the backup format, the layout written into a library —
and not the database schema, which is migrated forward at startup and which
nobody acts on.
