# An automatic origin is every rule that permitted the download

An automatic Download records every Automation Rule that permitted it, not one
chosen rule. Rules are unordered permissions, so selecting a single winner
would add an ordering the domain expressly rejects and would hide true answers
to why the Download started.

The Download records Person directly for a manual Origin. An automatic Origin
uses one exported child row per permitting rule, with its own stable identity,
a nullable live reference to the rule and the rule's immutable name as it read
at submission. Deleting a rule clears only the live reference; the copied name
keeps the Download understandable forever and the remaining members still show
the complete permission that existed at the moment of submission.
