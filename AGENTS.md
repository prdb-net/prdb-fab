# Repository Guidelines

A self-hosted web application that finds content on Usenet with prdb's help,
downloads it through SABnzbd, and builds a sorted library out of what arrives.
Open source, MIT. Read `VISION.md` before designing anything — it is what the
constraints here are in service of.

## Agent skills

### Issue tracker

Local markdown under `.scratch/`, which is not committed. See
`docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` and `docs/adr/` at the repo root. See
`docs/agents/domain.md`.

## Working with prdb

prdb's public API is the only interface this project can influence, and the
people behind it are approachable. So when a design here is being bent around
something the API does not offer, the change is worth proposing upstream rather
than only worked around — a limitation nobody reports is a limitation nobody
fixes.

That is not true of the other interfaces. SABnzbd and the Newznab API are what
they are, and this project adapts to them.
