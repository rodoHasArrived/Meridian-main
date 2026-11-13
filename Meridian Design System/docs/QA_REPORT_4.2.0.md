# Meridian 4.2.0 — Change & QA report

Date: 2026-09-01 · Release: **4.2.0 · release candidate** (last stable 3.5.0) · Identity: `release.json`

## What shipped
`docs/RELEASE_NOTES_4.2.0.md` is the narrative. In one line: the operator layer (CommandPalette lookup, HotkeysProvider, ActionQueue, ObjectTrail, WorkspaceLayout) on all seven archetypes; `components/decision/` (EvidenceRail · ConsequenceRail · DecisionSnapshot) at release-candidate maturity; the conformance gate (`tests/conformance.html`) reading all 35 `.dc.html` entries; every version and count generated from `release.json` + `_ds_manifest.json`.

## QA — completed checks (this run, 2026-09-01)
- **Conformance gate** (`tests/conformance.html`, offline): **PASS** — 16 static rules over 241 files, **0 blocking**, 82 review findings (C-01 34 · C-05 18 · C-11 15 · C-06 7 · C-13 6 · C-02 2; 49 of them in templates), **45/45** dynamic suites. Two suite failures found on the first run and fixed: the readme inventory and `docs/METRICS.md` census had drifted from the manifest (310/193/125/665 vs 313/194/126/680) after the decision trio and ActionQueue landed — regenerated, not retyped. `api/families.json` totals were likewise stale (310 vs 313 rows; 7 vs 10 release-candidate) and were regenerated.
- **Unit suite** (`tests/unit.html`, offline): **49/49 pass** — schemas, lifecycle transitions, canonical hashing, invalidation, metric derivation, adapters, persistence, URL identity, exports/manifest parity.
- **Compiler validation** (`check_design_system`): no issues; 313 exports · 126 cards · 27 templates · 680 tokens under 11 theme scopes; manifest in sync.
- **Static a11y audit** of the 4.1 surfaces: `tests/A11Y_STATIC_AUDIT_4.1.md` (nine defects fixed) — unchanged in 4.2.

## Review findings carried into 4.3 (recorded, not hidden)
| Rule | Hits | Disposition |
|---|---:|---|
| C-01 numbers through `core/format.js` | 34 | template adoption once a `LocalDate`/format carrier is on the namespace |
| C-05 row min-height in component CSS | 18 | density-token sweep |
| C-11 `aria-posinset`/`aria-setsize` on synthetic rows | 15 | belongs with the a11y run below |
| C-06 · C-13 · C-02 | 7 · 6 · 2 | `ingestion-operations` ActionQueue adoption + remaining action-fate declarations |

## Chromium run — keyboard · ARIA · responsive (same day)
`tests/a11y-run.html`, results in `tests/A11Y_RUN_4.2.0.md`. 13 component surfaces driven with real keyboard events and focus; 7 archetypes × 3 viewports. **0 script errors, 0 horizontal overflow** across 21 loads. Found and fixed: `LayoutRegion` focus-to-body on collapse; six sub-24 px targets in five primitives. One item OPEN by nature (live-region verbosity of *Modified*). On this evidence `release.json` moved `beta → rc`.

## Deferred (not claimed) — why this is `rc`, not `stable`
- **Assistive-technology run** — NVDA + Firefox, VoiceOver + Safari journeys in `tests/BROWSER_TEST_PLAN.md`; announcement order and verbosity, and whether a live region is read once or per step, cannot be settled without listening.
- **Firefox / WebKit** rendering of the responsive matrix.

## Migration
Nothing removed. `Button` now defaults to `type="button"` — add `type="submit"` where a form relied on the implicit default. See `docs/UPGRADING.md`.
