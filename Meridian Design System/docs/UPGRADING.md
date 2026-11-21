# Upgrading

One page per behavior change a consumer might feel. Additive releases aren't listed.

## → 4.3

Additive. Nothing removed, nothing renamed. Three things a consumer may feel (and one you will only notice if you measured it — `WorkspaceLayout` dividers now have a 25 px hit box that overlaps 9 px into each neighbouring region's edge; region content that placed its own controls flush against a divider may find the divider wins the click):

- **`security-master-registry`'s inspector no longer has a *Save changes* step** — fields write optimistically through `OptimisticCell`; a rejected write restores the prior value with the reason attached. A consumer that copied the registry template and scripted the staging button has nothing to press.
- **`Button` inside an `AnnotationAnchor`** — the ¶ control stops propagation. If a row's `onClick` relied on bubbling from every descendant, the annotation marker is now the exception, by design.
- **Fifteen new exports are release-candidate** (`api/families.json` › `stability`): the Optimistic, SelectionCompare and Annotation groups. Public and supported; prop shape may move inside 4.3.x. Pin to the named props documented on their cards, not to internal class names.

## → 4.2

**One object identity.** `FinancialObjectType` and `FinancialObjectRef` are **removed** from
`operations/DecisionFlow`. They were a second identity union that omitted `strategy`, so the
seventeenth object existed for the header that displays it and not for the flow that approves it.

```diff
- import type { FinancialObjectRef } from "@meridian/operations/DecisionFlow";
+ import type { ObjectRef } from "@meridian/core/ObjectModel";

- const object: FinancialObjectRef = { type: "journal", id: "JE-20418", version: 3 };
+ const object: ObjectRef = { type: "journal", id: "JE-20418", version: 3 };
```

The shape is identical — `{ type, id, version, label? }` — so this is a rename at every call site
and nothing else. Two differences to know about:

- `type` is now `MeridianObjectType` (strict), not `MeridianObjectType | string`. A custom object
  type must be added to the union in `core/ObjectModel.d.ts`; that is the point.
- `strategy` is now a valid `type` for `DecisionFlowProvider`, `DecisionConsequenceBar` and
  everything downstream. It always should have been.

Runtime behavior is unchanged — these were type-only exports, so a plain JS consumer needs no edit.
Conformance rule **C-15** now fails the build on a second declaration of a canonical domain union
(object identity, reversibility, time roles, materiality classification).

**New: `components/decision/`.** `EvidenceRail`, `ConsequenceRail` and `DecisionSnapshot` are
additive and at `release-candidate` maturity (`api/families.json` › `stability`) — public and
supported, but the shape may still move inside a minor while templates adopt them. Nothing existing
depends on them.

**Version statements are generated.** If you patched a version string in `readme.md` or
`docs/METRICS.md`, it will be overwritten: identity is declared once in `release.json` and rendered
by `scripts/release_identity.py`. This tree is **4.2.0-dev** on the development channel; consume
**3.5.0** unless you are working on the system itself.

## → 4.0

One way to state a value, one way to show a table. Mechanical parts:
`python scripts/codemod_4_0.py <your-screens> --write` (dry-run without `--write`); it renames,
remaps template paths, and flags what needs a human. Plan: `docs/reviews/Release 4.0 Plan.html`.

**Tables.** `DenseDataTable`, `ExpandableDataTable`, `GroupedDataTable`, `FilteredDataTable` are
**removed**. `DataGrid` is the one row-record table; capabilities are props:

- Rename the component; `columns`/`rows` carry over.
- Sorting: `sortable` (grid-internal) or `sortKey`/`sortDir`/`onSort` (screen-owned).
- Committed row: `onRowClick` + `activeKey` (independent of checkbox `select`).
- Expansion → `expand` · grouping → `groupBy` (per-column `total` replaces `aggregateRows`) ·
  filter UI → compose a `Toolbar` above the grid.
- Windowing past 500 rows, column management, `label`/`caption` a11y, `rowKey="index"` are built in.
- **`Blotter` is unaffected** — it now wraps `DataGrid`, public API unchanged.
- **`WorksheetGrid` stays** — it is a cell-addressable worksheet, not a row-record table. The plan
  proposed deleting it (E-01); this is a deliberate deviation, recorded in the census.

**Vocabulary.** `Glyph*` (7 exports, `core/icons.js`) removed — inline the mark or use a badge.
`CONDITIONS` is bundle-internal — read `DataCondition.CONDITIONS`.

**Values.** Stated figures (P&L, NAV, tie-outs) render as `ValueEnvelope`s — value, condition,
authority and clocks travel together. Existing plain renders keep working; envelopes are the
pattern every 4.0 template now demonstrates (`guidelines/VALUE_ENVELOPE.md`). `AmountCell` is the
way in for accounting surfaces: pass `envelope`, or carry it on the data as
`debit: { value, condition, authority }`, and the formatted amount renders through the envelope —
formatting stays in `AmountCell`, provenance in the envelope. `LedgerTable` rows take a row-level
`envelope` that applies to both sides; `TrialBalance` rows take the same; `StatementTable` values
accept the envelope-shaped object. Arithmetic (totals, running balance, the proof, sort) reads the
figure and ignores the facets.

**Figures.** `AnalyticalFigure` is the published-figure contract (question, unit, as-of, source,
condition, readout, method, fallback); `AnalyticalFigureGrid` tiles small multiples. `Figure` is the
parametric plot primitive — reach for it before writing another named chart. It does **not** replace
the domain ladders (`CashFlowLadder`, `MaturityLadder`, `ScheduleLadder`, `IncomeSchedule`,
`DurationContribution`) or matrices (`BasisComparisonMatrix`, `ComparisonPlate`,
`RollforwardMatrix`), despite what its 3.x docstring claimed: those carry bucket-definition,
comparison-mode and value-envelope contracts `Figure` does not model. Nothing to migrate — the
consolidation is deferred to 4.1 (`D-01` in the plan), and it will be a breaking change when it lands.

**Screens.** New screens declare archetype + regions via `ScreenComposition`
(reference: `templates/dashboard-workstation/`). Hand-rolled frames keep working.

**Template merges (32 → 26).** Ten folders retired; every surface is reachable on its survivor
as a `view` tweak / URL param. Full map: `templates/PRODUCTION_SCREENS.md`. Slug redirects:

| Retired folder | Survivor |
| --- | --- |
| `reconciliation-workstation` | `reconciliation` (`view=queue` / `matcher`) |
| `reconciliation-exception` | `reconciliation?view=exception` |
| `close-tie-out` | `close-management?view=tie-out` |
| `strategy-onboarding` | `strategy-formation` |
| `strategy-builder` | `strategy-formation?view=builder` |
| `backtest-builder` | `strategy-runs?view=backtest` |
| `backtest-compare` | `strategy-runs?view=compare` |
| `covered-call-lab` | `strategy-runs?view=lab` |
| `amx-governance` | `strategy-governance` |
| `journaling-workstation` | `accounting-workstation?view=gl&compose=1` |

Copied folders are unaffected — they carry their own `ds-base.js`/`support.js`. The remaps only
matter for links into `templates/` or a re-copy.

## → 3.5

No prop or callback is removed; two primitives change their ARIA ROLE (behavior and visuals are
unchanged) — update tests or CSS that keyed on the old roles:

- **Stepper**: was `role="tablist"`/`"tab"` + `aria-selected`; now an ordered list —
  `<ol role list>` of buttons with `aria-current="step"` and position/state text.
- **SegmentedControl**: was `role="tablist"`/`"tab"`; now `role="radiogroup"`/`"radio"` with
  `aria-checked` and roving tabindex (one tab stop; Arrow keys move AND select).

Live regions: StatusBanner now has `role="status"` (or `role="alert"` for danger) by default —
pass `announce="off"` to restore silent rendering. DataStateSwitch announces non-ready states
the same way; ready children remain unwrapped.

Strategy suite: the `strategy-onboarding` template is now the six-stage Formation screen (the
setup wizard is retired; its thumbnail is archived under `docs/archive/`). Screens read the
shared store via `templates/_strategy-executor/executor-store.js` — copy that folder along with
any executor screen you extract.

## → 3.1

No API migration is required. The release is additive. Primary `Button` actions now render with
the graphite/paper structural axis rather than terracotta; custom CSS that assumed an accent-filled
`.mds-btn--primary` should switch to an explicit branded/selected treatment instead. Light-mode
`--text-on-accent` is dark ink and `--text-on-fill` remains white; custom solid-fill controls should
continue to use the role-appropriate token rather than hardcoded text color.

## → 3.0

**21 product screens were separated from the 7 archetypes.** Upstream 3.0.0 moved them into a
`meridian-screens/` tier staged for extraction into their own project, leaving the picker with the
7 archetype templates. **In this project all 28 live in `templates/`** — the picker indexes that
tree only. Folders you copied earlier are unaffected: anatomy and depth are identical. To copy a
production screen, take `templates/<slug>/` (index: `templates/PRODUCTION_SCREENS.md`).
`session-start` regrouped Admin → Start in the picker.

## → 2.0

**The `Modal*` aliases are removed** — `Modal`, `ModalHeader`, `ModalBody`, `ModalFooter`,
`ModalForm`, deprecated since 1.24. `Dialog` is the one overlay; width is a prop:

```jsx
<Modal open={o} onClose={close}>…</Modal>              // before
<Dialog open={o} onClose={close} size="md">…</Dialog>  // after
```

`ModalHeader`/`ModalBody`/`ModalFooter` → `Dialog*`, `ModalForm` → `DialogForm`. No behavior
change — the aliases were already thin re-exports of the Dialog parts.

## → 1.24

**`Gauge` and `LinearGauge` are removed.** Both had zero usage in any template, and a radial dial
contradicts the system's thesis. `ProgressBar` covers the need and now takes `max` and `target` (a
dotted datum at the reference ratio):

