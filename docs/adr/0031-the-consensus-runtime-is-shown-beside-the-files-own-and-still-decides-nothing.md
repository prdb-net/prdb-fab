# The consensus runtime is shown beside the file's own, and still decides nothing

prdb now publishes a runtime. Nothing checks a file against it.

[ADR 0021](0021-a-video-file-is-read-once-and-what-is-read-decides-nothing.md)
refused a minimum-runtime gate and gave one reason: "prdb publishes no runtime,
so there is nothing to compare against". `Prdb.Sdk` 0.11.0 makes that sentence
false — `durationMs`, `durationSpreadMs` and `durationFileCount` sit on
`VideoDetailDto` and `VideoSummaryDto` — so the refusal has to be re-argued from
scratch rather than cited. It survives, on three grounds that have nothing to do
with the fact that changed, and the consensus figure is put where ADR 0021 put
the runtime itself: in front of a person.

## The invention moved; it did not disappear

The strongest case for reopening is that `durationSpreadMs` is exactly what was
missing. It is the median absolute deviation of the files prdb holds — how far
real files legitimately disagree, because cuts, intros and re-encodes with
padding all differ honestly. A threshold expressed in spreads is calibrated
against the material, which is what ADR 0021 said an absolute threshold could
never be.

That is true, and it does not finish the job. Two numbers still have to be
invented, and both are the same kind of number ADR 0021 refused.

**The multiplier.** A MAD is a measure of dispersion, not a tolerance band.
Turning it into one means choosing *k* in `|runtime − durationMs| > k ·
durationSpreadMs`, and nothing in prdb's data says what *k* should be. Two
spreads and three spreads are both defensible, they classify different files,
and no observation available here distinguishes them.

**The quorum.** `durationFileCount` is the field that looks like it settles
this, and it is the field that proves the point. It says how many files stand
behind the median, so "prdb has no consensus" and "prdb has one from three
files" are genuinely different states — but deciding that three is too few and
twelve is enough is a threshold nobody has calibrated either. prdb tells us the
sample size; it does not tell us when its own figure is trustworthy. The
uncalibrated number moved from the comparison into the precondition for the
comparison, and a check that skipped the quorum entirely would refuse files on
the strength of one stranger's upload.

So the shape ADR 0021 objected to is intact. What changed is that the invention
now needs two numbers instead of one and hides behind a statistic.

## The asymmetry points the wrong way

[ADR 0006](0006-acting-alone-needs-a-named-video-and-an-allowed-confidence.md)
made the pre-download gate the looser of the two, on the ground that the error
it risks costs only bandwidth. The same reasoning, applied here, argues against
a gate rather than for one.

A **sample that files** is one bad file in the library. It is visible on the
entry page with its runtime beside it, it is deletable, and nothing depends on
it.

