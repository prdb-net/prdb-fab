# The settings surface and the onboarding forms are drawn from one kit

There is one form language, in `src/Prdb.Fab.Frontend/src/ui/`, and both
surfaces that have forms use it: everything under `/settings` and the four
connection forms
[ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)
shares between the settings routes and the onboarding wizard. Four rules come
with it. **A verdict has one renderer.** **Every destructive act is confirmed in
the application rather than by the browser.** **Every group of controls is a
real group.** And **a control is never rendered with a default standing in for a
value that has not been read yet.**

ADR 0020 is not amended. It decided what the settings hold and which routes hold
them, and both still stand; this is about how those routes are drawn.

## What was actually there

Every settings screen imported `onboarding/Onboarding.module.css` — a stylesheet
named after one of its two callers — for its fields and buttons, and then
invented the rest for itself. The result was not ugly so much as *inconsistent
in ways that changed what the tool said*.

**A group heading belonged to no control.** `IdentificationScreen` wrote *Allow
an automatic Download after* as a `<label>` bound to nothing, above three radios
that were bound to nothing else either. A screen reader announced a stray
sentence and three unrelated options. The same shape held for the Reporting
switches and for an Automation Rule's allowed Indexers — which is to say, for
every place on this surface where a set of answers is one decision.

**A failed read printed the exception.** `{String(settings.error)}` was on five
screens, so a tool that was still starting told the person `Error: Failed to
fetch`.

**A loading screen answered its own question.** `beforeChoice ??
settings.data?.beforeDownload ?? 'ThroughProbable'` renders the default as a
*selected* answer while the read is in flight and then silently jumps when it
lands. Four screens did this. Somebody who answered during that window answered
a question the tool had not asked yet.

**Saving had no state.** Three hand-rolled `saved` booleans with three different
reset rules, and Save offered when nothing had been edited — so pressing it
meant nothing in particular.

**One field could not be edited.** `const currentCap = cap ||
String(settings.data?.automaticDownloadCap ?? 20)` put the stored value straight
back the moment the input was cleared. The cap could be prefixed and never
replaced.

**A destructive act asked the browser.** `AutomationScreen` deleted a rule
through `window.confirm` with a `\n\n`-joined string, in an application that
already had `ConfirmationDialog` and used it for deleting a Video File.

## Why a kit rather than better discipline

The shapes on this surface are few and they repeat: a labelled field, a group of
named answers, a switch, a verdict, a submit. Six components cover all of it.
What a kit buys is not tidiness — it is that each of the four rules above is
held in *one* place, so it cannot be true on four screens and false on the
fifth. `String(error)` was not a mistake somebody made; it was a mistake
somebody made five times because there was nowhere to put the right answer.

It also goes in in one change rather than screen by screen. A tree holding two
idioms is a tree where the next screen is written in whichever one the author
last read, and the conversion is mechanical enough that spreading it out buys
nothing but the chance to stop half way.

## Where it lives, and why not in either caller

`src/ui/`, beside `settings/` and `onboarding/` rather than inside either.

ADR 0020's *one form, two entry points* is the reason. The connection forms are
shared because a field added later to one of two implementations is a field
missing from the other — and the same is now true of the kit they are drawn
with. Putting it in `settings/` would make the wizard import from a sibling
feature; leaving it in `onboarding/` is what the tree already had, and it is how
five settings screens ended up importing a file called `Onboarding`.

`Onboarding.module.css` keeps what is genuinely the wizard's: the step path, the
skip control and its pending block.

## Four rules, and what each is holding back

**A verdict has one renderer.** `Verdict` takes one of the four tones the
stylesheet already drew — `done`, `warning`, `confirmed`, `refusal` — and is the
only place any of them is rendered. It also settles something nobody had
decided: a refusal is announced as an `alert`, because it is the answer to
something the person just did and there may be nothing else on screen that
changed; the other three are `status`.

