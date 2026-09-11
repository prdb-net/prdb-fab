# Repository Guidelines

A self-hosted web application that finds content on Usenet with prdb's help,
downloads it through SABnzbd, and builds a sorted library out of what arrives.
Open source, MIT. Read `VISION.md` before designing anything — it is what the
constraints here are in service of.

## Agent skills

### Domain docs

Single-context: `CONTEXT.md` and `docs/adr/` at the repo root. See
`docs/agents/domain.md`.

## Tickets

Tickets for this repository live in planaffe, project `FAB`. The checked-in
`.planaffe` file names the project, so no command has to. `pa` takes its
instance and its token from `PLANAFFE_URL` and `PLANAFFE_TOKEN` in the
environment, which is how an agent is given one; it is never interactive, writes
data to stdout and errors to stderr, and `--json` prints the object as the API
answered it. That token is an agent's, and one agent normally stands for one
installation rather than for one run: another run on the same machine may be
signing with the same name. Check with `pa me` that it is an agent's — a run
started from a person's shell inherits whatever token that shell carries, and
work done under a user's enters the history as that person's word; if `pa me`
names a user, stop and say so rather than work under it.

### Taking a ticket and giving it back

```sh
pa next --claim              # the highest-ranked workable ticket, claimed in one step
                             # exit 8 means nothing is workable, and says why
# … do the work …
pa issue close FAB-42 --done --result-file -   # the result as Markdown on stdin
```

`pa next --claim` hands out and claims atomically, so two agents never get the
same ticket. Claim what you are about to work on, and only that. If you stop
without finishing, `pa issue release FAB-42` puts it back into `todo` for
somebody else; an agent's claim also expires on its own once the agent goes
quiet.

Close with `--done` when the work the ticket asked for is delivered, with
`--canceled` when it will not be done — the result says why either way. Where the
project requires review, an agent's close lands in `review` and a human accepts
it; that is not an error.

### Asking instead of guessing

A question is a state on the ticket, a comment is not. **Whoever can go on
comments; whoever cannot go on asks.**

```sh
pa issue comment FAB-42 "…"            # an observation, a decision, an interim state
pa issue ask FAB-42 "…"                # what you need to know before you can go on
pa issue ask FAB-42 "…" --wait 600     # the same, and wait up to 600 s for the answer
```

An open question makes the ticket unworkable and puts it on the human's list, so
nothing has to be flagged by hand. Asking does **not** release the claim: wait
with `--wait` and keep your context, or release the ticket if you will not wait.
Say what you need — "something is wrong here" is not a question. Answering
questions is a human's job; answer one only when told to.

### `ready`

`ready` is a statement about the ticket, not a permission: it is concrete enough
that somebody can implement it without asking first. Whoever writes a ticket says
so per ticket — set it on the ones that are clear, leave it off on the ones that
still have to ripen:

```sh
pa issue create "Title" --description-file - --priority 2 --ready
pa issue edit FAB-42 --ready false     # it turned out to be too vague
```

Where the project has triage required switched on, `ready` decides what `next`
hands out: an unflagged ticket is never handed to anybody. Where it is switched
off, `ready` selects nothing on its own — only an explicit `pa next --ready`
filters by it — and the flag is read by people rather than by the list. It is a
human's word by convention rather than by a rule: set it when you are told to,
and on a ticket you write yourself, where nobody else is waiting to say the word.
Clear it on your own when a ticket you picked up turns out to be too thin, saying
in a question or a comment what is missing.

### Creating a ticket

A ticket you create leaves in one of two states, and there is no third. Either it
is implementable as written, and it carries `--ready`. Or it hangs on a decision
only a human can take, and a question follows at once, naming that decision:

```sh
pa issue create "Title" --description-file - --priority 2 --ready
pa issue ask FAB-43 "Which of the two …?"   # on one left without --ready
```

The question is what makes the open point visible. It holds the ticket back from
`next` and puts it at the top of the human's list, whatever the project's
switches say. A missing `ready` does neither where triage required is off: the
ticket is handed to the next agent exactly as a clear one is, and that agent
walks into the decision you left in the prose. Asking needs no claim of its own,
so a ticket you have just written can carry its question immediately.

### The commands you need

```sh
pa next --claim                          # what to work on
pa issue view FAB-42                    # the whole context: the project's instructions, the epic, the ticket, what its blockers decided
pa issue comment FAB-42 "…"             # a note that forces nobody to act
pa issue ask FAB-42 "…" [--wait 600]    # a question; the ticket waits for an answer
pa issue close FAB-42 --done --result-file -
pa issue release FAB-42                 # give it back unfinished
pa issue create "…" --description-file - # a ticket of your own: --ready or ask
```

Everything else — creating tickets in bulk, epics, labels, releases — is
`pa <object> --help`.

## Working with prdb

prdb's public API is the only interface this project can influence, and the
people behind it are approachable. So when a design here is being bent around
something the API does not offer, the change is worth proposing upstream rather
than only worked around — a limitation nobody reports is a limitation nobody
fixes.

That is not true of the other interfaces. SABnzbd and the Newznab API are what
they are, and this project adapts to them.
