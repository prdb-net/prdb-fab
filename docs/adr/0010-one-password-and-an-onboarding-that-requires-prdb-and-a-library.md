# One password, and an onboarding that requires only prdb and a library

Sign-in is a single password with no username, set at first run and never
shipped as a default. Onboarding is a guided path with two entry points — set
up fresh, or restore a backup — and only two of its steps are mandatory: the
prdb API key and the library root. SABnzbd and the indexers are each a step the
user may skip, because a tool that cannot download is still a tool that holds a
library, and a step nobody needs must not stand between them and a working
installation.

`VISION.md` fixes the ends: one user, one password, no shipped credential, a
prdb key without which setup cannot complete, and a container that is given only
where its data lives, which port and which user it runs as. Everything else is
answered in the browser. What it leaves open is where the password lives, what
carries a session, what happens when it is lost, and which of the onboarding
steps genuinely block.

## Signing in

There is no username. One field, one secret; a name nothing checks is a prop.
The password is stored with ASP.NET Core's `PasswordHasher<T>`, which is
versioned and rehashes on sign-in. Argon2id is already a dependency for the
backup passphrase (ADR 0009) and is deliberately **not** reused here: the hash
sits in the same database as the prdb key and the indexer keys, which are not
hashed at all, so whoever can read it already holds the secrets worth having.
The file that is designed to travel — the backup — is the one that gets the
memory-hard derivation.

A successful sign-in creates a session row and returns its token in an HttpOnly,
`SameSite=Strict` cookie, `Secure` whenever the request arrived over https. The
row is what makes a session survive a restart and what makes it revocable;
thirty days, extended on use. Sign-in is rate-limited, because one password with
no username is the easiest thing in the world to try repeatedly. Sessions are
not exported: a restored installation ends on the sign-in screen.

An unauthenticated request gets **401**, never a redirect. The browser side is
one page that decides for itself what to show — sign-in, an onboarding step, or
the workspace — and one anonymous state endpoint answers what it needs to
decide: whether a password is set, whether this caller is signed in, and which
onboarding step is next.

The password can be changed in the settings, which requires the current one and
**ends every other session**. That is the only lever someone has who suspects a
session they did not open.

Losing it is recovered at the host, not over the network: the container is
started once with `FAB_RESET_PASSWORD=true`, which clears the password and every
session, logs loudly that the variable should now be removed, and drops the
installation back into "set a password". No second sign-in path, no trusted
proxy header — a second way in is a second way to configure wrongly.

## The window that closes

There are exactly two writes anyone may make without being signed in: setting
the initial password, and restoring a backup (ADR 0009, which needs the
unauthenticated path because the credential is inside the file). Both are gated
on the same single condition — **no password exists yet**. Setting one closes
the window for good; restore afterwards is reachable only signed in, and still
refuses an installation that holds anything.

One switch, not two overlapping notions of emptiness, and it is the only
unauthenticated write path in the application — which makes it the one to test.

## The path

1. Fresh, or restore a backup.
2. The password.
3. **The prdb API key.** Mandatory — `VISION.md` is explicit, and without it
   there is no identification, no wanted list, no artwork, no duplicate
   detection.
4. **SABnzbd.** Skippable.
5. **Indexers.** Skippable; one is enough when it is taken.
6. **The library root.** Mandatory: it is where filing puts things, and filing
   is in the first release (ADR 0005).

Each step commits when it completes, so the state is "which step is next" and a
closed tab costs nothing. The loop stands still until the mandatory steps are
done.

Every connection is checked against the real service before the step is allowed
to complete, and there is no way past a failure. A wrong key is a wrong key, and
"continue anyway" only moves the discovery to a point where it reads as
something else entirely. The verdict is not one message but four:

- **prdb** — `GET /user-identity`. `401` is a wrong key; **`403` is a valid key
  on an account whose tier has no API access**, which is a different sentence
  and a different fix; `429` is the quota, and `503` or a timeout is "not right
  now". The last two offer a retry rather than a correction.
- **SABnzbd** — a call that actually carries the key, since `version` and `auth`
  answer without one and would happily confirm a wrong key.
- **An indexer** — a real search. `t=caps` is not a key test: three of the four
  implementations surveyed answer it without an API key at all.

