# Vision

`prdb-fab` finds the videos someone actually wants on Usenet, sends them to
SABnzbd, and files what comes back into a library their media server can present
— using prdb to know what a release is before it is downloaded, and what a file
is after.

Fetch and Build: it fetches releases, and it builds the library out of them. It
does not play them, and that boundary is deliberate — the tool ends where
Jellyfin begins.

It is a self-hosted web application. It runs as a container next to SABnzbd and
the media server, is set up once, and then keeps working on its own.

## The problem

Usenet indexers answer the question "what releases exist with this text in the
name?". They do not answer "is this the video I am looking for", "do I already
have it", or "is this the same release under a different name". For films and
series, Sonarr and Radarr close that gap because TVDB and TMDB exist. For this
material, nothing does.

So the work stays manual: search several indexers by hand, guess from release
names, push an NZB to SABnzbd, and later find out that the file is a duplicate
of something already on disk under another name. What lands in the download
directory is a pile of releases whose names say little and whose contents are
unknown.

prdb knows this material — the videos, the sites, the actors, and the file
hashes that tie all of it to actual files. What is missing is the piece that
puts prdb's knowledge in front of the indexer search instead of after it.

## Who it is for

Someone who already runs Usenet at home: an account with a provider, SABnzbd,
one or more indexer subscriptions, and a media server. They reach for Docker
Compose rather than an installer, and their storage is mounted, not discovered.

It is a single-user tool — one person, their key, their library — but it is
reachable over a network, so it has a login. That combination is the whole
security model: authentication because it is a web application, and no accounts,
roles or tenants because there is only ever one person behind it.

That is a settled decision rather than a stage on the way to something larger,
and the reason is prdb rather than convenience. An API key belongs to a prdb
account, and everything synced hangs off it: favourite sites, favourite actors,
the wanted list, the fulfilment reports going back. A second person with their
own prdb account would mean a second complete sync domain — its own cache, its
own wanted list, its own rate-limit budget, its own reporting — which doubles
the most demanding part of the tool to serve a case self-hosting rarely has.
One installation follows one prdb identity.

## What it does

The loop, running continuously:

1. **Sync** from prdb: favourite sites, favourite actors, the wanted list, new
   videos, artwork, and the hashes that tie all of it to real files.
2. **Sync** from the configured indexers: new releases, pulled through their
   APIs and kept locally, the way Sonarr keeps a cache rather than searching
   live every time.
3. **Match** releases against prdb, so a release is a known video — or is not —
   before anything is downloaded.
4. **Decide**: automatically for what the user's rules cover, or by presenting
   it for a decision.
5. **Send** the NZB to SABnzbd and follow the download to its end.
6. **File** the finished files into the sorted library, and report back to prdb
   if the user allows it.

Each step is visible in the UI, because a tool that downloads on its own has to
be able to explain what it did and why.

## Knowing what a release is, before downloading it

This is the part that makes the tool different from an indexer front end. A
search result is little more than a release name, and prdb often knows what
video that name belongs to. Asking before the download rather than after is
what turns a list of releases into a decision — and a download not started
costs nothing to undo.

Identification is the same ladder either way; only the evidence differs. Before
the download there is a name and whatever the indexer says about the release.
After it there is the file itself, which can be hashed, and a hash is the
strongest answer there is.

So the promise is graded, and stating it honestly matters more than stating it
strongly. Some releases resolve to a video before anything is fetched, which
saves the download entirely. Others resolve only to a candidate, or to nothing,
and those become a decision the user makes — silently guessing would fill the
library with the wrong files, which is the one outcome worse than not matching
at all. What the tool must never do is present a guess as a fact: a match
carries how strongly it matched, and the UI shows it.

Exactly how far pre-download matching reaches is a design question to settle
against prdb's API when this is built, not a promise to make in advance. The
architecture should assume it improves over time and not bake in one route.

After the download, identification is the ladder `prdb-ordeno` climbs, for the
same reasons: hash first, release name second, site third, user last. `POST
/videos/identify` does the climbing server-side, in batches, and `Prdb.Hashing`
computes the hash values it is given. A file that arrives unidentified is not a
failure to hide — it is a queue entry, and resolving it by hand has to be
quick.

## Indexers

Several indexers can be configured, each with its own URL, API key and
categories, and each can be enabled or disabled without being deleted. The
Newznab-style API that indexers share is what the tool speaks, so adding one is
entering a URL and a key rather than waiting for support to be written.

