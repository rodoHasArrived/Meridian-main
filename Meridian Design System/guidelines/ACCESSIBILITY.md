# Meridian — Accessibility

**Conformance target: WCAG 2.2 Level AA.** Meridian is an operator workstation for financial
and trading operations — a domain where accessibility is frequently a procurement and regulatory
requirement, not a nicety. This guide states what the system guarantees, what it measures, and
what the consuming application is responsible for.

All contrast figures below are **measured** against the actual Concrete tokens (sRGB, WCAG 2.x
relative-luminance formula), not estimated. Re-run them whenever a base color token changes.

---

## 1. Color & contrast

`scripts/check_contrast.py` is the source of truth. It resolves the active token graph, CSS variables,
alpha compositing, and Meridian’s `color-mix()` semantic washes. The unit suite checks the same
matrix in `tests/test_contrast.py`.

### Default light mode

| Role pair | Measured ratio | Requirement |
| --- | ---: | ---: |
| Primary ink `#20252B` on plate `#F8F7F3` | 14.39:1 | 4.5:1 |
| Secondary ink `#535C65` on plate | 6.35:1 | 4.5:1 |
| Muted ink `#606870` on plate | 5.28:1 | 4.5:1 |
| Muted ink on canvas / recessed / hover | 4.97 / 4.62 / 4.91:1 | 4.5:1 |
| Accent-label ink `#0D1217` on rest / hover / pressed terracotta | 4.70 / 5.93 / 5.27:1 | 4.5:1 |
| Focus blue `#2D6FB3` on plate | 4.85:1 | 3:1 non-text |
| Chrome text / muted / faint on graphite | 11.34 / 6.00 / 4.80:1 | 4.5:1 |
| Paper action ink on graphite primary action | 11.75:1 | 4.5:1 |

### Default dark mode

| Role pair | Measured ratio | Requirement |
| --- | ---: | ---: |
| Primary ink `#F1EFEA` on plate `#171E25` | 14.63:1 | 4.5:1 |
| Secondary ink `#BEC4CA` on plate | 9.56:1 | 4.5:1 |
| Muted ink `#929CA6` on plate / hover | 6.03 / 4.82:1 | 4.5:1 |
| Accent-label ink `#14100E` on rest / hover / pressed terracotta | 6.68 / 8.75 / 7.85:1 | 4.5:1 |
| Focus blue `#5C9FE5` on plate | 6.04:1 | 3:1 non-text |
| Graphite action ink on paper primary action | 13.43:1 | 4.5:1 |

### Role rules

- **Terracotta is a brand and scenario signal, not the primary-action fill.** `Button` primary uses
  `--brand-axis` with `--ink-inverse`. Selected calendar days, checkboxes, or other accent-filled
  controls use `--text-on-accent`.
- **Accent and semantic fills have separate ink roles.** Light mode uses dark ink on terracotta and
  white ink on the darker semantic solids. Dark mode uses dark ink on both lighter families.
- **Semantic text uses the `-dim` variant.** Raw semantic hues are the ≥3:1 border/mark role; the
  `-dim` value is the normal-text role. The checker measures `-dim` text on both `a10` and `a20`
  composited washes.
- **White-label states are checked as a set.** Every brand preset must keep one label color AA on
  its rest, hover, and pressed accent fills.
- **Structural rules may be below 3:1 only when decorative.** Focus, selection, current state, and
  error condition always have an additional rail, wash, label, icon, or marker.
- Disabled controls are exempt from WCAG 1.4.3, but they must remain identifiable and must not be
  the only route to understanding why an action is unavailable.

Do not copy the hex values into components. Consume the semantic token. Any retone requires changes
in the light and dark token files, `parity-manifest.json`, and a clean contrast run.

---

## 2. Keyboard

Meridian is **keyboard-first**. Every operator task is reachable without a pointer. The full
control-by-control key map lives in `guidelines/PATTERNS.md › Keyboard navigation`; the contract:

- **`Tab` / `Shift+Tab`** move through all interactive elements in DOM order. No positive
  `tabindex` is used anywhere — DOM order is the tab order.
- **`Ctrl/Cmd-K`** opens the `CommandPalette` from anywhere — the keyboard path to any route,
  run, symbol, or action.
- **Overlays** (`Dialog`, `Drawer`, `CommandPalette`) close on **`Escape`** and trap
  focus while open.
