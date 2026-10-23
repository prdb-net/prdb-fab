# Automation safety

Fresh setup supplies **Recent Wanted autopilot**, selected by default with an
opt-out. Existing installations keep their current choices after an update;
**Settings → Automation → Set up Recent Wanted autopilot** adopts it explicitly.

## Prepare the existing collection

The Library root and optional mounted collection directories are inventoried,
probed and identified through prdb. This work survives closed tabs and restarts.
It excludes SABnzbd's download directory, symbolic links and already recorded
Library files. Scanning does not move files. Wanted and Status show progress.

The paged collection preview shows each source, identity, Confidence, Quality
and proposed destination. Approve files to **move** into the sorted Library;
cross-filesystem moves copy, verify and then delete the source. Resolve ambiguous,
duplicate or unreadable files in the Review Queue or explicitly leave them in
place. Unidentified files cannot reliably prevent later duplicate Downloads.
Directory failures cannot be dismissed as an empty scan. A genuinely empty,
fully scanned collection needs no empty approval.

Approval is bound to the preview revision. Changed files or identities require
review again. Filing rechecks sources and destinations and records moves in the
Operation Log. All automatic submissions, including custom-rule submissions,
wait until preparation and approved moves complete. Manual Downloads remain
explicit actions. A failed move remains visible and retryable; Restore rechecks
mounted files and unfinished moves before readiness is trusted again.

## A bounded recent start

After Connections, collection preparation and the complete Wanted baseline are
ready, the supplied policy freezes up to **100 newest eligible missing Wanted
Videos**, ordered by Release Date descending and stable prdb ID. Dates must lie
between today minus 89 days and today, inclusive, in UTC. Unknown and future
dates do not qualify. Already held Videos, existing Downloads and known file
or candidate holds are excluded.

The selection is **never refilled** when a Download finishes, fails, leaves
Wanted or ages out. Subsequent genuinely new recent Wanted intent can join.
Replayed feeds, corrected old metadata, restarts and disable/re-enable cannot
turn the excluded initial backlog into new intent. The date is checked again
before submission. Selection history is scoped to the prdb account.

All enabled Indexers participate, including one added later. The default limits
remain **20 unfinished automatic Downloads** and **3 attempts per Video**.
Settings shows the initial selection separately from later admissions and
progress. Older and overflow Wanted Videos remain available for manual actions.

## Custom permissions

Custom rules are advanced, independent permissions over the current Wanted list.
A rule selects enabled Indexers and optional minimum/maximum Release sizes.
They may deliberately permit older or additional Videos. Turning off Recent
Wanted autopilot does **not** turn off enabled custom rules. With neither the
supplied policy nor a custom rule enabled, automatic Downloads are off. Rules
are unordered; a Download records every permitting rule's copied name as Origin.

The first release has no favourite-Site or favourite-Actor automation and no
Quality condition. A Release name is not evidence of the files' Quality, so
size is the only pre-download approximation a rule may use.

## Before a Download starts

Every automatic submission rechecks all of these current facts:

- the Video is still Wanted;
- the Library does not already hold the Video in any Quality;
- collection preparation is complete;
- the Video has no outstanding identified file or unresolved candidate hold;
- the identification answer is in the configured before-download named set;
- an enabled custom rule permits the Release, or the supplied policy admits the Video and Indexer;
- no Download for the Video is in flight and its Retry Budget is available;
- the installation has room under its unfinished automatic Download cap.

The cap defaults to 20. Reaching it keeps work in a durable queue; it does not
discard the decision. The Release view and Status expose the current reason a
Release did not start, including a gate, size, Indexer, Review Queue, held
Video, cap, Retry Budget, in-flight Download, or exhausted Release set.

Enabling or changing a custom rule schedules a catch-up over already cached, matched
Wanted Releases. This Release catch-up has no extra confirmation; the cap bounds it. The separate
collection preview always precedes moves of existing files. Changing the before-download gate also only queues
reconsideration; the settings request itself never submits to SABnzbd.

## Forward-only changes

Disabling a rule affects future decisions only. Deleting a rule requires
confirmation. Existing Downloads retain the rule name copied at submission,
even after its live rule link is gone. Resetting a Video's local Download
history restores its Retry Budget and makes cached Releases eligible for
reconsideration; it does not touch SABnzbd.

When the Wanted feed says a Video is no longer Wanted, prdb-fab marks its
unfinished automatic Download **Abandoned** and stops following it. It does not
pause, retry, or delete the SABnzbd job, does not start another Release, and
does not remove anything already filed in the Library. SABnzbd remains under
the person's control.
