# components/ — the eight domains

One folder per domain, dependency arrows point **down** only (`docs/ARCHITECTURE.md`):

| Domain | Layer | Owns |
| --- | --- | --- |
| `core/` | L1 | primitives, forms, overlays, layout, hooks carriers, plus `Datum`, `Capsule`, `ContinuityFrame`, and the canonical `ObjectRef` identity contract every other domain refers to objects through |
| `data/` | L2 | tables + furniture, saved views, and the coordinated `LinkedSelectionProvider` contract |
| `charts/` | L2 | chart primitives plus `AnalyticalFigure`, data-bearing fields, asset/liability ladders, contribution views, and stable dependency graphs |
| `accounting/` | L3 | books-aware surfaces, investment-event vocabulary, proof/tie-out components, period-aware close controls, and posting-impact review |
| `trading/` | L3 | order ticket (live confirm gate), blotter, fills, depth ladder, option chain |
| `operations/` | L3 | readiness, evidence, audit, staged changes, concurrency, resolution, attestation, source coverage, and the guard/action-fate contracts |
| `decision/` | L3 | the decision chain as composition patterns — `EvidenceRail` (why the value is what it is), `ConsequenceRail` (what a commit would change), `DecisionSnapshot` (exactly what was reviewed, frozen) |
| `shell/` | L4 | workstation chrome plus the persistent `WorkspaceContextBar` and provider |

`decision/` is L3 and composes L1–L2 like any other domain surface — the rails are **slot**
compositions, not monoliths: the screen supplies the section bodies from `operations/` and
`accounting/`, and the rail owns the reading order. It exists as its own folder because
"evidence → consequence → proof" is a chain a screen adopts as a unit, and because burying it in
`operations/` is how `core` reached 118 exports.

Every component is a **trio + specimen**: `Name.d.ts` (public contract) · `Name.jsx` (implementation, tokens only) · `Name.prompt.md` (usage brief — required) · rendered in the domain's `*.card.html`. A `.d.ts` without a sibling `.jsx` documents a compound export (e.g. `Validators` ← `FormValidation.jsx`).

**One declaration per domain concept.** Object identity is `ObjectRef` / `MeridianObjectType` in
`core/ObjectModel`; reversibility is `ReversibilityClass` in `operations/DecisionFlow`; time roles
are `TIME_ROLES` in `core/TimeContext`. A domain that re-declares one of these forks the model —
conformance rule **C-15** fails the build for it.

Adding one: checklist in `../CONTRIBUTING.md`. When to reach for which: `../guidelines/PATTERNS.md`. API reference cards: open any `*.card.html`. Public family assignment (the API contract, as opposed to these folders): `../api/families.json`.
