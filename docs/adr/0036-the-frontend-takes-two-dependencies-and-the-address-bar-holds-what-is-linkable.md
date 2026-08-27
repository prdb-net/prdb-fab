# The frontend takes two dependencies, and the address bar holds what is linkable

React and Vite, adopted from `prdb-ordeno` whole. Two runtime dependencies
beyond React that it does not have — a router and a server-state library — each
argued from a decision already made here. And one rule that costs nothing now
and cannot be retrofitted: **anything worth linking to lives in the address, not
in component state.**

```
react, react-dom          as prdb-ordeno has them
react-router              library mode only
@tanstack/react-query     everything that came from the server

vite, @vitejs/plugin-react, typescript, oxlint, openapi-typescript
```

## The build is `prdb-ordeno`'s, unchanged

Vite with the React plugin, TypeScript, `tsc -b && vite build`, output straight
into `src/Prdb.Fab.Host/wwwroot` with `emptyOutDir`, and a dev server beside
`dotnet run` that forwards the API to it. `oxlint` as the linter. That is its
ADR 0006 with nothing added, and [ADR 0004](0004-the-stack.md) already fixed the
part that matters — the runtime image carries no Node, so this is a build-time
tree and nothing else.

API types are generated rather than hand-written, which is its ADR 0014.
*From what, and by what*, is
[ticket 06](../../.scratch/build-foundation/issues/06-the-contract-between-the-frontend-and-the-backend.md)'s
to settle; this decision only refuses the alternative, which is a second
description of the API maintained in TypeScript beside the one the backend
already publishes.

## A router, because this tool crossed the line `prdb-ordeno` drew

`prdb-ordeno` routes without a router library and says exactly when that stops
being right: *the moment an address needs a parameter of its own — a scene, a
run in the log — is the moment to weigh a real one.* Its own addresses are a
fixed list two segments deep, which is a `pushState`, a `popstate` listener and
a split.

This tool is past that line, and not by a little:

- [ADR 0028](0028-downloads-are-a-table-of-their-own-and-the-release-view-answers-for-the-video.md)
  makes the review queue **addressable per entry**, because a download that
  stopped at an open entry routes to that entry.
- [ADR 0020](0020-a-setting-exists-where-the-tool-cannot-know-the-answer-and-its-form-is-the-onboarding-step.md)
  gives **every automation rule and every indexer its own route**, on the
  argument that a route landing on a list gives the argument back.
- [ADR 0012](0012-the-library-shows-only-what-is-held-and-the-release-view-is-one-table.md)
  has a library entry page and a release view with two entry points.

Around twenty addresses, seven of them parameterised, three segments deep. So a
router, and the departure is licensed by the sibling project rather than argued
against it.

**Library mode only, and that is a boundary rather than a preference.** React
Router's framework mode brings a server-side runtime, which ADR 0004 ruled out
in the same breath as it chose React: *a framework with a server-side runtime of
its own would add a second runtime to the image for a UI that is a handful of
screens over an HTTP API.* A frontend that quietly needs Node at runtime
invalidates ADR 0034's image.

**One route table, in one module.** `prdb-ordeno`'s reason transfers and is
stronger at this size: the navigation, the routing and the fallback all have to
agree about what exists, and three copies of that list disagree the first time
an address is added. The host serves `index.html` for anything that is not the
API, so reloading `/settings/automation/<rule>` works and the address can be
sent to somebody.

The table itself is **not fixed here**. It follows from ADR 0012, ADR 0018,
ADR 0020 and ADR 0028, and is transcribed when it is built rather than invented
in this decision.

## Three places state lives, and the middle one is the load-bearing rule

**Everything that came from the server belongs to TanStack Query.** Three
requirements were already written before this decision, and each is what that
library is for:

- [ADR 0018](0018-the-status-page-reads-the-loop-and-a-brake-is-not-a-gap.md)
  polls every five seconds **while the page is open**, and must not cause work.
  A hand-rolled poll leaks at exactly the two places that are easy to forget —
  the tab going to the background and the route changing out from under the
  interval.
- [ADR 0022](0022-a-queue-entry-is-one-unmoved-file-with-one-reason-and-a-confirmation-outlives-it.md)
  puts a count in the header of **every** page. That is server state outliving a
  route: one key every surface reads, rather than a context plus a refresh
  protocol each new mutation has to remember to observe.
- A mutation makes several surfaces stale at once — dismissing a queue entry,
  letting go of a download, saving a setting. Invalidation by key is the
  mechanism, and
  the alternative is each screen knowing which other screens it just invalidated.

**Everything linkable belongs in the address.** The library's filters by site,
actor and quality, its search, the selected review queue entry, the release
view's sort. Not component state.

This is the rule that has to be written down rather than assumed, because
nothing breaks visibly when it is ignored. ADR 0012 chose a route over a drawer
**partly because a Gap and a post-restore message need something to link to**,
and ADR 0018 routes every Gap and every Brake at a specific destination. A
filter kept in `useState` does not fail — it just quietly makes those addresses
land somewhere generic, and the decisions that were paid for with a route get
nothing back. Retrofitting it means touching every screen; doing it from the
start costs a hook.

**Everything else is `useState` in the component it belongs to**, and there is
no client-state library. Once the two categories above are removed, what is left
is one component's own business — an open menu, a field being typed into — and a
store for that is machinery around a variable.

## Forms: one implementation, two wrappers, and the server is the validator

