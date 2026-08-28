# Credentials are stored in the clear, because there is nowhere to put a key

The prdb key, the SABnzbd key and every indexer key sit in the database as they
were typed. Nothing in the tool encrypts a column, and
[ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md)'s
export stays the only place a secret is encrypted at all — because it is the
only artefact designed to leave the machine.

[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)
said this in passing while arguing about something else: *the hash sits in the
same database as the prdb key and the indexer keys, which are not hashed at all,
so whoever can read it already holds the secrets worth having.* That sentence was
a premise there. Here it is tested, and it holds.

## There is nowhere to put a key

This is the whole decision, and everything else follows from it. Encryption at
rest protects against somebody who can read the database and cannot read the
key. Every candidate location for that key is inside the same reach.

**Not the login password.** ADR 0010 requires `FAB_RESET_PASSWORD` to clear the
password and every session and leave the installation intact, so a
password-derived key would destroy every credential on a reset — the recovery
path would become the destruction path. And it could not work anyway: the
routines run unattended, so the tool has to decrypt the prdb key at three in the
morning with nobody signed in.

**Not a key file beside the database.** It is in the mounted data volume, which
is the thing whose exposure is the threat. Anyone who copies the volume copies
both halves.

**Not the operating system.** A keyring or a TPM is not there;
[ADR 0034](0034-the-container-is-given-what-it-needs-before-it-starts-and-nothing-else.md)
fixes the deployment as a container with two mounts and one port.

**And not a seventh environment variable — though it passes the admission
rule.** That has to be said plainly rather than used as the refusal, because
ADR 0034's test is *whether the answer is needed before the application can
start*, and a decryption key genuinely is. It fails on three other grounds. It
puts the secret in the Compose file and in the process environment, which is
exactly the objection ADR 0010 made when it refused to configure the password
that way. It then lives in the same file people copy and paste when they ask for
help, beside the volume path it protects. And losing it costs every credential
with no way back, where the password — the other secret a person can lose — has
`FAB_RESET_PASSWORD`.

## The indexer key is already in the clear a hundred thousand times

The argument above would be enough. This one makes the alternative not merely
unfounded but ineffective, and it was found by reading the schema rather than
reasoned from first principles.

[ADR 0033](0033-the-schema-is-the-glossary-made-physical-and-the-export-boundary-runs-between-tables.md)
puts the **download URL on the `Release` row**, and
[ADR 0015](0015-the-indexer-cache-is-extended-never-re-walked.md) records that
this URL carries the indexer's API key. The indexer cache is bounded at 100 000
rows per indexer and is the most continuously written table in the schema.

So encrypting `Indexer.apiKey` would hide one copy of a secret that sits beside
it in plain text a hundred thousand times over. Encrypting the cache column too
is not the fix: it is the largest and hottest table in the database, the value is
read on every submission, and it would still be decryptable by anything that can
start the process — which is the first argument again, now paid for.

The URL is stored as the indexer returned it. It is cache, it is not exported,
and it refills itself. What follows is a rule rather than a change: **it is never
displayed and never logged**, which [ticket 09](../../.scratch/build-foundation/issues/09-how-a-failure-is-expressed-and-logged.md)
already lists among the things that must never reach a log line.

## The failure it would create is worse than the one it prevents

Without encryption, the worst case is a person re-typing four keys — a prdb key
they can read off prdb, a SABnzbd key from SABnzbd, and an indexer key per
indexer from each indexer. Tedious and complete.

With it, a lost or rotated key is every credential at once, permanently, on an
installation the person otherwise still has. ADR 0009 made exactly this trade
one level up when it refused to encrypt the whole backup file: *a forgotten
passphrase costs the entire backup, including the record of what was downloaded
and filed, which is the part that cannot be re-entered by hand. Encrypting only
the secrets bounds the loss to credentials the user can type again.* The same
reasoning, applied to the database, says do not encrypt it at all — there is no
part of this that cannot be typed again.

## What the actual controls are

Naming them matters, because "we decided not to encrypt" is only honest
alongside what is being relied on instead. None of these is new; each has been
decided elsewhere and is gathered here as the answer to *what protects the
credentials*.

- **The file permissions.** ADR 0034 fixes `PUID`, `PGID` and `UMASK` before the
  process drops to them, and takes ownership of the data directory only. The
  database, its WAL and its SHM are created under that umask. A world-readable
  data volume is the exposure; the umask is the control.
- **Nothing reaches a log.** No key, no indexer URL carrying one, no NZB URL.
  Ticket 09 owns how logging works and inherits this as a fixed requirement
  rather than a preference.
- **Nothing reaches the browser.** ADR 0020 already made keys write-only — the
  field renders empty with a marker, saving it empty means unchanged. Restated
  here because the place that rule gets broken is a component, not an endpoint.
- **A warning rather than a refusal** where the data directory is readable by
  everybody. ADR 0034's entrypoint already establishes the posture for exactly
  this shape of finding: *say plainly and carry on* when a share refuses the
  `chown`. Refusing to start would lock somebody out of their own NAS over a
  permission bit on a single-user tool, which is a worse outcome than the one it
  guards against.

## What the export carries, checked rather than assumed