Their content is pulled continuously and kept locally, rather than searched live
on every user action. That is what makes the UI fast, what makes matching
against prdb a background job rather than something the user waits for, and what
makes rate limits survivable: an indexer's API budget is spent by a scheduler
that knows what it already has, not by a person clicking search.

The local index is a cache of what the indexers offer. It is not a copy of
prdb's corpus, it expires, and it can be thrown away and rebuilt.

## Automation

prdb already carries the user's preferences: favourite sites, favourite actors,
and the wanted list. Those are the input to automatic downloading — when a
release matching a wanted video appears on any configured indexer, the tool can
fetch it without being asked, which is the entire reason to run an unattended
service rather than search by hand.

Automation is off until the user turns it on, and it is scoped: by site, by
actor, by the wanted list, with quality and size limits. Every automatic
decision is written down with the rule that caused it, so "why is this on my
disk" always has an answer.

Fulfilment closes the loop back: a wanted video that arrived is reported as
fulfilled, so the list reflects what is actually on disk rather than growing
forever.

## Handing off to SABnzbd

SABnzbd does the downloading. The tool holds its URL and API key, queues NZBs
into a category of its own, and follows each job to completion, failure or
deletion — so the UI can show the state of a download without the user opening
another tab, and so a failed job becomes a retry against another release rather
than silence.

SABnzbd being unreachable is a visible, recoverable condition, not a crash: the
queue waits and the sync status page says so.

## Downloads and the sorted library

Two directories, and they must not be the same one:

- The **download directory** is SABnzbd's, and it is allowed to be a mess.
  Arbitrary names, unpacked releases, leftovers — nothing in there is the
  tool's idea of a library.
- The **sorted library** is the target. It is written only by this tool, and it
  is what the media server points at.

Requiring them to differ is not pedantry. A tool that sorts within the directory
it also watches ends up re-processing its own output, and the first time that
goes wrong it goes wrong across the whole library.

Beyond those two, **additional mounted directories can be scanned**: the
existing collection someone accumulated before installing this, sitting on a NAS
share. Everything in them that identifies against prdb can be taken into the
sorted library. That first bulk run over thousands of files is a different
problem from a handful of new downloads a day, and both have to work — which is
why scanning is not in the first release, while the shape of the tool allows for
it from the start.

Files are moved, not copied — the download directory is meant to empty itself.
Where source and target sit on different filesystems, a move is a copy, a
verification and a delete, and the documentation has to say so, because that is
the difference between instant and overnight.

Only video files move. What an unpacker leaves behind — `.nfo`, `.par2`, cover
images — is not carried into a directory the media server reads. It is deleted
from the download directory instead, once nothing in there is still waiting on a
decision, under a setting that ships switched on and can be turned off. That
list is fixed rather than something the user writes patterns into: a delete
pattern in a text field is how someone loses a download directory. Anyone who
wants more has SABnzbd's own cleanup list, at the right layer.

Moving will apply to scan directories too, and that has to be said out loud
rather than discovered when they arrive: a first run over a collection someone
spent years arranging takes the identified files out of it and leaves the rest
behind. One library,
in one place, is the point — but a bulk operation over files the user already
considers sorted is the most dangerous thing this tool does. It is why the
first run over a scan directory shows what it would do before it does anything,
and why the operation log has to be able to put every file back.

The target structure follows what a media server expects, starting with
Jellyfin: a sorted library here should be a Jellyfin library, directly. The
layout work is not open research — `prdb-ordeno` validated one against a real
Jellyfin instance, and the rules it found (exact `yyyy-MM-dd` dates, `<actor>`
with a `<name>` child, quality suffixes) apply here unchanged. There is no
reason to discover them twice — and no reason to file into some simpler shape
first, since adopting the layout afterwards would be a rename across the entire
library.

## Duplicates

The library never holds one video twice at the same quality. Only the arriving
file can answer that, because the quality is read from the file — so the two
things that happen here are not one check at two strengths.

**Before the download** the tool says what it already holds: this video, and at
what quality. That is a reason to ask rather than to fetch silently — a video
already owned is exactly what a better encode of it looks like — and it stays a
sentence rather than a refusal, because nothing at that point can tell the two
apart.

**After the download** the file is here: its `osHash` can be computed locally
and its quality measured. That is the check that decides whether a file is filed
into the library or set aside.

Where two files are the same video at different quality, both are kept — someone
holding the 1080p and the 2160p version usually meant to. Where they are the
same video at the same quality, the library does not hold it twice, and the
redundant file is reported rather than deleted. Deleting is a decision the user
makes, never a default.

## Browsing prdb

The tool is also how the user looks at prdb, because deciding what to download
and seeing what exists are the same activity:

