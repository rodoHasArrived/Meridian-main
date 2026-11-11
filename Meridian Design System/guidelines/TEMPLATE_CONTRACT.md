# Template contract

Status: **normative, classified.** All 27 templates are classified against the rules below in
`api/template-eligibility.json`; migration is under way on four of them. Step 1 of the migration
sequence is complete — this document is now measurable, not aspirational.

Raw reference counts are **not** targets. "Put `Datum` in 27 screens" is a worse system, not a
better one. The denominator is the set of templates that satisfy each primitive's **use condition**,
so compliance is *eligible templates conforming*, never *templates referencing*. Of the 270
template × primitive cells, **152 are eligible and 118 are exempt with a stated reason** — three
templates (`asset-sheet`, `blank-workstation`, `session-start`) are eligible for none, and that is
a correct result, not a coverage gap.

The classification is **hand-authored and machine-checked**: eligibility is a design judgment per
screen, so it cannot be generated, but `scripts/check_template_eligibility.py` validates that every
template classifies every primitive exactly once, that every exemption cites a declared reason, that
the reference scan matches, that no screen uses a primitive it claims exemption from, and that the
adoption arithmetic recomputes. Current figures: `docs/METRICS.md`.

## Eligibility rules

| Primitive | Require when | Do not require when | Migration action | Acceptance test |
|---|---|---|---|---|
| `WorkspaceContextBar` / `Provider` | Entity, portfolio, book, basis, period, scenario, currency or as-of changes the data, its meaning, or the permitted actions | A Start or Admin screen carries no analytical context | Move duplicated local context controls into the provider | One context state; changing it updates every dependent plane |
| `LinkedSelectionProvider` | Two or more surfaces respond to the same committed selection | Selection is local to one isolated widget | Delete template-level `selectedId` plumbing | Table, figure, evidence rail and URL agree on the selected object |
| `ContinuityFrame` | Remote or live evidence has a useful last-known-good state | First-load-only static or local content | Wrap the evidence-bearing plane, not the page | Feed failure retains evidence, labels its age and condition, blocks unsafe writes |
| `AnalyticalFigure` | A visualization supports interpretation, comparison, export, evidence or a decision | Purely decorative microvisual | Move question, unit, source, as-of and fallback into the figure contract | Figure is intelligible without hover and has a non-visual exact-value path |
| `Datum` | Zero, target, prior period, selected period, current time, tolerance or parity matters | A reference line would add no analytical meaning | Replace ad-hoc lines and markers with a typed datum | Reference is named, keyboard-readable and geometrically stable |
| `TimeContextProvider` | Two or more time roles are in play, or posting/valuation/as-of can disagree | A single unambiguous timestamp | Declare roles once; read them downstream | No component re-derives a date the context already owns |
| `ObjectHeader` | The screen operates on an identifiable financial object | A pure list or admin form with no active object | Replace domain-specific headers | Object type, id, version and context are legible before any action |
| `DecisionFlowProvider` | A consequential action has ordered prerequisites or guards | A single unguarded edit | Delete local `step` / `isValid` state | Step survives reload; version change invalidates completed review |
| `ActionGuardList` | An action can be blocked by policy, period, authority or evidence | An action that cannot fail for policy reasons | Replace bespoke "you can't do this" copy | Every blocked action states reason, resolution and authority |
| `Capsule` | A module is genuinely configurable, rearrangeable or replaceable | A permanent section that merely needs a border | Use only once move/resize affordances actually work | No visible inert move or resize control; keyboard equivalent exists |

The `Capsule` rule is deliberately restrictive. A drag-only modularity metaphor must not ship as a
production promise, and any functionality that depends on dragging needs a non-dragging alternative.

## Archetype expectations

| Archetype | Almost always | Often | Rarely |
|---|---|---|---|
| Start | — | ObjectHeader | Context bar, decision flow |
| Monitor | Context bar, continuity, linked selection | Datum, analytical figure | Decision flow |
| Investigate | Context bar, linked selection, object header, evidence rail | Decision flow, guards, continuity | Capsule |
| Compose | Context bar, object header, guards | Decision flow, datum | Continuity |
| Report | Context bar, time context, analytical figure | Evidence rail | Decision flow |
| Admin | Guards | Object header | Context bar, continuity, figures |

## Load, degradation and precedence

Data state and continuity answer different questions: data state controls *whether content exists*;
continuity controls *whether previously valid evidence remains usable*.

| Situation | Owner | Required behaviour |
|---|---|---|
| First request, no prior data | `DataStateSwitch` | Loading |
| Valid request, zero records | `DataStateSwitch` | Honest empty state with denominator and filter context |
| Failure before any data rendered | `DataStateSwitch` | Error with retry |
| Feed fails after valid data rendered | `ContinuityFrame` | Retain last-known-good evidence |
| Partial refresh | `ContinuityFrame` | Keep complete evidence; identify the stale portion |
| Context changes, selection invalid | Context + linked selection | Clear or migrate deliberately; never a ghost selection |
| Connection restores | `ContinuityFrame` | Reconcile restored state against the retained snapshot |
| Restored data materially changes the object | `DecisionFlowProvider` | Invalidate stale review, impact and attestation state |

## Mobile: sibling states, not a shrunken workstation

Do not compress a three-pane workstation into 390 px. Preserve context summary, active object and
version, and the decision blockers; then expose **one analytical plane at a time** — Work / Evidence /
Impact — as sibling states. Consequential mobile actions use the review-density target, not the
minimum.

## Retained compatibility screens

A template folder marked `retained` in `api/template-eligibility.json` is an unpublished
compatibility screen kept from before this contract: a `screen.jsx` wrapper that is absent from
the manifest's template list, so the picker never offers it. `check_design_system_governance.py`
reports its entry-contract gaps (preview size, standard tweaks, `DataStateSwitch`, entry name) as a
warning instead of failing on them. The marker is not a way to ship a screen outside the contract:
a retained folder that appears in the manifest's template list fails the gate, and broken routes
fail regardless. Retained screens still count in the eligibility denominator as unmigrated.

## Migration sequence

1. ~~Classify every template by archetype and primitive eligibility; record the eligible set.~~ Done — `api/template-eligibility.json`.
2. Migrate the three coordinated chain workstations first — they carry the most local state.
3. Then every Investigate / Analyze / Monitor equivalent.
4. Then Compose / Report where eligible.
5. Start / Admin selectively, guards first.

Each migration **removes the old local state in the same change** rather than wrapping old behaviour
in a new provider. A template that keeps its own committed selection alongside
`LinkedSelectionProvider` has not been migrated; it has been decorated.

## Acceptance

100% of *eligible* templates satisfy the rules above; no duplicate authoritative context controls; no
committed cross-surface selection held in template state; every eligible remote work plane has
continuity behaviour; every published analytical figure has a non-visual exact-value path; no Capsule
shows an inert motion or resize affordance.
