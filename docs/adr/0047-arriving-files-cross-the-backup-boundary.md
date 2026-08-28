# Arriving files cross the backup boundary

`ArrivingFile` and `ArrivingFileCandidate` are exported whole. An open Arriving
File is the Review Queue entry ADR 0009 explicitly promises to preserve, and
neither its Probe facts, reason, Candidates nor the person's pending decision
can be fetched again. Exporting only rows with a reason would cut through a
table, contradicting ADR 0033 and losing files between Collecting and the Review
Queue.

Restore re-roots the Arriving File's source path under the Download Directory
and its intended path under the Library, restores every state, and lets ADR
0026's ordinary work sets resume conservatively. Its existing disappearance and
interrupted-Filing rules apply unchanged; Restore neither invents a successful
Filing nor deletes content merely because a source or intended path is missing.