No form library. Four connection forms — prdb, SABnzbd, an indexer, the library
root — each **one** component that takes its actions as props: onboarding
surrounds it with *skip* and *continue*, settings with *save*. That is ADR 0020
taken literally, including its reason: a field added later to one of two
implementations is a field missing from the other.

**Validation is the server's verdict**, which is
[ADR 0010](0010-one-password-and-an-onboarding-that-requires-prdb-and-a-library.md)'s
four distinct outcomes reached by the round trip, and ADR 0020 already fixed what
each does — a correction verdict refuses the save, a not-right-now verdict offers
a retry, and nothing saves past a failure. A schema in the browser would either
duplicate that or disagree with it, and the disagreement is the failure mode: a
form that accepts what the backend refuses, or refuses what it would have
accepted. Native constraint validation on the shape of a field is the only thing
done locally.

**Keys are write-only in the markup as well as in the API.** The field renders
empty with a marker saying one is set, saving it empty means unchanged, and
nothing ever renders a key — ADR 0020's rule, restated here because the place it
gets broken is a component, not an endpoint.

## Styling: CSS Modules and tokens, and no component library

**No component library**, and the reason is architectural rather than budgetary.
ADR 0012 chose a route over a drawer, and ADR 0020 chose seven routes over one
long page with anchors. **This tool routes where another would overlay** — which
removes nearly everything one reaches for Radix or Mantine to get: dialog,
popover, drawer, tabs. What is left is tables, grids, native form controls and a
multiple selection, and a dependency that ships a design system to supply those
would also ship a look this repository would then spend its CSS overriding.

**CSS Modules, with tokens on `:root` in one global stylesheet** that also
carries the reset. Modules are built into Vite and cost no dependency. The
prototype in
[`prototypes/11-library-and-release-views.html`](../../prototypes/11-library-and-release-views.html)
already produced the visual language — a dark palette, dense tables, the
quality and confidence pills — and it was concrete enough to settle ADR 0012, so
it is the seed rather than something to redo.

Whether there is ever a light theme is deliberately not decided. With tokens it
stays a later question instead of a rewrite.

The honest cost: `prdb-ordeno`'s single `index.css` would rot across twenty
routes, and modules answer that — but "a stylesheet per component, tokens at the
top" is a discipline this decision *agrees to* rather than one the toolchain
*enforces*, which is precisely what Tailwind would have done instead. That
trade is taken with open eyes, and the first three or four screens are where it
will show if it was wrong.

## What the dependencies mean for the image and for the licence

**For the image, nothing.** ADR 0034 builds the frontend in its own stage on
`$BUILDPLATFORM` and ships no Node, so `node_modules` never reaches a running
container.

**For the licence, something**, and it is the half that is easy to read
backwards. The dependency tree is not distributed; **the built bundle is**, and
it sits inside a published image. So every runtime dependency has to be
compatible with this repository's MIT licence. The four are MIT. A copyleft
dependency here would be a licensing problem rather than a matter of taste, and
that is worth stating once so that the next addition is weighed against it.

## Considered options

**Hand-rolled routing, as `prdb-ordeno` has it.** Rejected on that project's own
stated trigger: parameters. At twenty addresses with seven of them parameterised
this is no longer a `pushState` and a split; it is a matcher, a link component
and a route table written by hand.

**TanStack Router instead of React Router.** Typed parameters are a real gain and
were weighed. Rejected narrowly: it adds a generation step to a build that
ticket 06 is already spending one on, for type safety over a route table that
fits on one screen and changes rarely.

**Hand-rolled server state, as `prdb-ordeno` has it.** It works there, and there
is nothing there that polls or that shares a number across every route. Rejected
here on ADR 0018's five-second poll and ADR 0022's header count, which are the
two shapes a hand-rolled cache gets wrong quietly.

**A client-state library.** Rejected: after server state goes to Query and
linkable state goes to the address, what remains is local to one component.

**A form library.** Rejected: the validation that decides anything is a round
trip returning ADR 0010's four verdicts, and a client schema beside it is a
second source of truth about what the backend accepts.

**A component library, headless or complete.** Rejected under *styling*: the
decisions already made route rather than overlay, so the primitives it would
supply are largely ones this tool does not use.

**Tailwind.** Rejected, and it is the closest call in this decision. It would
enforce the constraint that CSS Modules only agree to. It loses on the
prototype: the visual language exists in plain CSS already, and one person
maintaining twenty routes is not the case Tailwind's constraint system is worth
its markup for.

**React Router in framework mode.** Rejected under *library mode only*: it needs
a runtime the image does not have, and ADR 0004 ruled that out for the whole
frontend rather than for one library.

## Consequences

- **The frontend has four runtime dependencies where `prdb-ordeno` has two.**
  That is a change of posture and is recorded as one rather than as two separate
  additions, so the next proposal is measured against it.
- **A filter or a search that lives in component state is a defect**, not a
  style, because it silently devalues the addresses ADR 0012 and ADR 0018 paid a
  route for.
- **React Router may never move to framework mode** without reopening ADR 0004,
  and the same test applies to any frontend dependency that wants a runtime.
- **Every runtime dependency must be MIT-compatible**, because the bundle ships
  in the image even though the tree does not.
- **The route table is one module**, and navigation, routing and the fallback
  read it rather than restating it.
- **`CONTEXT.md` is unchanged**, for ADR 0034's reason: a library is an artefact,
  not a concept the language needed.
- Ticket 06 inherits the contract and the type generation; ticket 10 inherits how
  this build is wired into CI and the image. Neither is pre-empted here.
- The fog patch about how the five browse surfaces share code now waits on
  ticket 06 alone.