ADR 0009 promises a document a person can read, which makes "does anything leak
into a plain field" a checkable property. All fourteen exported tables from ADR 0033,
checked:

| Exported table | Secret-bearing? |
|---|---|
| `Installation` | prdb key, SABnzbd key, password hash — ADR 0009's, encrypted |
| `Indexer` | API key, encrypted — **and the URL, see below** |
| `GateAdmission`, `AutomationRule`, `AutomationRuleIndexer` | no |
| `LibraryEntry`, `VideoFile` | paths, root-relative per ADR 0009 |
| `Download` | `stage_log` and `fail_message` verbatim — **see below** |
| `DownloadOriginRule` | rule id and copied name, no credential |
| `ArrivingFile`, `ArrivingFileCandidate` | paths re-rooted per ADR 0009, Probe facts and Candidates |
| `ReportedState`, `ConfirmedAssignment` | `userHash` |
| `OperationLogEntry` | paths and names |

`Session` is not exported at all, which ADR 0010 already decided, so no session
token travels.

`userHash` stays in the clear. It is an identifier prdb issues, not a
credential, and it is what makes an account-stamped row attributable to the
account that produced it — encrypting it would break the one comparison it
exists for.

Two entries in that table were nearly not clean.

**`Download.stage_log` is safe because of a decision made somewhere else.**
ADR 0016 chose **`addfile` only**: the tool fetches the NZB itself and posts the
bytes. So SABnzbd never holds the indexer URL, and the verbatim stage log — which
ADR 0016 stores without ever reading it for control flow, because it passes
through gettext — cannot quote a key. Under `addurl` every exported download row
would carry an indexer key in a plain field of a document designed to be read.

That makes `addfile` a **precondition of ADR 0009's readability promise**, which
nothing had noticed. Moving to `addurl` reopens this decision, and it is recorded
here so that the reopening is not silent.

**`Indexer.url` is the one thing this decision changes elsewhere.** Newznab users
routinely paste a URL with `?apikey=…` in it. Stored as pasted, that is a key in
a plain field of the export — the same leak, arriving through the form instead of
through SABnzbd. So ADR 0020's indexer form **splits a pasted URL into the base
and the key, or refuses it**, rather than storing what was typed. That is the
only obligation this decision creates for anyone else.

## Considered options

**Encrypt the credential columns under a key derived from the login password.**
Rejected under *there is nowhere to put a key*: `FAB_RESET_PASSWORD` would become
the destruction path, and unattended routines have to decrypt with nobody signed
in.

**A seventh environment variable holding the key.** Rejected — but not by
ADR 0034's admission rule, which it passes. It fails on ADR 0010's objection to
the password in the environment, on living in the file people copy, and on having
no reset path.

**A key file in the data volume.** Rejected: it is inside the thing whose
exposure is the threat, so it protects against nobody who is not already
stopped by the filesystem permissions.

**SQLCipher, or whole-database encryption.** Rejected twice over: it has the same
key problem, and it would replace the `SQLitePCLRaw.bundle_e_sqlite3` that
ADR 0004's EF Core stack and `prdb-ordeno`'s pin both rest on, for a protection
that ends the moment the process starts.

**ASP.NET Core Data Protection over the credential columns.** The framework's own
answer, and rejected for the same reason as the key file: its key ring would live
in the data volume. It would also have to be exported for a backup to restore,
which puts the key inside the document it is protecting.

**Encrypt `Indexer.apiKey` only, accepting the cache.** Rejected under *already
in the clear a hundred thousand times*: it is the appearance of a control rather
than one.

**Refuse to start on a world-readable data directory.** Rejected under *what the
actual controls are*: ADR 0034 already fixed the posture as saying plainly and
carrying on, and locking a person out of their own NAS over a permission bit is
the worse failure.

## Consequences

- **ADR 0010's aside becomes a decision.** Nothing in the database is encrypted,
  and the login hash is the only thing that is hashed. That ADR's refusal to
  reuse Argon2id for the login is now supported by a decision rather than by an
  observation.
- **ADR 0009 remains the only encryption in the tool**, and there are not two
  mechanisms over one set of columns — there is one, over the one artefact that
  travels.
- **ADR 0020 gains one requirement**: the indexer form splits a pasted URL into
  base and key, or refuses it, so that no key reaches a plain exported field.
- **ADR 0016's `addfile` is load-bearing for the backup's readability**, and a
  move to `addurl` reopens this decision.
- **ADR 0034's "what has to be said out loud" list gains a ninth item**: the
  credentials in the data volume are not encrypted, so whoever can read the
  volume holds them — the same fact its eighth item states as a convenience, read
  from the other side.
- **Ticket 09 inherits a fixed requirement** rather than a preference: no key, no
  indexer URL carrying one, no NZB URL in any log line.
- **`CONTEXT.md` is unchanged.** No new term; **Password** and **Passphrase**
  already keep the two secrets apart, and this decision adds no third.
- **This is not a permanent prohibition on encryption.** It is a finding about
  where a key could live today. If this tool ever gains a place to keep one
  outside the data volume, the arithmetic changes and this is reopened — but
  nothing is built now for a place that does not exist.