- **What's new** — the newest videos prdb knows about, as the landing page for
  "is there anything for me today".
- **Sites**, **actors** and **wanted videos** — browsable, with prdb's artwork,
  and with the obvious action attached: find this on the indexers.
- **The library** — what has actually been downloaded, with prdb's thumbnails,
  filtered by site and actor, searchable by title, filterable by quality.

Artwork comes from prdb and is cached locally, because a grid of thumbnails
that fetches on every scroll is a grid nobody scrolls.

## Dashboard and sync status

Two views, and they answer different questions.

The **dashboard** answers "what is happening": downloads over time, what
arrived recently, how the wanted list is doing, how much of the library is
identified, what is waiting in the review queue.

The **sync status** page answers "is anything broken": when each indexer was
last polled and what it returned, the state of the prdb sync and the remaining
rate-limit budget, whether SABnzbd is reachable, what failed and when. An
unattended tool that cannot say whether it is still working is one the user has
to check by hand, which defeats it.

## Reporting back to prdb

The user may let the tool report back to prdb. Two things it learns are worth
sending: which wanted videos have been fulfilled, and hash-to-video assignments
confirmed by hand in the review queue — the second being something prdb cannot
obtain otherwise, since it is a human-checked answer for exactly the file that
automatic recognition failed on. Whether there is more worth reporting is a
question for the point at which it is built.

It is genuinely useful, and it is also a report about someone's disk going to a
remote service. So it is a setting the user controls and can switch off, with
what is sent stated plainly rather than buried. Fulfilment of wanted videos and
confirmed hash assignments are separate channels with separate switches: opting
into one is not opting into the other.

Reporting must be idempotent from the client's side. A crash between prdb
accepting something and the tool recording that it was sent must not turn into a
retry loop every five minutes — the sync remembers what it has sent, and
recovers rather than submitting again.

## Setup, and getting the user to the first download

Onboarding is a guided path, and it is the feature most likely to decide whether
someone keeps the tool: prdb API key, then SABnzbd, then the indexers, then the
library.

**The prdb API key is not optional.** Without it there is no identification, no
wanted list, no artwork and no duplicate detection — the tool is an indexer
search box with extra steps. Setup cannot be completed without a working key,
and that has to be said before installation rather than discovered at first run.

**SABnzbd and the indexers are not conditions of setup.** They are what
downloading needs, not what a working installation needs, and a step nobody has
an answer for yet must not stand between someone and a tool that runs. Either
can be skipped and added later; what is skipped is named where the user will see
it, so an installation that downloads nothing says why rather than sitting
quiet.

The container is given only what it needs to start — where its data lives, which
port, which user it runs as. Everything else is answered in the browser and kept
by the tool, so changing an indexer key is a form, not a YAML edit and a
restart.

## Backup, from the first release

Everything that would be painful to lose is small: settings, indexer URLs and
keys, the SABnzbd connection, automation rules, the prdb key, the local record
of what was downloaded and where it was filed. That is a file, not an archive.

Everything large is disposable by design: the indexer cache, cached artwork,
downloaded video files. None of it belongs in a backup, because all of it can be
fetched again.

So backup is a button that produces one portable file, and restore takes that
file on a fresh container and gives back a working installation. Credentials are
in there, which is exactly why the export has to be explicit about being
sensitive. This is designed in from the start — a backup format retrofitted onto
a schema that never anticipated it is how people lose their configuration.

## Principles

**Do not download what is already there.** The hashes are the reason this tool
exists rather than a search page. Bandwidth, Usenet retention and disk are all
finite, and spending them twice on the same video is the failure the user is
trying to avoid.

**Files are irreplaceable.** Video files, and anything that carries a video, are
never deleted without being asked for — duplicates included, which are reported
rather than removed. That is the whole of the principle, and it is deliberately
about content: clearing a `.par2` file out of a download directory is not what
it protects against. Cross-filesystem moves are copy-verify-delete, nothing is
filed on a failed lookup, and every move and every deletion is logged with what
it was and why — which is also what makes an undo possible. A button is easier
to press than a command is to type, so a web UI raises the stakes rather than
lowering them.

**Set up once, then leave it alone.** The value is in unattended running. A tool
that needs weekly babysitting has failed at its actual job — which is why the
sync status page and, later, notifications are not decoration.

**Nothing leaves without permission.** Reporting to prdb is a switch, per
channel, and what each one sends is stated in the UI. The default posture of a
self-hosted tool is that data stays home.