A **legitimate file that is refused** is worse than it first looks, because of
what [ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
attached to a queue entry afterwards: **an open entry brakes the video.** So a
false positive does not merely delay one file — it stops automation for that
video until a person acts. An installation whose owner is away for a fortnight
would accumulate braked videos on the strength of a *k* nobody chose. The whole
point of the tool is unattended running, and this is the one gate that would
convert a statistical near-miss into a stopped loop.

Ranked by cost, the wrong answer in one direction is a file to delete and in the
other a feature that stops working, and the multiplier that separates them is
guessed.

## A sixth reason would have no honest exit

ADR 0022 gives every reason at most one acting exit, and
[ADR 0026](0026-filing-is-three-routines-over-one-arriving-file.md) fixed the
five reasons as an ordered cost test. A runtime check would need the identified
video's `durationMs`, which arrives with `includeVideoDetails: true` at
identification, so it would sit between `Unidentified` and `Duplicate` — a
change to the set, as the ticket correctly says, not an addition beside it.

Then it needs an exit, and there is none to give it. The file identified
perfectly; the library may not hold it; nothing is wrong with it except a
number. The only exit that means anything is *file it anyway* — which is the
exact wording ADR 0022 refused for `EntryMissing`, on the ground that an exit
phrased as an override is a confirmation dialog pretending to be a decision. And
unlike `EntryMissing`, there would be no second fact for the entry to state that
narrows the question for the user; the entry would show a runtime and a median
and ask them to be the multiplier.

## What is done instead

**Three nullable columns on the catalogue video row** — `durationMs`,
`durationSpreadMs`, `durationFileCount` — written wherever a `VideoDetailDto`
is written. They cost no request at all: ADR 0026 already sets
`includeVideoDetails: true` on `POST /videos/identify`, and ADR 0013's repair
pass reads the same document.

**Displayed wherever a file's runtime is displayed and prdb has one.** On the
library entry page, beside each file's own runtime, as the median with its
sample size — `4 min` against `prdb: 31 min, median of 12`. Written with the
sample size rather than without it, because "median of 12" and "median of 2"
mean different things and hiding the difference is the same act as inventing a
quorum, done to the reader instead of to the code.

**A note on a review queue entry that exists for another reason**, in the same
form. This is the third of the three options the ticket named, and it is
admitted because it costs nothing: the entry already shows the runtime beside
the quality (ADR 0022), and the entry's video is known in every case where a
consensus can be looked up. It never becomes a reason and never changes an exit.

**Where prdb has no consensus, nothing is shown.** No placeholder, no "unknown",
and no comparison against `null` — which is `VideoDetailDto`'s honest answer for
a video too few people have submitted files for, and ADR 0027's *absent, never
approximate* rule applied to a screen instead of a sidecar.

## What is untouched

**The release ranking.**
[ADR 0008](0008-between-releases-of-one-video-size-stands-in-for-quality.md)'s
comparator chain runs before any download exists, so there is no runtime to
compare and no release is re-ranked. The ticket's last question therefore has no
mechanism to answer: since nothing refuses a file, no release fails a check, and
the ranking is never asked to avoid one.

**The sidecar.** ADR 0027 declined to write the runtime for a reason
independent of this one — a median over other people's files does not belong
beside one file whose own runtime the spread explicitly permits to differ — and
that reason is, if anything, this decision in miniature.

**The probe.** ADR 0021's four fields plus the `osHash` are unchanged. Nothing
new is read from the file, and the pass still decides nothing.

## The price, stated

A sample that identifies cleanly is filed, and **the tool does not go looking
for it**. The runtime beside it on the entry page is the whole of the defence,
and it only works when somebody looks.

ADR 0021 made samples visible in the review queue and Ticket 01 made them
ordinary rather than rare, so this is a real gap and not a theoretical one. It
is accepted rather than closed, and if it turns out to matter the answer is the
one `VISION.md` already defers — perceptual hashes, which describe what the
picture is rather than how long it lasts — and not a runtime gate assembled from
two guessed numbers.

## Considered options

**A gate that refuses to file a file outside `k · durationSpreadMs`.** Rejected
under all three headings above: two invented numbers, an asymmetry that stops
automation on a false positive, and no honest exit for the entry it would
create.

**A gate with a quorum — check only where `durationFileCount` is high enough.**
Rejected: it is the same gate with the invention relocated. It also has a
perverse coverage pattern, since the videos with the most submitted files are
the popular ones a user is least likely to be sampled on, and the obscure video
where a sample is most plausible is exactly the one prdb has no consensus for.

**A review queue entry rather than a refusal.** Rejected: under ADR 0022 an
entry *is* the refusal, since the file is not filed until someone answers, and
it additionally brakes the video. It is the strictest of the three options, not
the gentlest.

**File it, and raise a Brake on the status page.** Rejected:
[ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)
defines a Brake as the tool deliberately not acting, with a route to the setting
behind it. Here the tool acted and there is no setting. It would be the page
reporting an opinion, which is the failure that ADR spent its argument
preventing.

**A library filter for files far from the consensus.** Rejected as a fourth
filter on a view ADR 0012 fixed at site, actor and quality — and it would need
the multiplier after all, to decide what "far" selects.

**Store the three fields and display nothing**, on the ground that the check may
come later. Rejected by ADR 0021's own admission rule: a field with no reader is
a column that will be wrong for years before anybody notices. The display is
what makes them a named reader's.

**Write to prdb about the discrepancy.** Rejected:
[ADR 0019](0019-fulfilment-understates-the-quality-and-is-retracted-only-by-a-person.md)
and ADR 0022 fix two channels with two switches, and `VISION.md` names no third.
The tool disagreeing with prdb's median about somebody's file is not a fact prdb
asked for.

## Consequences

- **ADR 0021 is amended.** Its sentence "prdb publishes no runtime, so there is
  nothing to compare against" is no longer true, and its refusal of the
  minimum-runtime gate now stands on the three grounds above. The rule it
  actually established — *the pass is strictly descriptive* — is unchanged and
  is what this decision extends to the consensus figure.
- **`CONTEXT.md` changes twice.** **Runtime**'s definition currently reads "read
  from the file because prdb publishes no such figure", which is false; it
  becomes read from the file because it is a property of the file. And
  **Consensus Runtime** is added beside it — prdb's median across the files it
  holds, a property of the video, never compared automatically — with
  *Expected runtime* and *Reference runtime* under `_Avoid_`, since both name it
  as something a file can fail.
- **The data model gains three nullable columns** on the catalogue video row,
  written from `VideoDetailDto`. Not exported: they come back with the
  catalogue, which ADR 0013 refills.
- **No routine, lane, state or reason is added.** ADR 0022's five reasons,
  ADR 0026's four routines and ADR 0016's four states are untouched, which is
  the clearest statement of what this decision is: a display.
- **One wish is added** to the list: a statement from prdb about when its own
  consensus is reliable — a flag, or a documented sample size below which the
  figure should not be shown. It is what a check would have needed, and it is
  the one part of the problem prdb is better placed to answer than we are.
- The tool now holds a runtime for the video and a runtime for each of its
  files, and they will disagree. `CONTEXT.md` carrying both terms is what stops
  that from reading as a bug.
