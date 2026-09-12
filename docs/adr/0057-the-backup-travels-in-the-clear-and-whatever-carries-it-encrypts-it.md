# The backup travels in the clear, and whatever carries it does the encrypting

The exported document holds the prdb key, the SABnzbd key, every indexer key and
the login credential as they are stored. Nothing in the tool encrypts them, there
is no passphrase, and restore takes any file that parses. What protects the file
on its way somewhere is whatever the person moves it with — restic, borg, age, an
encrypted share — which is the layer that was going to encrypt it anyway.

This reverses one paragraph of
[ADR 0009](0009-a-backup-is-a-readable-document-with-encrypted-secrets.md) and
only that one. The single readable JSON document, the versioned envelope, the
export boundary, the root-relative paths, the empty-installation restore and
everything else that decision settled stand unchanged.

## What ADR 0009 was protecting against

Stated correctly there, and worth repeating before it is argued with: *the file's
whole purpose is to be carried somewhere else — a cloud drive, a USB stick, an
email to oneself — and every one of those places is outside what the user
controls.* A memory-hard derivation was chosen over PBKDF2 for the matching
reason: *this file is designed to sit where an attacker can work on it offline
and unhurried.*

Neither sentence is wrong. What changed is not the threat but the answer to who
is in a position to meet it.

## The person moving a backup already runs something that encrypts

A backup that is never moved is not a backup, and nobody moves one by hand twice.
Whoever has a destination has a tool in front of it — restic, borg, Kopia,
rclone with a crypt remote, age in a shell script, a LUKS volume, an encrypted
share. Every one of them encrypts everything it carries, under a key the person
already manages, with a rotation story and a recovery story they have already
decided on.

Against that, this tool encrypting four fields is a second control over a subset
of one file, with its own secret to remember, sitting underneath a first control
that covers all of it. It is not additional protection in any useful sense: the
carrier's key is what an attacker has to get past either way, and where the
carrier is absent — a file dragged onto an unencrypted drive — the passphrase is
being typed by somebody who has just shown they are not thinking about this,
which is exactly the case where a typed passphrase is weakest.

## A passphrase is a secret that can be lost, and this project has been here twice

ADR 0009 refused to encrypt the whole file, and said why: *a forgotten passphrase
costs the entire backup, including the record of what was downloaded and filed,
which is the part that cannot be re-entered by hand.*

[ADR 0037](0037-credentials-are-stored-in-the-clear-because-there-is-nowhere-to-put-a-key.md)
then took the same argument one level down, to the database, and reached the end
of it: *the same reasoning, applied to the database, says do not encrypt it at
all — there is no part of this that cannot be typed again.*

Encrypting only the secret fields bounded that loss rather than removing it. And
the part it bounds the loss to — four credentials a person can type again — is
precisely the part ADR 0037 decided was cheap enough to leave unprotected in the
database. The project has already priced these four secrets, twice, and both
times the answer was that losing access to them costs an evening and losing the
record costs everything.

What the bound does not cover is *when* the loss is discovered. A forgotten
passphrase is found out at a restore, which is to say after the data loss that
made the restore necessary, by somebody who is already having a bad day. That is
the worst moment this tool has to offer, and app-level encryption is the only
thing that puts anything there.

## The bar is where ADR 0037 put it, and the document does not lower it

ADR 0037 keeps all four of these secrets in the clear in the database, and found
that the indexer key is additionally present in plain text in up to a hundred
thousand cache rows, because the download URL on a `Release` carries it. Whoever
can read the data volume holds everything the document would have encrypted, and
holds it without a derivation to run.

So the encryption never protected the secrets. It protected one copy of them, for
the part of the journey between leaving the container and reaching the encrypted
destination the person was going to use anyway. That is a real interval, and it is
the interval the person is standing in front of when they press the button — which
is the interval a sentence can address better than a cipher can.

## What is owed instead, and it is not a comfort

Dropping the encryption is only honest alongside what replaces it, and what
replaces it is being explicit at the two moments it matters.

- **Before the file is produced.** The export names what is in it — the prdb key,
  the SABnzbd key, every indexer key, the login credential — and says that
  anybody who holds the file holds them. It is an acknowledgement rather than a
  passphrase: one thing to understand instead of one thing to remember.
- **In the documentation, beside how to run the tool.** The backup is as
  sensitive as the data volume and should be given to something that encrypts it.
  That belongs on ADR 0034's list of what has to be said out loud, where item 3
  already stood — it changes from a warning about a passphrase that cannot be
  recovered into a statement about a file that is readable.

