# Meridian — Accessibility

**Conformance target: WCAG 2.2 Level AA.** Meridian is an operator workstation for financial
and trading operations — a domain where accessibility is frequently a procurement and regulatory
requirement, not a nicety. This guide states what the system guarantees, what it measures, and
what the consuming application is responsible for.

All contrast figures below are **measured** against the actual Concrete tokens (sRGB, WCAG 2.x
relative-luminance formula), not estimated. Re-run them whenever a base color token changes.

---

## 1. Color & contrast

### Text — light mode (foreground on `--bg-light` #FFFFFF panel, unless noted)

| Token | Hex | Ratio | Normal text | Large text |
| --- | --- | --- | --- | --- |
| `--text-primary` | `#22272E` | **15.0:1** | ✅ AAA | ✅ AAA |
| `--text-secondary` | `#4D5967` | **7.1:1** | ✅ AAA | ✅ AAA |
| `--text-secondary` on canvas `#DEE3EA` | — | **5.5:1** | ✅ AA | ✅ AAA |
| `--text-muted` | `#59636F` | **6.1:1** | ✅ AA | ✅ AAA |
| `--text-muted` on canvas `#DEE3EA` | — | **4.7:1** | ✅ AA | ✅ AAA |
| `--text-muted` on `--bg-medium` `#EBEFF4` | — | **5.3:1** | ✅ AA | ✅ AAA |
| `--text-disabled` | `#889099` | 3.2:1 | exempt¹ | exempt¹ |
| `--accent` | `#2F6F8F` | **5.5:1** | ✅ AA | ✅ AAA |
| `--accent-dim` (pressed) | `#255B75` | **7.4:1** | ✅ AAA | ✅ AAA |
| White on `--accent` (primary button) | — | **5.5:1** | ✅ AA | ✅ AAA |
| White on `--accent-dim` (pressed button) | — | **7.4:1** | ✅ AAA | ✅ AAA |
| `--red` | `#BA3F55` | **5.3:1** | ✅ AA | ✅ AAA |
| `--orange` | `#8A520E` | **6.4:1** | ✅ AA | ✅ AAA |
| `--purple` | `#6F5BA7` | **5.6:1** | ✅ AA | ✅ AAA |
| `--green` | `#16885F` | 4.45:1 | ⚠️ see below | ✅ AA |
| `--topbar-text` `#F4F6F8` on `--topbar-bg` `#171A1F` | — | **16.1:1** | ✅ AAA | ✅ AAA |

¹ WCAG 1.4.3 exempts disabled/inactive controls from contrast minimums. Disabled text is
deliberately low-contrast to read as inactive.

