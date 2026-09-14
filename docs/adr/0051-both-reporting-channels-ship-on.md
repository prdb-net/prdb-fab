# Both Reporting channels ship on

Fulfilments and Confirmed Assignments are both enabled by default for a new
installation, while remaining separate switches that can be disabled
independently. The reports close prdb-fab's feedback loops with facts the tool
is specifically positioned to know, and the privacy surface already states
exactly what each bounded payload contains; preserving an explicit opt-out is
therefore preferable to leaving useful reports silently local.

This confirms ADR 0019's default for Fulfilments and supersedes only ADR 0022's
requirement that Confirmed Assignment reporting be opt-in and off by default.
Existing installations retain their saved choices: changing the shipped default
does not reinterpret a decision already stored in the installation.

*[ADR 0064](0064-a-preview-is-published-only-after-it-has-been-explained-and-an-uncertain-upload-is-never-sent-twice.md)
adds a third channel on the same argument, and one clause these two did not
need. Fulfilments and Confirmed Assignments are statements to prdb's own
machinery; a published preview is a picture strangers see, with no retraction.
So it ships on and it is gated: nothing is published until the explanation has
been in front of somebody.*