**prdb is the only metadata source, and its public API is the only door.**
Everything the tool knows about videos, sites, actors and hashes comes from
the documented public API — no scraping, no private endpoints, no database
access, no metadata corpus of its own. A wrong title is a prdb problem with a
prdb fix.

**That API is reached through `prdb-sdk`, never a hand-rolled HTTP client.** The
C# package `Prdb.Sdk` covers the public API completely, generated from the same
OpenAPI document the API publishes, and it is a NuGet dependency like any other.
Hash values come from its companion `Prdb.Hashing`, because `osHash` and `pHash`
have to be reproducible bit for bit — a hash computed a slightly different way
does not match approximately, it matches nothing. Anything missing is fixed in
the SDK and released, not worked around here.

**Indexer credentials are the crown jewels.** API keys, the SABnzbd key and the
prdb key are stored, exported and displayed with that in mind.

**Docker Compose is the supported way to run it.** Not one option among several
— the way. Storage arrives as mounts, and the documentation teaches the parts
people get wrong: `PUID`/`PGID` and ownership, NAS shares, and why the download
directory and the library should share a filesystem.

**Reachable, but not open.** One user, one password, set during first run. No
shipped default credential, ever. Whoever exposes this to the internet has made
their own choice; the default must not be an open door.

**English, one language.** The interface is English and stays English.
Translation is not planned, and pretending otherwise would put a localisation
layer under every string for no one's benefit.

## What it is not

- Not a Usenet client. SABnzbd downloads; this decides what is worth
  downloading and what to do with it afterwards.
- Not an indexer, and not a replacement for having one. It reads the indexers
  the user already pays for.
- Not a media server or a player, and not on the way to becoming one. It
  produces a library; Jellyfin serves it. Playback is a solved problem with
  several good answers, and each of them is a larger project than this tool —
  transcoding, client applications, per-viewer state — which is also the only
  place multiple users would ever have come from.
- Not a metadata editor. Corrections belong upstream in prdb.
- Not multi-user, not hosted, not a service someone else runs.
- Not a general-purpose *arr. It knows one kind of content, because knowing it
  properly is the entire value.

## Prerequisites for the user

- A prdb account with an API key. Non-negotiable, and stated before install.
- Docker, and storage that can be mounted into a container.

For downloading — which is the point, but not a condition of getting set up:

- A Usenet provider and a working SABnzbd.
- At least one Usenet indexer with API access.

## The first release, and what comes after

The first release has to be the whole loop and nothing else: onboarding that
ends with a working key, and with SABnzbd and an indexer wherever the user has
them; continuous indexer sync; matching
against prdb, with duplicate detection against what is already in the library;
sending to SABnzbd and following the job; filing what arrives into the library
in the layout below; a library view with artwork, search and filters; the sync
status page; and backup and restore. Automation may start narrow — the wanted
list — as long as every decision it makes is visible and reversible.

Deliberately after that, not before:

- **Filing everything downloaded, however weakly it identified.** The first
  release files what identifies well enough to act on and queues the rest for
  the user; lowering that bar until nothing is left over is the later step.
  Filing itself, and the layout it files into, are in the first release.
- **Scan directories.** Absorbing a collection that existed before this tool
  needs a dry run, an undo across thousands of files, and a review queue that
  survives that volume — a second product beside the download loop, and the
  most dangerous thing the tool will ever do.
- **Notifications**, so an unattended tool can say something went wrong without
  the user thinking to look.
- **Perceptual hashes and thumbnails for unknown files**, optionally submitted
  to prdb — which is how recognition gets better for everyone. A perceptual
  hash decodes 25 frames, so it belongs in a background queue that never holds
  up filing. `ffmpeg` and `ffprobe` are in the image from the first release
  regardless, because quality is read from the file rather than guessed from a
  release name; what arrives later is this use of them, not the tools. The
  plain `osHash` is not in this bucket either: it reads 64 KiB from each end of
  a file, costs nothing even on a large library, and is part of the first
  release.
- **An MCP server**, so an agent can drive the tool.
- **Optional 2FA.** Optional is the operative word: a mandatory second factor
  on a self-hosted box is how people lock themselves out of their own NAS.
- **Outbound SOCKS proxies**, configurable per destination — indexers, SABnzbd,
  prdb — with more than one supported, for users whose network requires it.

## Open questions

- How far automation should go on its own before the first release, and what the
  smallest useful rule set looks like.
- What the review queue holds and how an entry leaves it, given that an
  undecided file also holds up the cleanup of the directory it sits in.
- How much indexer history to keep locally, and when to drop it.
- What the backup file contains exactly, and how restore behaves when the
  library it describes is no longer there.
