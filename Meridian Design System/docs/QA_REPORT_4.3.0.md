# Meridian 4.3.0 — Change & QA report

Date: 2026-09-08 (cut) · 2026-09-10 (stable) · Release: **4.3.0 · stable** · Identity: `release.json`

## What shipped
`docs/RELEASE_NOTES_4.3.0.md` is the narrative. In one line: the agency layer (`OptimisticCell` · `AnnotationLayer` · `SelectionCompare`) adopted on six production screens plus the `interaction-depth` archetype; Assurance and the Account Sheet in `accounting-workstation`; `LocalDate` on the namespace with all 36 deferred template date/number findings swept; 18 unclassified exports brought into the family contract.

## QA — completed checks (this cut, 2026-09-08)
- **Conformance gate** (`tests/conformance.html`, offline, this cut): **PASS after two fixes** — first run reported *2 to fix*: a C-02 **blocking** hit on `components/core/LocalDate.jsx` (a docstring that spelled out the anti-pattern it exists to replace — reworded; the rule reads prose, the same class as C-07/C-12 on their first run) and the parity suite *every component ships a .prompt.md* missing `Assurance.prompt.md` and `LocalDate.prompt.md` — written. readme inventory, `docs/METRICS.md` snapshot and manifest counts all agree at 334 · 199 · 130 · 28 · 680.
- **Compiler validation** (`check_design_system`): no issues; **334 exports · 130 cards · 28 templates · 680 tokens** under 11 theme scopes; manifest in sync.
- **Family contract** (`api/families.json`): regenerated — **9 families cover all 334 exports, 0 unclassified, 0 phantom, 0 double-assigned**; 28 at release-candidate maturity. Found and fixed: 15 new 4.3 exports and 3 from 4.2 (`ActionQueue` group) in no family.
- **Release identity** (`scripts/release_identity.py` blocks): readme and `docs/METRICS.md` regenerated from `release.json` + manifest + families — no typed count.

## QA — re-run on the 4.3 tree (2026-09-10) — what moved this from rc to stable
- **Unit suite** (`tests/unit.html`, offline): **49/49 pass** on the 4.3 bundle.
- **Chromium keyboard / ARIA / responsive matrix** (`tests/a11y-run.html` → `tests/A11Y_RUN_4.3.0.md`): 16 surfaces (the 13 from 4.2 plus `OptimisticCell`, `AnnotationAnchor`, `CompareTray`/`CompareSheet`) and 8 archetypes × 3 widths (`interaction-depth` added). **40 PASS · 1 OPEN** after fixes. 0 script errors and 0 overflow in 24 loads.
- **Four defects found and fixed by that run**, all component-level: Escape from the OptimisticCell revert dialog dropped focus to `<body>` (`OptimisticCell` and `OptimisticRowBar` now return focus to the Revert control — the harness asserts it); the `WorkspaceLayout` divider's own box was 7 px (now a 25 px target with the same 7 px look); the region collapse / pop-out icon was 20 × 20 (now 24); the `Toggle` label — its whole click target — was 20 px tall (now 24). No template was edited.
- **Two harness faults found and fixed**: an embedded preview only holds system focus during interaction, which silently turned every `focus()`-dependent row into a false FAIL (the harness now reports `document.hasFocus()`, starts on click, re-acquires focus per row, and is drivable in bursts via `window.__a11yRun`); and `input.value` set directly never reached React's `onChange` (native setter now). Both are in `A11Y_RUN_4.3.0.md`.

## Review findings carried into 4.4 (recorded, not hidden)
| Rule | Hits at 4.2 | Disposition |
|---|---:|---|
| C-01 · C-02 template numbers/dates | 34 · 2 | **swept in 4.3** — expect 0 on re-run |
| C-05 row min-height in component CSS | 18 | density-token sweep |
| C-11 `aria-posinset`/`aria-setsize` on synthetic rows | 15 | with the assistive-technology run |
| C-06 · C-13 | 7 · 6 | remaining action-fate declarations; `ingestion-operations` priority now writes through `OptimisticCell`, re-count on re-run |

## Deferred (declared, not claimed) — carried into 4.4
1. **Assistive-technology run** — NVDA + Firefox, VoiceOver + Safari journeys in `tests/BROWSER_TEST_PLAN.md`.
2. **Firefox / WebKit** rendering of the responsive matrix.
3. **`WorkspaceLayoutBar` *Modified* status** — inserted once, never re-rendered; whether NVDA reads it once is the OPEN row above.

## Migration
Additive. `security-master-registry` loses its *Save changes* staging step (writes are optimistic); the ¶ annotation control stops propagation; fifteen exports are release-candidate. `docs/UPGRADING.md › → 4.3`.
