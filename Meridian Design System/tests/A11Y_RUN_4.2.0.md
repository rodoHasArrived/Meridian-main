# Accessibility — browser run 4.2.0 (Chromium, keyboard + ARIA + responsive)

**Date:** 2026-09-01 · **Harness:** `tests/a11y-run.html` (open it; result on `window.__a11y`) · **Engine:** Chromium, no assistive technology attached.

## What this run is, and is not

This is the executable half of `tests/BROWSER_TEST_PLAN.md`: the components are mounted from the compiled bundle, driven with real `KeyboardEvent`s and real focus, and the assertions read what the DOM and accessibility attributes actually contain. The seven archetype templates are loaded at 1280 × 900, 1440 × 900 and 1920 × 1080 and checked for script errors, horizontal overflow, and the 24 px target floor (`guidelines/ACCESSIBILITY.md` § checklist).

It is **not** a screen-reader result. Three questions stay **OPEN** and are printed as such by the harness rather than passed: announcement order and verbosity of composed names, whether `role="status"` insertions are read once or on every arrow-key step, and anything Firefox or WebKit does differently. `guidelines/ACCESSIBILITY.md` does not upgrade its claim on the strength of this run; `release.json` moves `beta → rc` on it, and `rc → stable` only on the NVDA/VoiceOver journeys.

## Keyboard / ARIA matrix — 13 surfaces

| Surface | Check | Result |
|---|---|---|
| Stepper | `<ol>` with progress semantics, `aria-current="step"` once, "Step 2 of 4, current" in the name, no tab roles | PASS |
| SegmentedControl | `radiogroup`, one tab stop, `→` skips a disabled option and both focuses and selects, Home/End | PASS |
| Slider | Label via `aria-labelledby`, formatted `aria-valuetext` (`2.00% → 3.00%`) | PASS |
| Tabs | Roving tablist, every `aria-controls` resolves to a real panel | PASS |
| Select | Combobox named by label **and** value; `↓` opens listbox with `aria-activedescendant` on an `option`; Escape closes and keeps trigger focus | PASS |
| WorksheetGrid | `aria-rowcount/colcount`; `→` moves `aria-activedescendant` to a named cell; Enter edits; Escape returns focus to the grid | PASS |
| EvidenceRail | Real tablist — arrows/End move selection *and* focus; panel labelled by its tab; unproven sections absent, not blank | PASS |
| WorkspaceLayout divider | `separator`, `aria-orientation`, two-region `aria-valuetext`, `→` = +4 % | PASS |
| WorkspaceLayout divider | Stops at `minSize` under repeated `←` | PASS (re-run) — 30× `←` stops at 15 % = `minSize .3` of two size units; the first run pressed 30 keys inside one frame, a harness fault |
| WorkspaceLayout region | Collapsing while focus is inside moves focus to the labelled rail | **FAIL → fixed → PASS on re-run** — focus fell to `<body>`, exactly the defect the 4.1 static audit could not close. `LayoutRegion` now remembers whether focus was inside before a toggle and moves it to the rail (collapse) or the collapse control / body (expand). Re-run: `activeElement=BUTTON "Expand Case"`. |
| WorkspaceLayout bar | *Modified* `role="status"` — mutation count | OPEN — inserted once, never re-rendered across nine steps (0 text mutations); how NVDA reads it is the deferred half |
| ObjectTrail | Disclosure, not menu: no `aria-haspopup`, `aria-controls` resolves, Escape closes and focus stays on the trigger | PASS |
| CommandPalette | Modal dialog + combobox/listbox; `↓` selects; Enter runs; Escape closes and focus returns to the opener | PASS (re-run) — the first automated pass measured focus before the palette's own `setTimeout(0)` focus had fired; harness timing, corrected |
| ActionQueue | Live region present; fates stated in words | PASS on the live region (`aria-live="polite"`); row-fate wording verified on the component's card, the harness could not submit a fixture instruction |

## Responsive matrix — 7 archetypes × 3 widths

Script errors: **0 of 21** loads. Horizontal overflow: **0 of 21**. Every screen rendered (81–451 nodes).

Target size (< 24 px in either axis, measured on the component's own box):

| Screen | Finding | Disposition |
|---|---|---|
| dashboard-workstation | `ObjectTrail` pin star 22 × 13 | **fixed** — `.mds-trail__star` min 24 × 24 · re-run clean |
| analytical-workbench | `SegmentedControl size="sm"` tenor buttons 32 × 17 (12) | **fixed** — `.mds-seg__btn` min-height 24 at both sizes · re-run clean |
| analytical-workbench | `WorkspaceLayoutBar` select and Save / Reset 22 px tall (re-run) | **fixed** — `.mds-wlay__select`, `.mds-wlay__btn` 24 px |
| basket-builder | `SegmentedControl size="sm"` filter 85 × 17 (16) | same fix · re-run clean |
| blank-workstation | `DensityToggle` 86 × 23 | **fixed** — `.mds-den__btn` min-height 24 · re-run clean |
| settings-admin | `TagInput` remove × 14 × 14 (re-run) | **fixed** — 24 × 24 hit box, glyph unchanged, negative margin keeps the chip's height |
| settings-admin | `Checkbox` / `Toggle` hidden `<input>` 18 × 18, 36 × 20 | not a defect — both are wrapped in a `<label>` that is the target; harness now measures the label |
| report-pack · session-start | none | — |

All fixes are component-level, so every template that mounts the primitive inherits them; no template was edited. Second run: 27 PASS · 1 OPEN · 6 FAIL, all six the two target-size items above, fixed after that run; third run expected clean.

## Still deferred (unchanged)

- NVDA + Firefox and VoiceOver + Safari journeys — every row in `tests/BROWSER_TEST_PLAN.md` marked *announced*, *read*, *verbosity*.
- Firefox / WebKit rendering of the responsive matrix.
- `DenseDataTable` virtualised `aria-posinset` accuracy while scrolling (C-11 in the conformance gate, 15 findings) — a scroll-driven check this harness does not yet drive.
- Dark-theme rendered contrast of severity washes (`docs/QA_DARK_AUDIT.md`).
