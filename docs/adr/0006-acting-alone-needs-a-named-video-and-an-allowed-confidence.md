# Acting alone needs a named video and an allowed confidence

The tool acts without being asked — downloading a release, filing a video file
— only when prdb named exactly one video **and** the confidence of that
identification is one of an explicitly listed set of values. The set is
enumerated, never expressed as a minimum to compare against, and there are two
of them: `{Exact, Strong}` after a download, `{Exact, Strong, Probable}` before
one.

`POST /videos/identify` answers with a confidence on a six-value scale, the rung
that matched, and candidates where several videos fit equally well. Turning that
into behaviour is the whole of unattended operation, and it happens twice — once
where being wrong costs bandwidth, once where being wrong puts the wrong file
into the library.

Two properties of the API force the shape. `Ambiguous (5)` is numerically above
`Exact (4)` while meaning the server declined to choose, so any `>=` comparison
lets through the one answer that must never be acted on. And which rung yields
which confidence is undocumented, so a threshold cannot be derived from
`matchedBy` and the values have to be named directly.

## Considered options

**A `videoId` is enough; confidence is display only.** `prdb-ordeno` does
exactly this — its `Recognition.State` reads `videoId != null` as recognised and
shows the confidence without gating on it. Rejected here because that tool never
acts on its own: filing there happens when a person asks for it, so the
confidence has a reader rather than a job. This tool files unattended, and prdb
returns a `videoId` from the `ReleaseName` rung too — acting on that alone would
make a release name sufficient authority to write to the library.

**One threshold for both places.** Simpler to explain and to configure.
Rejected because the evidence available differs absolutely: the hash rungs are
unreachable before a download and always available after one. A single value
high enough to protect the library keeps automation from ever firing, and one
low enough to let automation fire lets release names into the library.

**A threshold per automation rule.** Rejected: it multiplies a value nobody can
calibrate, since the rung-to-confidence mapping is unpublished, and it turns
"why did nothing happen" into a search through every rule.

**Measure the mapping before choosing defaults.** Rejected as a precondition.
The measurement needs a prdb key and real indexer results — the running product
— and the same protection is bought more cheaply by making the gate explain
itself on the sync status page.

## Consequences

- The comparison is set membership. Any code that treats `IdentifyConfidence`
  as an order is wrong, and `Ambiguous` is why.
- `Ambiguous` and a site-only result are separate outcomes rather than points on
  the scale. Neither is ever acted on after a download; both become review queue
  entries, the second carrying its site.
- Before a download the gate does not block a site-only result, so a rule scoped
  to a site remains possible. Such a download cannot be duplicate-checked,
  because the video is unknown.
- Both values are user-movable but floored at `Probable`. Below that lies
  "filing everything however weakly it identified", which is deliberately after
  the first release; a setting must not ship it early.
- The identification made before a download never files anything. The arriving
  file is identified again from its own bytes, and that answer wins — silently
  for the filing decision, while both are kept so the provenance of the download
  still reads correctly.
- The defaults are guesses, so the sync status page gains a job: show which
  confidences came back, and say plainly when the gate blocked everything.
  Without it, a wrong default is indistinguishable from a broken tool.
- Every release and file shows what it was identified as, how strongly and by
  which rung, and a refusal to act is stated as a sentence rather than an
  absence.
