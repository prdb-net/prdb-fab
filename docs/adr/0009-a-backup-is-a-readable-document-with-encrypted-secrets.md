# A backup is a readable document with encrypted secrets

The backup is a single JSON document, readable by a person, holding only what
the tool cannot fetch again. The credentials inside it are encrypted under a
passphrase the user chooses at export; everything else stays plain. Restore
runs on an empty installation only, before any login exists, and the paths it
carries are relative to roots the user re-answers.

`VISION.md` fixes the ends — one portable file, credentials in it, explicit
about being sensitive, and designed in from the start rather than retrofitted.
What it leaves open is the format, what happens to those credentials, and what
restore does when the world no longer matches the record.

## What is in it

The test is whether the tool can get it back by itself. Settings, indexers with
their URLs and keys and rank, the SABnzbd connection with its path mapping, the
prdb key, automation rules including the disabled ones, the review queue, the
login credential, and the local record of downloads and filings — including
which releases are consumed and what has already been reported to prdb.

The indexer cache, cached artwork and the video files stay out, as `VISION.md`
says. So does everything prdb holds: videos, sites, actors, the wanted list,
favourites. A library entry is exported as a video id plus the facts that are
ours — the filed path, the osHash, the quality — never as a copy of prdb's
metadata.

Two of those are not on `VISION.md`'s list and are load-bearing anyway.
**Consumed releases**: without them the ranking of ADR 0008 offers the releases
that already failed, in the same order, on the first sync after a restore.
**What has been reported to prdb**: `VISION.md` requires reporting to be
idempotent from the client side, and a restored installation that has forgotten
what it sent re-reports every fulfilment it holds.

## The file

JSON, UTF-8, indented, under a versioned envelope naming the format version,
the tool version and the export time. The format version is not the EF schema
version, or every migration would be a format break; restore migrates older
files forward and refuses a file written by a newer tool version, naming that
version, rather than reading the parts it recognises.

Readable matters because the failure mode is someone standing in front of a
restore that will not complete. A file they can open answers "what is actually
in here" without us.

The secret fields — indexer keys, the SABnzbd key, the prdb key, the login
credential — are encrypted individually under a key derived from a passphrase
the user types at export and again at restore. Argon2id derives the key,
AES-GCM encrypts the fields. The passphrase is its own secret, not the login
password.

## Restore

Restore expects an empty installation: any indexer, rule or library entry
present and it refuses, naming what it found. It is therefore reachable from
onboarding **without authentication**, because the login credential is inside
the file and on a fresh container nobody can be signed in yet. The two states
coincide — an installation empty enough to restore into is one that has nothing
to steal — and the passphrase gates the file itself.

Filed paths are stored relative to their root, and the roots — library,
download directory — are re-answered once at restore, prefilled from the
configuration. Verification is a background pass afterwards, not part of the
restore: osHash is cheap, and a restore that hashes a whole library before it
finishes is a restore people interrupt.

Until an entry is verified it counts as **held**. That is the rule that keeps a
mis-mounted library from looking like an empty one, which under ADR 0007 would
be a standing instruction to download the collection again. Nothing is deleted
and nothing is re-fetched on the strength of a missing file; what is missing or
mismatched is counted on the sync status page and left to the user.

Downloads still unfinished at export time are looked up at SABnzbd by their job
id and followed again where it still knows them. Where it does not, the
download failed and the release is consumed for that video — that is what
`CONTEXT.md` already means by consumed, not an exception to it.

## Considered options

**Copy the SQLite file.** What the sibling project does, and what `VISION.md`
rules out here: the database is dominated by the indexer cache, so the backup
would be orders of magnitude larger than the part worth keeping, and it can
only be restored into the exact schema version it came from. The WAL files have
to travel with it or the copy is torn.

**Plain text with a warning.** Honest and simple, and it is what comparable
tools do. Rejected because the file's whole purpose is to be carried somewhere
else — a cloud drive, a USB stick, an email to oneself — and every one of those
places is outside what the user controls.

**Encrypt the whole file.** Rejected for the failure it creates: a forgotten
passphrase costs the entire backup, including the record of what was downloaded
and filed, which is the part that cannot be re-entered by hand. Encrypting only
the secrets bounds the loss to credentials the user can type again.

**PBKDF2 instead of Argon2id**, to avoid a dependency. Rejected: this file is
designed to sit where an attacker can work on it offline and unhurried, which
is the one situation a memory-hard derivation is for.

**Merge into a running installation.** Rejected for the first release. Merging
needs identity and conflict rules for rules, entries and downloads, to serve a
case that arises after data loss or a move — both of which start from an empty
installation anyway.

**Automatic exports, before a migration.** Tempting given that ADR 0004 runs
migrations at startup and stops on failure. Rejected: nobody is present to type
a passphrase, so it would write an unencrypted file with every credential in it
into the data volume, unasked. The case it insures against is a bug in our
migration, not the user losing anything.

**Quiescing the sync for the export.** Unnecessary. Everything exported is
committed state, and the one thing a running sync writes heavily — the indexer
cache — is not in the file.

## Consequences

- The schema carries backup in mind from the start, as `VISION.md` demands:
  every exported table needs a stable identity that survives a round trip, and
  filed paths are stored so that a root can be substituted.
- Restore is part of onboarding, not of settings, and onboarding gains a second
  entry point. This constrains how sign-in is designed.
- The sync status page gains a third silent-failure count beside those of ADR
  0006 and 0007: library entries that verification could not confirm.
- Argon2id is a package dependency, and the first one taken for a
  non-functional reason.
- An entry pending verification counts as held, so duplicate detection and
  automation are deliberately conservative for as long as the pass is running.
- A restore onto a SABnzbd that does not know the old job ids consumes the
  releases that were in flight, and those videos fetch a different release
  under ADR 0008's retry budget.
- The passphrase cannot be recovered, and the export screen has to say so
  before it is typed rather than after.