```jsx
<Gauge value={78} label="Fill" />                              // before
<ProgressBar value={78} target={95} label="Fill" showValue />  // after
<LinearGauge value={45} max={60} label="CPU" />                // before
<ProgressBar value={45} max={60} label="CPU" showValue />      // after
```

**`Modal*` is deprecated** (removed in 2.0). `Dialog` is the one overlay; width is a prop. Rename
`Modal`→`Dialog`, `ModalHeader`/`ModalBody`/`ModalFooter`→`Dialog*`, `ModalForm`→`DialogForm`, and
replace any local width override with `size="sm" | "md" | "lg"` (420 / 520 / 720px). The aliases
still work this release, so the migration can be mechanical.

**Default table row height is 36px, not 40px.** The `--theme-row-height` CSS fallback was still the
retired 40px, so a page that loaded the bundle without `styles.css` rendered rows one step taller
than the standard density scope. If a screen depended on 40px rows, pass `rowHeight={40}` explicitly.

**`DockGrid`, `ApprovalRequest`, and `ApprovalsInbox` are removed** — untyped, undemoed exports with
no template usage. Nothing in the system depended on them.

**`ui_kits/` is removed.** Copy from `templates/` instead. Note that `ui_kits/dashboard/components.jsx`
had been compiling into the bundle and assigning `Button`, `DenseDataTable`, `WorkstationShell`,
`MetricCard`, `Input`, `Badge`, `PanelSurface`, `Eyebrow`, `StatusBanner`, `EntitySummary`, `NavItem`,
`DataTable`, and `ToolbarStrip` onto **`window`** — if your page read any of those as bare globals
(rather than from `window.MeridianDesignSystem_205d17`), it was reading a dark-themed lookalike and
will now get `undefined`. Destructure from the namespace.

