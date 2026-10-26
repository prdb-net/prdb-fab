# Collection returns are approved durable File work

This explicitly amends ADR 0029's “nothing reads the Operation Log” and its
five-act limit. The log supplies the provenance linking a Collection File's
original path to its filed Video File. The return surface reads that link to
prepare a plan; the log remains immutable and carries a new **Returned** act.
Execution and recovery read a separate confirmed plan, never replay the log.

Imports from Scan Directories qualify, including files reorganised inside the
Library root. Downloads and files whose path did not change are excluded. A paginated
preview names current Library paths and original destinations, shows conflicts,
and supports selected or whole-preview approval. A hash of all candidate
records binds approval to the shown paths, independently of the collection
revision. An approval writes intent, not filesystem changes.

The File lane executes one return at a time, revalidating paths and refusing
symlinks or occupied destinations. A full SHA-256 reading is persisted before
moving; it is recovery evidence, not Identification. Cross-filesystem moves
use the existing full-byte copy/verify/delete mechanism. After interruption,
the destination must match the persisted full-file evidence before any source
is removed or Library record released. A changed middle byte is a refusal,
even if size and osHash still match. Errors remain visible per file and retry
fairly; a blocked return does not erase another file's progress.

Library deletion and return execution share a content mutation lock. A Library
Entry with pending returns cannot be deleted, and Filing for that Video waits.
After a return, its Collection File is explicitly left in place, the old
Arriving File is removed, and only the returned Video File leaves the Library.
An empty Library Entry and its generated companions may be removed. Other
qualities and unrelated files remain. Fulfilment is never retracted implicitly.

ADR 0065's acquisition barrier closes while returns run and remains closed
after they finish. The person must explicitly resume automatic acquisition,
having been told that returned Wanted Videos may then be downloaded again.

Backup format 7 carries return intent and recovery evidence, with root-relative
paths. Restore preserves the barrier, re-roots Library paths, leaves absolute
Scan Directory paths explicit, and requires confirmation of unfinished return
paths before resuming. Restart on the same installation needs no new approval.
