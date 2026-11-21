# Browser / assistive-technology test plan — CHROMIUM HALF EXECUTED (4.2.0), AT HALF NOT

**4.2.0 note.** The keyboard / ARIA rows below that a browser can answer without a screen reader, and the responsive matrix, were executed in Chromium on 2026-09-01 — `tests/a11y-run.html`, results in `tests/A11Y_RUN_4.2.0.md`. Rows that say *announced*, *read* or *verbosity* are still not claimed.

Per the 3.5 delivery constraints (no browser installation, no network), the following matrix is
prepared for later execution and is **not claimed** anywhere in this release's QA evidence.
`scripts/validate_release.py` prints the same deferral.

**4.1 note.** The 4.1 surfaces were given a *static* pass — roles, names, tab stops, print behaviour
read from source — recorded in `tests/A11Y_STATIC_AUDIT_4.1.md` with nine defects found and fixed.
That pass is not a substitute for anything below; §4.1 states what only the run can settle.

## Keyboard / ARIA matrix (execute with NVDA + Firefox, VoiceOver + Safari)

| Surface | Checks |
| --- | --- |
| Stepper | List semantics announced ("Step n of m, state"); `aria-current="step"` on the current step; no tab/tablist announcements remain |
| SegmentedControl | Announced as radio group; one tab stop; Arrow keys move AND select; Home/End; disabled options skipped |
| Slider | Label announced from `aria-labelledby`; formatted `aria-valuetext` ("0.35Δ") read on change; PgUp/PgDn steps |
| Tabs | No `aria-controls` announced for switcher-only usage; panel focus order after activation |
| Select | Announced "combobox, <label>, <value>, collapsed"; typeahead; `aria-activedescendant` tracking; Esc restores trigger focus |
| WorksheetGrid | Grid announced with row/col counts; arrows move active descendant with cell name (ref, value, formula, error); F2/Enter edit; focus returns to grid after commit/Escape; error cell announces `aria-invalid` |
| DenseDataTable | Region + caption announced; Enter/Space activates row; virtualized: row "n of total" positions accurate while scrolling; select-all + row checkboxes named; Alt+←/→ resize, Ctrl+←/→ reorder, P pin from the header |
| ChartFrame wrappers | Figure name + description read; hidden data table navigable; unlabeled charts silent |
| StatusBanner / DataStateSwitch | polite/assertive/off behave per prop; ready content never re-announced |
| MetricCard | Group name carries value, trend + delta, context, tone, sparkline description |
| Formation screen | DecisionFlowRail step states; ActionGuardList blocking reasons; ConsequenceBar disabled reason; VersionConflictNotice focus management |

## 4.1 surfaces — session continuity + adapters

Static pass: `tests/A11Y_STATIC_AUDIT_4.1.md`. These are the checks it could **not** answer.

| Surface | Checks |
| --- | --- |
| WorkspaceLayout dividers | Announced as separators with orientation; `aria-valuetext` names both regions and shares; arrow keys resize in 4% steps and stop at `minSize`; whether every step re-announces or only the settled value |
| WorkspaceLayout regions | Collapsing a region **while focus is inside it** moves focus to the labelled rail, not to `<body>`; expanding returns focus into the body; *Show N hidden* restores and names what came back |
| WorkspaceLayout bar | *Modified* (`role="status"`) fires once per change, not once per arrow-key step; the layout `select` announces "combobox, Active workspace layout, <name>"; the *Save as…* option's `window.prompt` is reachable and escapable |
| ObjectTrail strip | Disclosure is discoverable (no `aria-haspopup`); Escape closes and focus stays on the trigger; pinned group announces its count; star toggle reads pressed state |
| ObjectTrail version notice | `role="status"` announces the moved version once, on arrival, without stealing focus from the object that just opened |
| ObjectTrailList | Row names read label → type → meta in a usable order; the empty statement is announced as content, not as an error |
| HandoverBrief | Kind group labels announced before their rows; per-row action buttons reachable without leaving the row's name; acknowledge button becomes a statement without a focus trap; reflow at 1280×900 in terminal density keeps the 44px review-row floor |
| AdapterDiagnostics | The composed row name is usable when heard, not merely complete (verbosity judgement); expanded detail is entered in reading order; focus stays on the row when the detail closes; the *retrying with no next attempt* flag is reached before the retry block |
| AdapterPostureChip | Posture is read from its label, never inferred from colour; the inline next-attempt clause does not fragment the chip's name |

## Responsive matrix (1280 / 1440 / 1920 wide · 900 / 1080 tall)

- Formation: 3-column grid collapses to rail-over-content below 1180px; consequence bar never overlaps inputs.
- Builder/Backtest/Runs/Compare/Governance: tables scroll horizontally only after honest reflow; status bars pin.
- Covered-Call Lab: request panel stacks above results below 1180px; sliders remain 44px targets.
- Density modes terminal/compact/default/spacious at each width; dark theme spot-checks.

## Acceptance journey (manual, mirrors `tests/unit.html` final case)

complete formation → resolve worksheet blocker (B5) → run → edit dataset and observe stale proof
+ gated export/promote/approve → revert (or rerun) → compare A/B → approve in Governance →
edit again and observe the approval derive to superseded.