The `userHash` prdb returns is stored. It is stable per account, so a key
entered later that belongs to a **different** account can be recognised: the
wanted list is swapped out underneath and the local record of what was already
reported no longer refers to this user. That does not block — people do move
accounts — but it demands an explicit confirmation that names what stops lining
up. The record of what was reported is kept regardless; not re-reporting is the
harmless outcome.

SABnzbd reports paths as it sees them, which need not exist in this container,
so its step collects the path mapping and **verifies it**: resolve SABnzbd's own
completed-downloads path through the mapping and confirm the result exists and
is readable here. A wrong mapping is otherwise discovered at the first finished
download, where it presents as a download that hangs.

That mapping is also why there is no separate question for the download
directory: it is derived from the verified mapping, and when SABnzbd is skipped
there simply is none. The library step therefore asks for one path, confirms it
is writable by the container user, **refuses** a library root that lies inside
the download directory or contains it, and **warns** — without refusing — when
the two are on different filesystems, where a move degrades into a copy and a
delete. Some NAS layouts are genuinely like that.

## Skipping, and what a skipped step leaves behind

Skipping is a deliberate act with its consequence spelled out at the moment it
is taken — without an indexer nothing is searched and nothing is downloaded —
and afterwards the wizard is finished and does not return. What remains is a
**gap**: a named piece of the loop that is missing, carried on the sync status
page with a direct route to the form that fills it.

That is the same mechanism, not a second one, as a connection that stopped
verifying — including everything a restore finds stale. A restore applies the
file first and verifies afterwards, never blocking: a backup can be months old,
and an installation that refuses to restore because a key expired has trapped
the one record that cannot be typed again.

Onboarding ends on the **wanted list**, with the first sync visibly running and
a link to the sync status page. `VISION.md` measures onboarding by whether it
leads to a first download; the wanted list is the only source of intent
(ADR 0007), fills from prdb within seconds, and is where the user can act. An
empty library shows nothing, and the status page explains machinery rather than
intent.

## Considered options

**A username beside the password.** Rejected. Password managers handle a
single-field form, and there is exactly one user.

**Argon2id for the login hash too**, to keep one derivation in the image.
Rejected for the reason above: the hash is not what an attacker with the
database would be working on.

**A stateless signed cookie**, avoiding a table. Rejected: it cannot be revoked,
which makes "change the password and end other sessions" impossible to honour.

**The password from an environment variable.** Rejected as the way to configure
it — it puts the secret in the Compose file and in the process environment,
where anything that dumps the environment carries it along. It survives as the
reset path, which is a one-shot action at the host rather than standing
configuration.

**Trusting an upstream reverse proxy** (a forwarded-identity header) instead of
the password. Rejected for now rather than on principle; plenty of this audience
runs exactly that. It is a second authentication path, and its failure mode is a
header anyone can set. It changes nothing here if it is added later.

**A mandatory indexer and a mandatory SABnzbd**, which is how `VISION.md` read
before this decision. Rejected: they are needed for downloading, not for having
a working installation, and as the tool grows a library-only installation is a
legitimate one. Blocking on them buys nothing and costs the shortest path to a
running tool.

**Warn and continue on a failed check.** Rejected — see above.

**Asking for the download directory as its own question.** Rejected as asking
twice for one fact, and as a way to end up with two answers that disagree.

## Consequences

- `VISION.md` is amended in two places — the prerequisites and the sentence
  describing the first release — so that an indexer and SABnzbd are strongly
  recommended rather than conditions of onboarding. The prdb key stays
  non-negotiable.
- `FAB_RESET_PASSWORD` has to exist and be documented before the first release,
  because it will be used.
- An installation can complete onboarding and download nothing. That state is
  named on the sync status page rather than being prevented, which gives that
  page a fourth thing to carry beside the silent-failure counts of ADR 0006,
  0007 and 0009.
- The schema gains a password hash, a session table that is deliberately not
  exported, the prdb `userHash`, the next-onboarding-step marker, and the
  SABnzbd path mapping that ADR 0009 already exports.
- Nothing mechanical — a health check, a script — can reach the tool, since a
  browser session is the only credential. That gap is left open deliberately
  rather than filled with an API token nobody has asked for.
- Reaching this over plain http on an untrusted network sends the password in
  the clear, and the documentation says so plainly rather than implying the
  cookie flags help.
