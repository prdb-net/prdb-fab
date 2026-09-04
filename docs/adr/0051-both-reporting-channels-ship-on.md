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
