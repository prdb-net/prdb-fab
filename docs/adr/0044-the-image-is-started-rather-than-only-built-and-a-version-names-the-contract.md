# The image is started rather than only built, and a version names the contract

`prdb-ordeno`'s two workflows are adopted whole — a CI job that builds, tests
and checks the API contract, and a publish job that pushes a multi-architecture
image to Docker Hub. Three things depart, and each of them is forced by a
decision this map has already taken rather than by taste: the ffmpeg step does
not come across, the arm64 image is started on a native runner, and this tool
stamps a version into itself because
[ADR 0043](0043-core-returns-its-reasons-and-the-log-is-a-file-the-user-can-send.md)
made its log a thing users send to strangers.

## What runs on a push, and the step that does not come with it

The chain is the sibling's, in its order: checkout, `setup-dotnet` from
`global.json`, `setup-node` with the lockfile cached, `npm ci`, `dotnet build`,
`dotnet test --no-build`, the API-contract check, the frontend lint, the
frontend build. [ADR 0004](0004-the-stack.md)
named the middle two as the verification commands and
[ADR 0040](0040-the-api-is-named-actions-and-a-verdict-is-a-success.md) requires
the contract check, so most of this is assembly rather than decision.

**The ffmpeg step does not come across**, and that is worth stating so nobody
copies it back in later. In `prdb-ordeno` it is the most elaborate thing in the
file — some sixty lines of shell around a nine-minute budget, sliced timeouts,
five attempts and a `dpkg --configure -a` between them — and it exists for one
reason its own comment gives: tests there read a picture size out of a real
file, so the runner needs the same pair the runtime image ships.

[ADR 0042](0042-nothing-reads-the-clock-directly-and-the-network-is-replaced-at-the-socket.md)
put **ffprobe's output for real files** on its written list of what is not
tested, on [ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)'s
argument that the file is read once and what is read decides nothing — so the
parsing is worth testing and the media is not. A build step whose only customer
is a test that does not exist is not a saving, it is a step that will
eventually go red for a reason nobody can act on. `ffmpeg` stays in the image,
where [ADR 0034](0034-the-container-is-given-what-it-needs-before-it-starts-and-nothing-else.md)
put it and where the smoke test below exercises it.

**Nothing else is fetched by the build.** `Prdb.Sdk` and `Prdb.Hashing` are
published to nuget.org and referenced as ordinary packages with pinned
versions, exactly as `prdb-ordeno` consumes them, so the SDK is a number in
`Directory.Packages.props` and not a submodule, a local build or a private
feed. `dotnet restore` is the whole of it.

## Both architectures, and only one of them emulated

The image is **built and started** on both published architectures on every
push. ADR 0042 handed *that the image runs* to this ticket in as many words,
and it was right to: the claims worth checking are ADR 0034's, and every one of
them is a property of a running container rather than of a layer. That it drops
to `PUID:PGID` and leaves the rest of the media alone. That `docker stop`
reaches PID 1 instead of the daemon killing it on a timeout. That a variable
with a dot in its name survives the entrypoint's shell, which is the whole
reason that entrypoint is `bash` and the only way the diagnostic knob ADR 0043
depends on can be shown to work at all.

**Both build times are measured into the job summary**, which is `prdb-ordeno`'s
arrangement and is kept for its reason: it deliberately left open whether
emulated arm64 builds stay affordable, and a number in every run is an answer
that maintains itself. Publishing stays one multi-platform `buildx` invocation,
so the second architecture pays only for the runtime stage — which is what
ADR 0034's structure was shaped to achieve.

**The arm64 image is started on a native `ubuntu-24.04-arm` runner**, and this
is the departure. GitHub made arm64 runners free for public repositories after
the sibling's decision was written, so the trade it was weighing no longer
exists. It matters more for starting than for building: under QEMU a
`setpriv`, a signal reaching PID 1 and a shell dropping an oddly named variable
are exactly the class of thing that goes wrong slowly and ambiguously, and a
smoke test whose failures cannot be told from emulation artefacts is a smoke
test nobody trusts.

## What a version means, and what it cannot undo

**Semantic versioning over the user-facing contract**, `0.x` until the first
release, where a minor may still break things.

The contract is what a person can act on: the settings and their meanings, the
API the frontend is built against, ADR 0009's export format, and the layout
this tool writes into a library. **The schema is not part of it.** It is
migrated forward at startup, the user never sees it, and versioning against it
would produce major bumps for changes nobody outside the build can observe.

### Forward only, and the release notes say so

[ADR 0004](0004-the-stack.md) stops the tool
on a failing migration rather than running against a half-migrated database the
user cannot be expected to repair. The consequence is one this decision has to
state rather than leave implied: **there is no way back.** Once a newer version
has started against `/data`, pinning an older tag gives that version a database
it does not understand, and ADR 0034's instruction to pin a version is only
safe advice while this is written down beside it.

