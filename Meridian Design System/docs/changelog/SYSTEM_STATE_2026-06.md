# Meridian Design System — System State Report

**Date**: June 30, 2026 (component counts refreshed July 1, 2026)
**Status**: Production Ready (v1.9)
**Scope**: 108 component modules across 6 domains + 3-tier token system
**Supersedes**: `SYSTEM_AUDIT_REPORT.md` (June 2026)

---

## Why this report exists

The previous audit (`SYSTEM_AUDIT_REPORT.md`) was a **roadmap** — it inventoried the system at
65 exports / 5 domains and recommended a set of components and fixes. **That roadmap is now
complete.** Nearly every Priority 1–3 recommendation has shipped, and every accessibility gap it
flagged is closed. This report records the current state and resets the forward-looking section to
what is *actually* still open.

---

## The previous roadmap — closed out

### Components: all recommended primitives now ship

| Recommended in last audit | Priority | Status |
| --- | --- | --- |
| Grid, Flex, Stack (layout helpers) | 🔴 1 | ✅ **Shipped** — `core/Grid`, `core/Flex`, `core/Stack` |
| Gauge / LinearGauge | 🔴 1 | ✅ **Shipped** — `core/Gauge`, `core/LinearGauge` |
| FormField wrapper | 🔴 1 | ✅ **Shipped** — `core/FormField` (+ `Form`, `FormValidation`, `Validators`) |
| Combobox | 🟡 2 | ✅ **Shipped** — `core/Combobox` (+ `MultiSelect`) |
| TreeView | 🟡 2 | ✅ **Shipped** — `core/TreeView` |
| VirtualizedTable | 🟡 2 | ✅ **Shipped** — `data/VirtualizedList` |
| Autocomplete | 🟢 3 | ✅ **Covered** by `Combobox` |
| Accordion | 🟢 3 | ✅ **Shipped** — `core/Accordion` |

### Accessibility: every flagged gap closed

| Gap in last audit | Status |
| --- | --- |
| Inputs lacking `aria-describedby` for errors | ✅ `Input` wires `aria-invalid` + `aria-describedby` |
| Modal/Drawer missing `aria-modal="true"` | ✅ Drawer + Dialog already; **Modal fixed this revision** (role/aria-modal/Esc) |
| Breadcrumb missing `aria-current="page"` | ✅ Implemented |
| Icon-only buttons missing `aria-label` | ✅ Close buttons + icon controls labelled |

### Documentation: the missing guides now exist

| Gap in last audit | Status |
| --- | --- |
| No architecture/patterns guide | ✅ `PATTERNS.md` (decision trees, keyboard map, composition) |
| No migration guidance (Modal vs Dialog vs Drawer) | ✅ Covered in `PATTERNS.md` |
| No performance best-practices | ✅ Covered in `PATTERNS.md` + this report §4 |
| No accessibility reference | ✅ **New** — `guidelines/ACCESSIBILITY.md` (measured WCAG data) |
| No token "which-one-when" guide | ✅ **New** — `guidelines/TOKEN_REFERENCE.md` (3-tier model) |

---

## Current inventory (refreshed July 1, 2026)

**108 component modules across 6 domains** (the operations domain is new since the last audit;
several modules are compound, exporting multiple components — e.g. `Modal` → Modal/Header/Body/Footer).
Counts below are machine-counted from the compiled bundle, not hand-tallied — re-derive them
from `_ds_bundle.js` (or `check_design_system`) rather than copying forward by hand, which is how
the previous 80-module figure drifted in the first place.

| Domain | Modules | Notable |
| --- | --- | --- |
| **core** | 65 | Buttons, inputs, overlays (Dialog/Drawer/Modal/Popover), Select/Combobox/MultiSelect, Tabs, Accordion, TreeView, CommandPalette, Gauge/LinearGauge, layout (Grid/Flex/Stack), form stack (FormField/Form/FormValidation/Validators), Toast/ToastProvider |
| **data** | 17 | DenseDataTable, FilteredDataTable, ExpandableDataTable, WorksheetGrid, VirtualizedList, EditableCell, MetricCard, Pagination, ColumnChooser, BulkActionBar, Skeleton/SkeletonTable |
| **charts** | 9 | CandleChart, EquityCurve, DrawdownChart, DepthChart, CorrelationHeatmap, Histogram, ScatterChart, Sparkline, ChartCard |
| **accounting** | 7 | LedgerTable, JournalEntryForm, ReconciliationPanel, StatementTable, TaxLotTable, AccountTree, AmountCell |
| **operations** | 7 | ReadinessPanel, GateRail, SeverityBadge, ValidationIssueList, EvidenceLink, TrustStrip, WorkspaceSection |
| **shell** | 3 | WorkstationTopbar, NavRail, StatusBar |

