# Automation safety

Automation is a set of independent permissions over Videos in the current
local copy of the prdb Wanted list. It is off when no rule is enabled. A rule
allows one or more enabled Indexers and may set minimum and maximum Release
sizes. Rules are unordered: if several rules permit one Release, the Download
records every one of them as its Origin.

The first release has no favourite-Site or favourite-Actor automation and no
Quality condition. A Release name is not evidence of the files' Quality, so
size is the only pre-download approximation a rule may use.

## Before a Download starts

Every automatic submission rechecks all of these current facts:

- the Video is still Wanted;
- the Library does not already hold the Video in any Quality;
- the Video has no open Review Queue entry;
- the identification answer is in the configured before-download named set;
- at least one enabled rule allows the Indexer and Release size;
- no Download for the Video is in flight and its Retry Budget is available;
- the installation has room under its unfinished automatic Download cap.

The cap defaults to 20. Reaching it keeps work in a durable queue; it does not
discard the decision. The Release view and Status expose the current reason a
Release did not start, including a gate, size, Indexer, Review Queue, held
Video, cap, Retry Budget, in-flight Download, or exhausted Release set.

Enabling or changing a rule schedules a catch-up over already cached, matched
Wanted Releases. There is deliberately no preview: the unfinished-Download cap
is the bound on that action. Changing the before-download gate also only queues
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