So the operational sentence in the release notes is *copy `/data` before
updating*, and specifically not *take a backup first*. ADR 0034 already
established that ADR 0009's backup file deliberately holds only what cannot be
fetched again — a person holding one has the part that matters and not the
database, which is the wrong half for a rollback.

### The tool stamps its own version and says it first

`prdb-ordeno` stamps none: its `Directory.Build.props` carries no `<Version>`,
and the number exists only in a git tag and an image tag. That was defensible
there. It stopped being defensible here yesterday, when ADR 0043 made a rolling
file on the data volume the support channel and invited people to send it.

A log file without the version that produced it is a guessing exercise, and the
guess is being made by whoever is reading it rather than by whoever has the
installation. So the version is stamped at build time and the first Information
line at startup carries the version, the commit and the schema state — which is
also the first line of any log anybody attaches to anything, by construction.

## Docker Hub, and the changelog as the release notes

**`prdbnet/prdb-fab` on Docker Hub**, for the sibling's reason: this audience
finds images through Unraid's Community Applications, Portainer's search box and
a Synology dialog, and all three look there. Tags are adopted unchanged — the
full commit SHA always, because it is the one tag that means exactly one commit;
`latest` on the default branch; the version on a release.

Two of the sibling's smaller arrangements are adopted rather than rediscovered,
because both are the kind of thing that is otherwise learned from a red build:
the publish job is skipped in a fork, which has neither the account nor any
business publishing under this name; and absent credentials are a warning with
nothing published rather than a failure, since CI still builds and starts the
image on every push — what is missing then is distribution, not proof.

**The changelog is the release notes.** Keep a Changelog and semantic
versioning, with the sibling's rule about what earns an entry: what changed for
someone *using* the tool, so a refactor that moves a thousand lines earns
nothing and a renamed setting earns a line. The release body is generated from
that section rather than written a second time — the same move the sibling makes
in publishing its Docker Hub description out of the repository instead of a web
form, and for the same reason: one text, reviewed like anything else here.

## Considered options

**Keep the ffmpeg installation in CI anyway**, in case a test later wants it.
Rejected: ADR 0042 wrote down what is not tested precisely so that scaffolding
for absent tests would not accumulate, and this is the largest single piece of
such scaffolding available to copy.

**Publish to GHCR as well as Docker Hub.** Genuinely tempting — it is one more
entry in the tag list, needs no new secret, and Docker Hub's anonymous pull
limit is a real symptom this audience meets, which ADR 0034's documentation
already has to explain. Rejected for the first release because the cost is not
the CI line: it is two names to keep in step across the README, the Compose
example, every pinning instruction and every bug report that starts with which
image somebody pulled. Worth revisiting when there is a user asking rather than
a maintainer anticipating.

**Native runners for both architectures, and two single-platform pushes joined
into a manifest.** Rejected as more moving parts than the problem has: a
multi-platform `buildx` invocation already shares the stages that dominate the
build, and the manifest it writes is one artefact rather than three steps that
can disagree.

**Version the schema in the user-facing number**, so that a major bump warns
about a migration. Rejected: every release migrates, so the warning would be
constant and therefore unread, and the thing a person must actually know —
that there is no way back — belongs in the release notes for *every* version
rather than encoded in one digit.

**A `latest`-only publish, with no version tags.** Rejected by ADR 0034, which
made pinning a version the documented instruction.

## Consequences

- **The repository gains `.github/workflows/ci.yml` and `publish.yml`**, a
  `docker/smoke-test.sh` in the sibling's shape checking ADR 0034's claims in a
  running container, `CHANGELOG.md` and `CONTRIBUTING.md`. All of them are
  ticket 11's to write; this decision fixes what they contain.
- **`CONTRIBUTING.md` departs from the sibling's in one place.** Its bug-report
  section splits reports two ways; here there are four sides to separate — this
  tool's own logic, the indexer, SABnzbd, and prdb's metadata — because a report
  is useful in proportion to how precisely it says which of them misbehaved.
- **`Directory.Build.props` gains a `<Version>`**, which the sibling does not
  have, and the release workflow stamps it. This is the smallest of the three
  departures and the one with a named beneficiary: whoever reads a log file
  they were sent.
- **ADR 0034's operational document gains the forward-only sentence.** It
  already tells people to pin a version and to read the release notes before
  updating; what it does not yet say is that the pin protects them going
  forward and not backward.
- **CI needs no secret and reaches no network beyond the package feeds.**
  ADR 0042 put prdb, the indexers, SABnzbd and the CDN outside the test suite,
  so there is no credential in this build and no third-party service whose bad
  day turns into a red build.
- **`CONTEXT.md` is unchanged.** A workflow, a registry and a version scheme are
  artefacts, which ADR 0034 already settled is not something the language needs
  a term for.
- **Left to [ticket 11](../../.scratch/build-foundation/issues/11-the-walking-skeleton.md):**
  the workflow files themselves, how the version reaches the assembly from the
  tag, and what the smoke test asserts beyond ADR 0034's five claims. The
  skeleton is the first thing that has to pass all of it.