---

## 1. Accessibility — now measured, not asserted

The previous audit said the a11y baseline was "in place." It now has a **measured, documented
conformance target**: WCAG 2.2 AA, with real contrast ratios computed against the Concrete tokens
(`guidelines/ACCESSIBILITY.md`). Headline facts:

- **Text contrast passes AA on every surface** — primary 15.0:1, secondary 5.5–7.1:1, muted
  4.7–6.1:1. Most pairs clear AAA.
- **The one watch-item is structural and already mitigated:** raw `--green` is 4.45:1 as body text,
  which is precisely why semantic labels use the darker `--green-dim` variant. This is now written
  down as a rule, not tribal knowledge.
- **Focus ring is 5.5:1** (well over the 3:1 non-text minimum); structural borders sit below 3:1
  **by design** and are conformant because state is always co-signalled (wash + inset), never
  border-color alone.
- **Keyboard, focus management, motion, and screen-reader semantics** are documented with a
  consumer conformance checklist.

**Open a11y work:** automated CI contrast checks; a screen-reader test pass (NVDA/VoiceOver) on the
11 templates; focus-trap audit on nested overlays.

---

## 2. Tokens — architecture now documented

The token system is a clean **3-tier model**, now fully written up in `guidelines/TOKEN_REFERENCE.md`:

- **Tier 1 — Theme** (`--theme-*`, `data-brand`, `data-theme-density`): the white-label control
  surface. Six brand presets (indigo/emerald/rose/slate/cyan/amber) + custom accent override; three
  densities.
- **Tier 2 — Semantic** (`--accent`, `--green`, `--text-primary`…): the public API components are
  authored against.
- **Tier 3 — Derived** (`--*-dim`, `--*-a10`, `--severity-*`, `--state-*`): `color-mix()` off Tier 2,
  so re-branding and dark mode propagate automatically.

The guide also resolves the recurring "these sound the same" questions (`text-secondary` vs
`text-muted`, `accent-hover` vs `accent-dim`, `bg-hover` vs `bg-active`) by **role**, and documents
the Concrete constraints (2px radius, flat elevation, 100/150ms motion).

**Open token work:** an automated lint to flag raw hex / Tier-3 authoring in consuming projects
(the `_adherence.oxlintrc.json` hook is the right home for this).

---

## 3. Code quality — status

| Item from last audit | Status |
| --- | --- |
| Standardize prop naming (`open`, `onClose`) | ✅ Consistent across overlays/controls |
| Hardcoded colors in JSX → tokens | ✅ Components author against tokens |
| Add memo to expensive renders | ✅ Verified July 2026 — `useTableState` memoizes filter/search/sort, `FilteredDataTable` uses `useCallback`, `ColumnChooser` uses `useMemo` |
| JSDoc on all exports | 🟡 `.d.ts` coverage is strong; inline JSDoc still uneven |

---

## 4. Performance — honest status

The flat plane and border-driven structure keep CSS cheap. The remaining scale question is
**large-table rendering**:

- `VirtualizedList` now exists and is the answer for 1k+ row lists — **but confirm DenseDataTable /
  FilteredDataTable actually delegate to it** above a row threshold rather than rendering all rows.
- Recommended: a documented "use VirtualizedList above N rows" rule in `PATTERNS.md`, and a
  benchmark of DenseDataTable at 1k / 5k / 10k rows.
- Charts render eagerly; for dashboards with many off-screen charts, consider lazy mount.

---

## 5. What's actually still open

No critical issues. The genuinely remaining opportunities, in priority order:

1. **CI guardrails** — automate the two checks this revision documents manually: contrast (against
   `ACCESSIBILITY.md` §1) and token-tier adherence (raw-hex / Tier-3 lint via the oxlint hook).
2. **Screen-reader pass** on the 11 templates — the component layer is solid; verify screen-level
   structure (heading order, landmarks, reading order) per template.
3. **Table virtualization wiring** — confirm/enable VirtualizedList delegation in the dense tables
   and document the row-count threshold.
4. **Realistic card data** — a few cards still use light placeholder data; richer scenarios
   (out-of-balance reconciliation, multi-currency journal, flagged-formula worksheet) would surface
   edge cases and read as more credible.
5. **Inline JSDoc** — close the gap between `.d.ts` coverage (strong) and in-editor hints (uneven).

---

## Conclusion

Meridian has moved from "mature with a roadmap" to "**roadmap delivered.**" Every component the last
audit recommended has shipped; every accessibility gap is closed; the patterns, accessibility, and
token guides that were missing now exist — the latter two grounded in **measured** data rather than
assertion. Remaining work is guardrails and validation (CI contrast/token lint, screen-reader
passes, virtualization wiring), not net-new construction.