- **Roving focus** in composite widgets: `Select`/`Combobox`/`MultiSelect`, `TreeView`,
  `Accordion`, `RadioGroup`, `SegmentedControl`, `ContextMenu`, and `Tabs` all move selection
  with the arrow keys and confirm with `Enter`/`Space`. The container is one tab stop; arrows
  move within it.
- **`TreeView`**: `↑/↓` move between visible nodes at any depth, `→` expands/descends, `←`
  collapses/ascends — the standard tree pattern.
- **Data-surface grids are one tab stop each** (roving tabindex / active-cell model — never a
  tab stop per cell):
  - **`WorksheetGrid`** — arrows move the active cell; `Home`/`End` jump to the row extremes,
    `Ctrl+Home`/`Ctrl+End` to the sheet corners, `PageUp`/`PageDown` move 10 rows; `Enter`/`F2`
    (or typing) edits when `editable`, `Enter`/`Tab` commit down/right, `Escape` cancels.
  - **`CoverageMatrix`** (with `onCellClick`) — arrows move the focused cell, `Home`/`End` jump
    within the row (`Ctrl+` for grid corners), `Enter`/`Space` activates; the mono readout
    follows keyboard focus exactly as it follows hover.
  - **`DepthLadder`** (with `onPriceClick`) — the price column is one tab stop starting at the
    best bid; `↑/↓` move between levels, `Home`/`End` jump to the extremes, `Enter`/`Space`
    fires the price click (ticket prefill).
  - **`OptionChainTable`** (with `onSelect`) — `↑/↓` move the focused strike (starting at the
    money), `Home`/`End` jump, `Enter`/`Space` selects the strike row.
- **Coordinated analytical components:**
  - `AnalyticalField` uses arrow keys to move among marks and Enter/Space to commit a selection.
  - `DurationContribution` exposes every ranked contribution as a keyboard-reachable selection.
  - `ControlDependencyGraph` exposes nodes by keyboard and ships a structured relationship outline.
  - `CloseCalendar` provides a dependency-list view when calendar geometry is not suitable.
  - `ContinuityFrame` announces degraded state without removing the previously valid evidence.

**Focus visible:** all interactive elements show the `--focus-ring` (2px solid interaction blue, 1px
offset) on keyboard focus only (`:focus-visible`), so mouse users don't see rings but keyboard
users always do. Where internal padding would clip the ring (e.g. a close button inset in a
header), set `outline-offset: -2px`.

---

## 3. Focus management

| Situation | Behavior |
| --- | --- |
| Overlay opens | Focus moves into the overlay (first focusable / the panel) |
| Overlay open | `Tab` is trapped within the overlay; it cannot escape to the page behind |
| Overlay closes | Focus returns to the trigger that opened it |
| Destructive confirm (`Dialog`) | Focus lands on the safe (Cancel) action, not the destructive one |
| Toast appears | Focus is **not** stolen — toasts are `aria-live` announcements, not interrupts |

---

## 4. Forced colors & automated checks

**Forced colors / Windows High Contrast:** `tokens/contrast-modes.css` (in the global closure)
covers `forced-colors: active`. Meridian's border-carries-structure language survives WHC mostly
intact; the stylesheet adds transparent outlines to the wash-only state marks (selected rows,
status dots) so they repaint in system colors, and pins the progress fill's author color. If you
add a component whose state is communicated by background wash alone, add a rule there.

**Automated contrast checking:** `scripts/check_contrast.py` recomputes the WCAG ratio of every
token pairing in §1's tables — both modes, including the `color-mix` dim tokens — and exits
non-zero on regression; `tests/test_contrast.py` runs it in the suite. The hand-measured tables
above are now documentation of that script's checks, not the source of truth.

---

## 5. Motion (WCAG 2.3.3 · 2.2.2)

Meridian's motion budget is already minimal — 100–150ms color/border eases, no parallax, no
entrance choreography, and **live data updates silently** (no animated number rolls). The only
continuous motions — `Spinner`, the `ProgressBar` indeterminate sweep, the `Skeleton` shimmer,
the `Accordion` caret — all honor **`prefers-reduced-motion: reduce`** (the spinner slows, the
sweep and shimmer stop, the caret snaps). Consumers adding new motion must gate it the same way.

---

## 6. Screen readers & semantics