> **The one rule that matters: semantic text uses the `-dim` variant, never the raw hue.**
> Raw `--green` (#16885F) is 4.45:1 — *marginally under* the 4.5:1 AA threshold for normal text.
> This is exactly why every chip, badge, and status row renders its label in `--green-dim`
> (the hue mixed 75% toward `--dim-mix`), which is darker and clears AA comfortably. The raw
> hues (`--green`/`--red`/`--orange`/`--purple`) are for **borders and ≥18px/▲ icons** (the
> trio's solid-border role), where the 3:1 non-text / large-text threshold applies and all pass.
> The `colors-states` card encodes this trio; follow it and you are conformant by construction.

### Non-text contrast (WCAG 2.2 · 1.4.11)

- **Focus ring** `--border-focus` (#2F6F8F) on a white panel is **5.5:1** — comfortably above the
  3:1 requirement for focus indicators. Every interactive control exposes it via `:focus-visible`.
- **Structural borders are decorative and intentionally below 3:1** (`--border` #CBD3DC ≈ 1.5:1,
  `--border-strong` #99A5B2 ≈ 2.5:1). This is conformant because the border is **never the sole
  indicator** of a control or its state: inputs also change background on hover/focus and show a
  high-contrast focus ring; selected table rows carry a 3px accent inset *plus* a background wash;
  active nav items carry a 3px accent bar *plus* a wash. Do not rely on border color alone to
  communicate state — pair it with the wash/inset the components already provide.

### Dark mode — measured (graphite charcoal tokens)

Dark mode was fully swept July 2026 with the same measured methodology as the light table.
Foreground on the dark panel `--bg-light` `#1A2026` unless noted.

| Token | Hex | Ratio | Normal text | Large text |
| --- | --- | --- | --- | --- |
| `--text-primary` | `#E5EAEF` | **13.6:1** | ✅ AAA | ✅ AAA |
| `--text-primary` on canvas `#0E1113` | — | **15.7:1** | ✅ AAA | ✅ AAA |
| `--text-secondary` | `#A5AFBC` | **7.4:1** | ✅ AAA | ✅ AAA |
| `--text-muted` | `#8F9AA7` | **5.8:1** | ✅ AA | ✅ AAA |
| `--text-muted` on header band `#232A32` | — | **5.1:1** | ✅ AA | ✅ AAA |
| `--text-muted` on hover row `#283039` | — | **4.7:1** | ✅ AA | ✅ AAA |
| `--text-disabled` | `#5A6574` | 2.8:1 | exempt¹ | exempt¹ |
| `--accent` | `#5790BE` | **4.8:1** | ✅ AA | ✅ AAA |
| `--accent` on canvas `#0E1113` | — | **5.5:1** | ✅ AA | ✅ AAA |
| `--accent-dim` (pressed / accent text) | `#609BC9` | **5.5:1** | ✅ AA | ✅ AAA |
| Dark ink `--text-on-accent` on `--accent` | — | **5.5:1** | ✅ AA | ✅ AAA |
| Dark ink `--text-on-accent` on `--accent-dim` (pressed) | — | **6.3:1** | ✅ AA | ✅ AAA |
| Focus ring `--border-focus` `#5B9FD9` | — | **5.8:1** | (3:1 non-text) ✅ | — |
| `--green-dim` / `--red-dim` / `--orange-dim` / `--purple-dim` | — | **7.0 / 6.3 / 7.6 / 6.8:1** | ✅ AA(A) | ✅ AAA |
| Raw `--green` / `--red` / `--orange` / `--purple` (borders, ≥3:1 role) | — | **5.1 / 4.4 / 5.6 / 4.7:1** | ⚠️ see rule | ✅ AA |
| `--text-on-fill` ink on solid green / red / orange / purple | — | **5.8 / 5.0 / 6.5 / 5.4:1** | ✅ AA | ✅ AAA |
| `--topbar-text` / muted / faint on chrome `#0D1117` | — | **15.7 / 9.3 / 6.0:1** | ✅ AAA/AA | ✅ AAA |

¹ Same WCAG 1.4.3 exemption as light mode.

**The alpha-10/20 washes.** The trio pattern puts `-dim` text on translucent hue washes, so the
effective background is the wash composited over the panel. Measured over the dark panel:
`-dim` text on its own **a10** wash is **6.1 / 5.6 / 6.6 / 6.0:1** (green/red/orange/purple) and
on the heavier **a20** wash **5.2 / 4.9 / 5.5 / 5.1:1** — all AA. The light-mode equivalents
(6.1–8.0:1 on a10 over white) pass too. These composite pairs are now locked in
`scripts/check_contrast.py` (`WASH_PAIRS`), so a wash or hue edit that breaks a chip fails the suite.

**What the sweep changed** (dark tokens only — light mode untouched):

- **`--accent-dim` `#3C6688` → `#609BC9`.** The old value failed two ways: dark ink on the
  pressed primary button was **3.11:1**, and `accent-dim`-as-text (Toast action, ColumnManager
  pin tag, operator RoleBadge) was ~**2.5:1** on dark panels. Dark pressed now goes *lighter*
  (between `--accent` and `--accent-hover`) — the standard dark-UI convention — clearing AA as
  both a fill under dark ink (6.3:1) and as text (5.5:1).
- **`--text-muted` `#8893A0` → `#8F9AA7`.** The old value was AA on the panel but **4.28:1** on
  `--bg-hover` rows — muted captions inside hovered table rows dipped under. Now 4.67:1 on hover,
  5.8:1 on the panel.

The same rule as light mode holds: **semantic text uses `-dim`, never the raw hue** — raw
`--red` is 4.4:1 in dark (borders and large glyphs only). The `dark-mode` and
`dark-mode-validation` cards visually verify every surface/text pair in dark.

**Text sitting *on* a solid fill is a separate question from text on a panel.** White text on
the dark-mode accent measures only **~3.45:1** — this is why `--text-on-accent` flips to dark
ink `#0D1117` in dark mode. **Swept July 2026:** every
other solid-fill + white-text pairing now routes through the same token — accent fills
(`Pagination`, `Stepper`, `Checkbox`, `DatePicker`/`DateRangePicker`, `ColumnChooser`,
`SelectionToolbar`, `BulkActionBar`, `Modal`'s primary button) use `--text-on-accent`; non-accent
semantic fills (the `Badge`/topbar environment chips, `Stepper`'s complete step) use its alias
`--text-on-fill`. Hardcoding white on a solid fill is now a defect, not a legacy pattern.

---

## 2. Keyboard

Meridian is **keyboard-first**. Every operator task is reachable without a pointer. The full
control-by-control key map lives in `PATTERNS.md › Keyboard navigation`; the contract:

- **`Tab` / `Shift+Tab`** move through all interactive elements in DOM order. No positive
  `tabindex` is used anywhere — DOM order is the tab order.
- **`Ctrl/Cmd-K`** opens the `CommandPalette` from anywhere — the keyboard path to any route,
  run, symbol, or action.
- **Overlays** (`Dialog`, `Drawer`, `Modal`, `CommandPalette`) close on **`Escape`** and trap
  focus while open. *(Modal gained `Escape` + `role="dialog"`/`aria-modal` parity in this
  revision — see §6.)*
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

**Focus visible:** all interactive elements show the `--focus-ring` (2px solid accent, 1px
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

`Modal` previously lacked the dialog semantics its siblings had. It now matches `Dialog`/`Drawer`:

- `role="dialog"` + `aria-modal="true"` on the panel, `aria-labelledby` wired to the
  `ModalHeader` title (`id="mdl-title"`).
- **`Escape` closes** the modal (new `closeOnEsc` prop, default `true`).

No visual or layout change; existing call sites are unaffected.

**July 2026 follow-up:** the focus contract in §2/§3 is now actually implemented, once.
`Dialog`, `Drawer`, and `Modal` share `core/useOverlayFocus.js` — initial focus into the panel,
**real Tab cycling at the boundaries** (previously documented but not implemented anywhere),
and focus restore to the trigger on close. `Modal` previously had no focus management at all;
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

**See also:** `PATTERNS.md` (keyboard map, component decision trees) ·
`guidelines/TOKEN_REFERENCE.md` (which token to use when) ·
`guidelines/dark-mode-validation.card.html` (dark surface/text verification).
