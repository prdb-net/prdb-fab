# Privacy and outbound data

prdb-fab is self-hosted, but it is not an offline application. It reads the
services configured during setup: prdb for catalogue and Identification data,
Indexers for Releases, SABnzbd for categories and Download state, and image URLs
published by prdb for artwork. Opening Status reads only the local database and
does not contact any of them.

## Reporting to prdb

Both Reporting channels are **on by default**. Settings → Reporting has two
independent switches; disabling one does not disable the other. Turning a
switch off stops new outbound reports on that channel. Pending differences
remain local so the page can explain what is not being sent. Turning a channel
off does not retract or delete a report prdb already accepted, and it does not
erase the local record of what was sent.

### Fulfilments

This channel reports only a Video that is both Wanted in the current local copy
of the prdb account and held as a Library Entry. Each report contains:

- the prdb Video id;
- `isFulfilled: true`;
- the time the Library Entry was filed;
- the highest held quality that prdb's scale can state without exaggerating:
  `720p`, `1080p`, `2160p`, or no quality below `720p`;
- the application category `Other`, with no external application id.

The desired report is computed from database records. prdb-fab does not inspect
or probe a filed path to make it. What prdb last accepted is retained per prdb
account so restarts and restores do not repeatedly submit the same state.

### Confirmed Assignments

This channel reports only a file-to-Video assignment a person explicitly
confirmed in the Review Queue. It sends the recorded values of that exact
decision:

- prdb Video id and `osHash`;
- file size;
- arrival filename and Release name;
- runtime, width, height and video codec when the original probe recorded them;
- the source `UserConfirmed`.

It sends no cookies, local filesystem path, Indexer credential, SABnzbd
credential or prdb API key as report data. Delivery still uses the configured
prdb API key as authentication.

## Local records

Reporting switches, the last reported Fulfilment state and Confirmed
Assignments are part of the installation's durable state. Reported state is
scoped to the prdb account that received it; changing accounts does not make a
record from one account suppress a report to another. Operational routine and
gate tallies are local diagnostics and are not reporting payloads.

Automation Rules, their allowed Indexers and size bounds, the current automatic
decision reason, and the copied rule names on a Download Origin are also local
records. They are not sent to prdb. An automatic Download makes the same
Indexer NZB request and SABnzbd `addfile` request as a confirmed manual
Download; automation adds no reporting channel.