**`assets/brand/meridian-symbol{,-light,-monochrome}.svg` are removed** — byte-alike copies of
`meridian-mark{,-light,-monochrome}.svg`. Point at the mark.

## → 1.21

**Reference docs moved — update bookmarks and links.** `PATTERNS.md` → `guidelines/PATTERNS.md`, `BRAND_GUIDELINES.md` → `guidelines/BRAND_GUIDELINES.md`; root duplicates of `VISUAL_FOUNDATIONS` / `CONTENT_FUNDAMENTALS` / `ICONOGRAPHY` are gone (`guidelines/` was already the canonical copy). No code, token, or component changes — only doc paths.

## → 1.16

**Dark `--accent-dim` is now lighter than the accent** (`#3C6688` → `#609BC9`). Pressed primary
buttons and accent-dim text in dark mode get brighter, not darker — this fixes two AA failures
(pressed ink 3.11:1; accent-dim-as-text ~2.5:1). Light mode is unchanged. If you used
`--accent-dim` in dark expecting a *dark* fill (e.g. a custom hover on a light-text element),
switch that usage to `--accent-ghost` or a wash.

**Dark `--text-muted` lightened** (`#8893A0` → `#8F9AA7`) so muted captions stay AA on hover
rows. Purely a legibility bump; no action needed.

**Keyboard events on data surfaces.** `DepthLadder` (with `onPriceClick`), `CoverageMatrix`
(with `onCellClick`), and `WorksheetGrid` now handle Arrow/Home/End (+Ctrl/Page variants) keys
and call `preventDefault` on them. If a parent listened for those keys while focus was inside
these components, it will no longer receive them.

## → 1.10

**Table row heights now follow the density token.**
`DenseDataTable` and `FilteredDataTable` previously hardcoded `rowHeight = 40` (dense: 32) for
both CSS and virtualization math. They now read `--theme-row-height` (26 terminal · 28 `compact` ·
36 default · 44 `spacious`), so `DensityToggle` / `body[data-theme-density]` actually resizes tables.

- **If you set no density:** the standard scope is 36px — tables that assumed the old 40px hardcode get one row-step tighter.
- **If you set `data-theme-density`:** tables will now (correctly) change height. If a screen
  relied on tables ignoring density, pass an explicit `rowHeight={40}` to opt out.
- **If you passed `rowHeight`:** unchanged — an explicit prop still wins.

**OrderTicket fields.** If you styled `.mds-ticket__in`/`.mds-ticket__lbl` from outside (you
shouldn't have), those class names are gone — it now composes `Input`/`Select`.

## → 1.9

**Text-on-fill colors.** If you built custom components copying the old `color: white` on
accent/semantic fills, switch to `var(--text-on-accent)` (accent fills) or `var(--text-on-fill)`
(mode/semantic fills) — dark mode flips these to dark ink, and the `white-on-fill` governance
rule now flags hardcoded white in `components/`.

**Overlay focus.** `Dialog`/`Drawer`/`Modal` now trap Tab. If a consumer screen depended on
tabbing out of an open dialog into the page (unlikely, non-conformant), that no longer works.