- **Landmarks/roles:** `Breadcrumb` is a `<nav aria-label="breadcrumb">` with `aria-current="page"`
  on the active crumb. Overlays are `role="dialog" aria-modal="true"` labelled by their title.
  `Toast` stack is `role="status" aria-live="polite"`. `DensityToggle` is a `role="radiogroup"`.
  `Spinner` is `role="status"` with an accessible `label` (defaults to "Loading").
- **Icon-only controls carry an `aria-label`** (close buttons on Dialog/Drawer/Modal, etc.), and
  decorative glyphs (the `⌕` search mark, separators) are `aria-hidden="true"`. When you add an
  icon-only button, you must supply an `aria-label`.
- **Forms:** `Input` sets `aria-invalid` and wires `aria-describedby` to its error text when
  `error` + `errorId` are passed; `FormField` associates `<label>`, hint, and error for you. Use
  `FormField`/`FieldInput` rather than hand-wiring labels so these associations are never missed.
  Never use a placeholder as the only label.

---

## 7. What changed in this revision

`Modal` (the deprecated alias over `Dialog`, removed in 2.0.0) previously lacked the dialog
semantics its siblings had. It was brought to parity with `Dialog`/`Drawer`:

- `role="dialog"` + `aria-modal="true"` on the panel, `aria-labelledby` wired to the
  `DialogHeader` title (the dialog's generated title id).
- **`Escape` closes** the dialog (`closeOnEsc` prop, default `true`).

No visual or layout change; existing call sites are unaffected.

**July 2026 follow-up:** the focus contract in §2/§3 is now actually implemented, once.
`Dialog` and `Drawer` share `core/useOverlayFocus.js` — initial focus into the panel,
**real Tab cycling at the boundaries** (previously documented but not implemented anywhere),
and focus restore to the trigger on close.
`CommandPalette` now also prevents Tab from escaping the palette.

**July 2026 — dark sweep + data-surface keyboard pass:**

- Dark mode is now **fully measured** (§1 dark table) with two token fixes: dark `--accent-dim`
  lightened to `#609BC9` (pressed-button ink was 3.11:1; accent-dim-as-text was ~2.5:1) and dark
  `--text-muted` lifted to `#8F9AA7` (was 4.28:1 on hover rows). `scripts/check_contrast.py`
  gained the pressed/hover/accent-text pairs and composited **wash pairs**, so both regressions
  are machine-checked from now on.
- `DepthLadder` and `CoverageMatrix` gained roving-tabindex keyboard navigation, and
  `WorksheetGrid` gained `Home`/`End`/`Ctrl+corner`/`Page` jumps — contracts in §2. No visual
  change; uninteractive usages (no `onPriceClick`/`onCellClick`) are untouched.

---

## 8. Consumer conformance checklist

The system gives you accessible components; an accessible *screen* still needs these from you:

- [ ] **One `<h1>` per screen**, and a sensible heading order (don't skip levels for size — use
      tokens for size).
- [ ] **Every icon-only button** you add has an `aria-label`.
- [ ] **Every input** is labelled — via `FormField`/`FieldInput` or an associated `<label>`.
      Placeholders are not labels.
- [ ] **Color is never the only signal.** Pair tone with text/icon/position (the trio pattern
      does this for you; preserve it in custom cells).
- [ ] **Live regions for async results** — wrap streaming status in `aria-live` (or use `Toast`).
- [ ] **Keyboard-test every flow**: Tab through it, operate every control, confirm focus is
      visible and never trapped off-screen, and that `Escape` closes overlays.
- [ ] **Respect `prefers-reduced-motion`** in any motion you add.
- [ ] **Don't override the focus ring** to `none` without a replacement of equal/greater contrast.
- [ ] **Target size** — keep custom hit targets ≥ 24×24px (WCAG 2.2 · 2.5.8); dense table rows
      meet this at ~34–40px height.
- [ ] **Re-run the contrast table** if you white-label `--accent` or any base hue (§1).

---

## 9. Known boundaries (honest scope)

- **Reflow / zoom (WCAG 1.4.10):** the supported floor is **1280px logical width at up to 200%
  zoom**. Meridian is a dense multi-pane workstation; at 400% zoom (320px reflow) the split-pane
  and table surfaces do not reflow to a single column, and we do not claim otherwise. Templates
  keep text readable at 200% (no clipped fixed-height text); genuinely small-screen use is out
  of scope for an operator terminal.
- **RTL:** untested and unsupported in this revision. Layouts are LTR-composed (nav rail left,
  inspector right, right-aligned numerals). Numeric/mono content is direction-neutral, but don't
  ship `dir="rtl"` without a dedicated pass over NavRail, SplitPane, and table alignment.

- Meridian guarantees the **component** layer. Screen-level structure (heading order, landmark
  regions, reading order, page `<title>`) is the consumer's responsibility — see the checklist.
- Decorative structural borders sit below 3:1 by design (§1); they are conformant only because
  state is always co-signalled. If you build a custom control whose *only* boundary is a
  `--border` hairline, add a focus ring and a non-color state cue.
- Data-dense tables are accessible as tables; very wide tables still require horizontal scrolling,
  which is acceptable under 1.4.10 (data tables are exempt from full reflow) but should scroll the
  table region, not the page.
- No automated audit replaces manual testing. Validate with a screen reader (NVDA/JAWS/VoiceOver)
  and keyboard-only passes on each new screen.

---

**See also:** `guidelines/PATTERNS.md` (keyboard map, component decision trees) ·
`guidelines/TOKEN_REFERENCE.md` (which token to use when) ·
`guidelines/dark-mode-validation.card.html` (dark surface/text verification).

## 3.5 accessibility extension — keyboard additions

Additive and backward-compatible; role-semantic migration notes in `docs/UPGRADING.md`.

| Surface | Keys |
| --- | --- |
| SegmentedControl | One tab stop · ←/→/↑/↓ move AND select · Home/End |
| WorksheetGrid | Arrows/Home/End/Ctrl+Home/End/PgUp/PgDn move the active descendant · Enter/F2 or type to edit · Enter/Tab commit (focus returns to the grid) · Esc cancels |
| DataGrid | Enter/Space activates the focused row · on a header: Enter sorts · Alt+←/→ resizes · Ctrl+←/→ reorders · P pins |
| Select | ↑/↓/Home/End/typeahead · Enter/Space select · Esc close — announced as a named combobox |
| Stepper | Steps are list items announcing "Step n of m, state"; current step carries aria-current="step" |

## 4.1 accessibility status — session continuity + adapters

| Surface | Keys / semantics |
| --- | --- |
| WorkspaceLayoutGrid dividers | One tab stop each · `role="separator"` with `aria-orientation` · ←/→ (row) or ↑/↓ (column) resize in 4% steps, floored by `minSize` · `aria-valuenow` + `aria-valuetext` naming both regions and their shares |
| LayoutRegion | Collapse and expand carry `aria-expanded` + `aria-controls` on the region body · a collapsed region is a labelled, focusable rail, never removed from the tab order · `hidden` regions are restorable from the bar's *Show N hidden* button |
| WorkspaceLayoutBar | Named group · layout `select` labelled *Active workspace layout* · *Save* / *Reset* name the layout they act on · *Modified* is `role="status"` |
| ObjectTrail | Disclosure with `aria-controls` (no `aria-haspopup` — it is not a menu) · Escape closes · pin toggle reports `aria-pressed` · version-moved notice is `role="status"`, never an alert |
| HandoverBrief | Compound rows are single buttons; a row with its own action renders as a `div` so no button nests in a button · kinds are labelled groups in fixed order |
| AdapterDiagnostics | Rows carry a composed `aria-label` naming every figure with its meaning (the column strip is `aria-hidden`) · `aria-expanded` + `aria-controls` → the detail `role="group"` · posture is always a text label, never colour alone |

**Status of the 4.1 run.** A static audit was executed and is recorded in
`tests/A11Y_STATIC_AUDIT_4.1.md` (nine defects found and fixed). The assistive-technology run
remains **prepared, not executed** — `tests/BROWSER_TEST_PLAN.md` §4.1 lists what only that run can
settle: announcement verbosity, live-region timing, and focus after a region collapses or a detail
closes. Nothing in this document's conformance claims rests on the static pass.

Announcements: StatusBanner and DataStateSwitch take `announce` = polite · assertive · off
(danger/error default assertive); ready content is never wrapped in a live region. Figures:
ChartFrame's `ariaLabel`/`ariaDescription`/`accessibleData` flow through every indexed-series
wrapper; MetricCard's group name carries value, trend, delta, context, tone, and
`sparklineLabel`.
