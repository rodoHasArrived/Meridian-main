# Accessibility — browser run 4.3.0 (Chromium, keyboard + ARIA + responsive)

**Date:** 2026-09-10 · **Harness:** `tests/a11y-run.html` (result on `window.__a11y`) · **Engine:** Chromium, no assistive technology attached · **Tree:** 4.3.0 (`_ds_bundle.js` 334 exports), the six agency-layer adopters and `templates/interaction-depth` included.

## What this run is, and is not

Same contract as `A11Y_RUN_4.2.0.md`: real components from the compiled bundle, real `KeyboardEvent`s, real focus, assertions on what the DOM and accessibility attributes contain; eight archetype templates at 1280 × 900 · 1440 × 900 · 1920 × 1080 checked for script errors, horizontal overflow and the 24 px target floor. It is **not** a screen-reader result — announcement order, verbosity, live-region re-reading and anything Firefox / WebKit does differently stay OPEN. `release.json` moves `rc → stable` on this run **plus** `tests/unit.html` 49/49 on the same tree; `guidelines/ACCESSIBILITY.md` does not upgrade its claim.

## Harness fault found first — and why every 4.2 pass was still honest

The first 4.3 attempt reported 17 FAIL, every one a `focus()`-dependent assertion (`activeElement=BODY` after an arrow key, Escape "not returning" focus). None were component defects: **an embedded preview only holds system focus while the user is interacting with it**, and without it Chromium leaves `document.activeElement` on `<body>` no matter what the component calls. The 4.2 run happened to hold focus throughout; the same rows re-run under focus pass unchanged. The harness now (1) reports `document.hasFocus()` as its first row, (2) starts on the first click when it lacks focus rather than running blind, (3) re-acquires focus before every keyboard row, (4) exposes `window.__a11yRun.keyboard(from, to)` so a driver can run rows in short focused bursts, and (5) waits for a streamed template's control geometry to be stable across three 500 ms samples before measuring targets, instead of a fixed 2.6 s. A second harness fault: it set `input.value` directly, which React's value tracker treats as no change — the OptimisticCell edit never committed. Fixed with the native setter.

## Keyboard / ARIA matrix — 16 surfaces

| Surface | Check | Result |
|---|---|---|
| Stepper | `<ol>` progress semantics, `aria-current="step"` once, "Step 2 of 4" in the name, no tab roles | PASS |
| SegmentedControl | `radiogroup`, one tab stop, `→` skips disabled and both focuses and selects, Home/End | PASS (one attempt lost the `Home` key in the first second after the host granted focus — `End → d · Home → d`; re-run in the same session `Home → a`; same class as the 4.2 timing re-runs) |
| Slider | `aria-labelledby`, formatted `aria-valuetext` `2.00% → 3.00%` | PASS |
| Tabs | Roving tablist, every `aria-controls` resolves | PASS |
| Select | Combobox named by label + value; `↓` opens with `aria-activedescendant` on an `option`; Escape closes, trigger keeps focus | PASS |
| WorksheetGrid | `aria-rowcount/colcount`; `→` moves activedescendant to "B1, 2"; Enter edits; Escape returns focus to the grid | PASS |
| EvidenceRail | Real tablist, arrows + End move selection and focus, panel labelled by tab, unproven sections absent | PASS |
| WorkspaceLayout divider | `separator`, orientation, two-region valuetext, `→` = +4 %, stops at `minSize` (15 %) | PASS |
| WorkspaceLayout region | Collapse with focus inside → focus lands on the labelled rail | PASS (the 4.2 fix holds) |
| WorkspaceLayout bar | *Modified* `role="status"` mutation count | OPEN — inserted once, 0 text mutations over nine steps; how NVDA reads it is the deferred half |
| ObjectTrail | Disclosure not menu, `aria-controls` resolves, Escape keeps trigger focus | PASS |
| CommandPalette | Modal dialog + combobox/listbox; `↓` selects; Enter runs; Escape closes and returns focus to the opener | PASS |
| ActionQueue | `aria-live="polite"` region present | PASS (row fate wording still verified on the card) |
| **OptimisticCell** (4.3) | Provisional is a polite live-region word (`data-status=pending`) → Confirmed; prior value named "was 12%"; Revert… opens dialog "Revert this cell" with the reason field focused; Escape closes **and returns focus to Revert…** | **FAIL → fixed → PASS on re-run.** Escape closed the dialog and focus fell to `<body>` — the same class as the 4.1 LayoutRegion defect. `OptimisticCell` and `OptimisticRowBar` now carry a ref to their revert control and restore focus in an effect when the reason form closes. The harness asserts the return, so the row cannot pass without it. Re-run: `Esc closed=true focus back on Revert…=true`. |
| **AnnotationAnchor** (4.3) | ¶ is a disclosure (`aria-expanded`, no `aria-haspopup`) named "1 annotation, some stale on Weight"; opens dialog "Annotations · Weight"; the row's `onClick` fires 0×; Escape closes and ¶ keeps focus | PASS |
| **CompareTray / CompareSheet** (4.3) | Toggles `aria-pressed`; third toggle disabled with title "Compare set is full (2)"; tray is region "Comparison set" reading "2 of 2"; Compare opens dialog "Comparison by selection"; Escape closes | PASS |

## Responsive matrix — 8 archetypes × 3 widths

Script errors: **0 of 24** loads. Horizontal overflow: **0 of 24**. Final unattended run after all four fixes: **40 PASS · 1 OPEN**, with two rows re-taken in the same session — the first OptimisticCell attempt ran before the host had granted focus, and one settings-admin load was disturbed by a concurrent probe into the same frame (+640 px, not reproducible alone: `overflow=0`). Every screen rendered (81–1039 nodes; `interaction-depth` is the largest at 148 controls).

Target size (< 24 px in either axis; for an `<input>` the owning `<label>` / `.mds-*` wrapper is the target):

| Screen | Finding | Disposition |
|---|---|---|
| analytical-workbench · interaction-depth | `WorkspaceLayout` divider (`role="separator"`, `tabIndex=0`) box 7 px across | **fixed** — `.mds-ldiv` keeps its 7 px of layout and 7 px look but its own box is now 25 px (content-box padding + negative margin, `z-index:1`), so the keyboard target and the drag grab both meet the floor · re-run clean at all three widths |
| analytical-workbench · interaction-depth | `LayoutRegion` collapse / pop-out icon 20 × 20 | **fixed** — `.mds-lreg__icon` 24 × 24 · re-run clean |
| settings-admin | `Toggle` — the `<label class="mds-tog-wrap">` that is the whole click target is 20 px tall (the 36 × 20 track sets it) | **fixed** — `.mds-tog-wrap` `min-height:24px` · re-run clean at all three widths (`under 24px=0`) (4.2 measured the hidden `<input>` and, via the label, the same 20 px — reported then as "not a defect"; it is one, and the floor applies to the label) |
| dashboard-workstation · basket-builder · report-pack · session-start · blank-workstation | none | — |

All fixes are component-level; no template was edited.

## Still deferred (unchanged)

- NVDA + Firefox and VoiceOver + Safari journeys — every row in `tests/BROWSER_TEST_PLAN.md` marked *announced*, *read*, *verbosity*.
- Firefox / WebKit rendering of the responsive matrix.
- `DenseDataTable` virtualised `aria-posinset` accuracy while scrolling (C-11).
- Dark-theme rendered contrast of severity washes (`docs/QA_DARK_AUDIT.md`).