**Every destructive act is confirmed in the application.** `ConfirmationDialog`
moves into the kit unchanged and `window.confirm` leaves the tree. A browser
dialog cannot carry the sentence a preview endpoint returns, and ADR 0040 is
emphatic that the backend computes what an act covers and the act takes the
identifiers that were *shown* — a confirmation that cannot show them is a
confirmation of something else. Three call sites outside the settings surface
move with it, because the rule is about the application and not about one
screen.

**Every group is a real group.** `Fieldset` renders a `fieldset` with a
`legend`, and `Choice` and `Switch` are its members, each with its own hint —
because on this surface every named answer has a consequence of its own.
ADR 0006's gates are *set membership*, and what each set admits is the thing a
person is choosing between.

**A control is never rendered with a default standing in for an unread value.**
`Loaded` renders a sentence while a read is in flight, a refusal of the
surface's own with a retry beside it when the read fails, and the form only when
there is something to draw it from. That also removes the reason the defaults
existed: a form mounted after its read can initialise its draft from the real
value with plain `useState`, and comparing the two is what makes a dirty gate
possible at all.

## What the dirty gate replaces, and what it does not

`SaveBar` disables Save while nothing has changed and says which of its reasons
it is disabled for. It is sticky, and that is doing a job: ADR 0036 put this
application in library mode with `<BrowserRouter>`, so there is no `useBlocker`
and no route-level guard against walking away from an edited form. A bar that
stays visible for as long as there is something unsaved is the answer that does
not require a router migration to buy a dialog.

It is not a promise that every form has a meaningful dirty state. Two do not:
the prdb key form, where an empty field means *re-check the stored key* and is
therefore an act; and the SABnzbd form, whose submit asks SABnzbd for its
categories before it stores anything. Both say so where they opt out.

## Considered options

**Leave the CSS shared and fix the defects one screen at a time.** Rejected: the
defects are the same five defects five times, which is what a shared stylesheet
and no shared components produce. Fixing them individually leaves the next
screen free to reproduce them.

**A component library.** Rejected on ADR 0036's own terms — it took two
dependencies deliberately and a third would need an argument this surface cannot
make. Six components of about two hundred lines is not a library.

**Put the kit in `settings/` and have onboarding import from it.** Rejected:
that is the same mistake as today with the direction reversed, and ADR 0020's
shared forms belong to neither surface.

**An unsaved-changes prompt.** Rejected here rather than on principle: React
Router's `useBlocker` needs a data router, and migrating the router to buy a
dialog is a larger decision than this surface can carry. The sticky `SaveBar` is
what stands in its place.

**A frontend test runner, so the kit could be tested.** There is none, and
adding one is a dependency decision ADR 0036 owns rather than a step in a
redesign.

## Consequences

- `src/ui/` exists, and is the third top-level directory in the frontend beside
  a feature folder set. It holds what more than one feature draws with, and
  nothing feature-specific.
- **Nothing under `settings/` imports `Onboarding.module.css`**, and the name
  stops being a lie about who owns those classes.
- `window.confirm` is gone from the application. Two call sites outside this
  epic's surface — stopping following a Download, resetting a Video's Download
  history — were converted with it, because the rule is repo-wide and both
  already had a preview endpoint whose sentence they were flattening into a
  string.
- **One latent defect went with the split.** `Onboarding.module.css` defined
  `.path` twice: a quoted-path block, and the wizard's step list. The later rule
  won, so every filesystem path the SABnzbd step quoted back was rendered as a
  flex row with a counter in front of it. The step list is `.steps` now and the
  block is the kit's.
- Two colour pairs — the danger and primary button variants — exist in both the
  kit's stylesheet and `Filing.module.css`, because the filing screens style
  their own buttons with them and the kit must not reach into a feature's
  stylesheet. That is the one duplication this decision accepts.
- The six tickets after this one rework what individual masks contain. None of
  them adds a second way to draw a field.
