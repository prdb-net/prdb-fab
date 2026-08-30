# Contributing

Bug reports and pull requests are welcome.

Everything in this repository is in English — code, comments, documentation,
commit messages, branch names and PR descriptions. No exceptions.

## Where the project stands

This is an early repository. What exists is the foundation and nothing on top of
it: the projects, the database, the schedule, the logging, the API contract and
the image. `VISION.md` describes what the tool is meant to become — something
that finds, fetches and files videos on its own — and, just as usefully, what it
is not. It is the best place to check whether an idea belongs here before
spending an evening on it.

**`docs/adr/` is the other half of that.** Every decision that shapes this tool
is written down there with its argument, including the ones that were rejected
and why. A change that contradicts one is not necessarily wrong, but it needs to
say which decision it is reopening.

If you are thinking about contributing something substantial, open an issue
first. A design that does not fit yet is much cheaper to redirect before it is
written than after.

## Reporting a bug

This tool sits between four things it does not control — prdb, your indexers,
your downloader, and your filesystem — so a report is useful in proportion to
how precisely it separates them:

- **The tool's own logic** — it searched for the wrong thing, ranked releases
  oddly, filed a video into the wrong place, or did nothing when it should have
  done something. Include what you expected and what the status page said.
- **An indexer** — a search that returns nothing, an error the tool reports
  verbatim, a rejected key. Say which indexer software, since they differ more
  than their shared API suggests.
- **The downloader** — a download that was submitted and then went strange.
  Include what SABnzbd itself shows for the job.
- **prdb's metadata** — the tool did what it was told, but the answer it was
  given was wrong. That belongs upstream, though report it here if you are
  unsure and we will route it.

For any of them, the log is the most useful thing you can attach. Turn
`Logging__LogLevel__Prdb.Fab` up to `Debug`, reproduce it, and send the newest
file from `/data/logs/`. It never contains a key, a passphrase or a URL — that
is enforced by a test — so it is safe to attach as it is. Its first line names
the version that produced it; please leave that in.

## Working on the code

```
dotnet build           # also writes src/Prdb.Fab.Host/openapi.json
dotnet test            # xUnit v3, on the Microsoft Testing Platform
```

The frontend lives in `src/Prdb.Fab.Frontend`:

```
npm ci
npm run dev            # next to `dotnet run`, which it proxies /api to
npm run generate:api   # regenerate the committed types from the API
npm run lint
npm run build
```

To run the backend against a local prdb stand-in without making that stand-in
serve TLS, pass an SDK-approved loopback origin on the command line:

```bash
dotnet run --project src/Prdb.Fab.Host -- \
  --Prdb:BaseUrl=http://127.0.0.1:5080
```

The authenticated SDK accepts plain HTTP only for `localhost`, `127.0.0.1` and
`[::1]`; every other origin still requires HTTPS. This is a development input,
not a container setting or an alternative place to store the prdb API key.

**The API document and the generated types are committed**, and CI fails when
they do not match the code. If you changed an endpoint, run `npm run
generate:api` and commit the result with it.

The container is built and exercised the same way CI does it:

```
docker buildx build --load --tag prdb-fab:local .
docker/smoke-test.sh prdb-fab:local
```

## What the tests are for

There is no mocking library and no assertion library here, and that is
deliberate: a test may drive the composition root, but replacing a service with
a double to get past the wiring it exists to check defeats the point of the
wiring. Fakes are hand-written and say what they are pretending to be.

`docs/adr/0042` also writes down what is deliberately *not* tested, and why. If
you are about to add a test for something on that list, read the reason first.