Two things that were already true carry more weight now and are restated rather
than newly decided. ADR 0043 keeps keys and the password out of every log line.
ADR 0020's indexer form splits a pasted URL into base and key, or refuses it, so
that no key reaches a field nobody meant to put one in.

## Considered options

**Argon2id and AES-GCM over the four secret fields, as ADR 0009 wrote it.** The
decision this reverses, and it is not a bad one — the threat it names is real and
its reasoning about offline attack is correct. Rejected because the control only
holds where the passphrase is strong, and the two populations do not line up: a
person who picks and keeps a strong passphrase is the person who already runs an
encrypting backup tool, where one strong secret is managed once for everything
instead of typed per export; a person who picks a weak one gains a secret to lose
and, against an unhurried attacker holding the file, a delay rather than a wall.
It also costs the first dependency taken for a non-functional reason, a set of
derivation parameters in the envelope, and a migration story for the day those
parameters have to change.

**A passphrase, but optional.** Rejected, and it is the worst of the three. It
produces two shapes of document, two restore paths and a question at the moment
of least patience; whatever it defaults to is what almost every file will
actually be, so the default decides this anyway while the option pays for both
answers.

**Encrypt the whole file.** ADR 0009 rejected it for the loss a forgotten
passphrase would cause, and nothing here disturbs that — it is rejected twice over
now.

**Write the file already encrypted to an `age` recipient the user configures.**
Tempting, because a public key has no passphrase to forget and the private half is
the person's problem rather than ours. Rejected: it is the carrier's job done
badly — one recipient, one algorithm, no rotation, no repository, no verification
— for a dependency and a key format to maintain, and the people who have an `age`
key already have a tool that uses it.

**Refuse to export unless the browser is on https.** Considered because a readable
file now crosses the connection. Rejected on ADR 0010's own posture: it says
plainly that reaching the tool over plain http on an untrusted network sends the
password in the clear rather than implying cookie flags help, and the same
sentence covers this. Refusing would lock somebody out of their own installation
over a deployment choice they made deliberately.

## Consequences

- **ADR 0009's paragraph on the secret fields is reversed**, and that ADR carries
  a note saying so. Its title keeps the word *encrypted*, which is now wrong, and
  the file is not renamed: seven other decisions link to it by name, and a link
  that resolves is worth more than a title that reads correctly.
- **The format does not move.** `BackupFormat.Version` stays 1. The document
  already written carries the four fields as they are stored, so this decision
  removes work rather than changing a shape — there is no version 2 and nothing
  to migrate.
- **The tool takes no cryptographic dependency at all.** No Argon2 package, no
  derivation parameters in the envelope, and `System.Security.Cryptography` is
  not reached for either. ADR 0009's consequence *"Argon2id is a package
  dependency, and the first one taken for a non-functional reason"* does not
  happen.
- **Export asks for no passphrase**, and restore asks for none. A document that
  parses, is not from a newer tool, and meets an empty installation restores.
  That removes the whole class of failure ADR 0009 listed first — a wrong
  passphrase, a partial restore, a derived key that has to be cleared from
  memory.
- **ADR 0037's sentence that ADR 0009's export "stays the only place a secret is
  encrypted at all" becomes: nothing in this tool encrypts anything.** Its export
  table's four *encrypted* entries read *in the clear, as the database holds
  them*. Everything else in that table stands, including the two entries it found
  were nearly not clean — `Download.stage_log`, safe only because ADR 0016 chose
  `addfile`, and `Indexer.url`, safe only because ADR 0020 splits a pasted key
  out. **Both matter more now rather than less**: they are the difference between
  a document whose credentials are the four it admits to and one with a fifth in
  a field nobody meant to export.
- **`CONTEXT.md` loses `Passphrase`**, and **Password** loses the line telling it
  apart from one, because there is no longer a second secret to tell it from.
  This is the first term the glossary has had to retire.
- **ADR 0034's list item 3 changes** from *a backup passphrase cannot be
  recovered* to *the backup is readable and holds every credential*, and the
  documentation says what to do about it.
- **This is not a claim that the file is harmless.** It is the opposite: the file
  is exactly as sensitive as ADR 0009 said, and the tool now says so instead of
  implying that four encrypted fields had settled it.
- **Reopening this costs one file.** Should the tool ever grow somewhere to put a
  key — the same condition ADR 0037 leaves its own decision open on — encryption
  can be added under a raised format version without anything else moving.
