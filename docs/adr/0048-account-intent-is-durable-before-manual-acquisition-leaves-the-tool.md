# Account intent is durable before manual acquisition leaves the tool

Catalogue preference actions write prdb through the governed SDK transport and
project the accepted answer locally. They never claim success optimistically:
the previous local state remains visible after a refusal, timeout or transport
failure, while deleting an already-absent preference converges on absence.

A person-originated Download is the one exception to waiting for prdb before a
local projection. Inside one SQLite transaction it records the Download
reservation, a desired Wanted write and the local Wanted state before `addfile`
can reach SABnzbd. A worker converges the Wanted write independently, and a
stale Wanted-feed tombstone cannot erase it while that write is pending. A
definitive prdb refusal blocks the write and removes the untrue local claim; it
does not cancel or disguise the person's Download request.

Manual SABnzbd submission has its own durable state. `Pending` may be retried
because no submission has begun; `Submitting` and `Unknown` are never submitted
blindly again and are recovered, where possible, by the existing exact-name
observation. Thus a crash can delay a Download or make its remote outcome
explicitly uncertain, but cannot silently duplicate one.
