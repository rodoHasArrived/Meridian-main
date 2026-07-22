# Meridian — Patterns & Architecture Guide

How to choose and compose Meridian components. Grounded in the "Institutional Ops" language:
light workstation, hairline structure, one accent, mono data, no decoration.

---

## Documenting a component

Every component ships a `.d.ts` (the compiler requires it to register the component at all — see
`readme.md`'s "Compilation & consumption"). A sibling **`.prompt.md`** is the second layer —
**required for every component** with a `.jsx`+`.d.ts` (the `prompt-coverage` governance rule
enforces it; coverage is currently 100%): 4–8 lines of prose read by an agent composing a screen
— what the component is *for*, one or two realistic JSX examples (real prop values, not
`foo`/`bar`), and any non-obvious behavior a `.d.ts` signature can't express (state ownership,
keyboard behavior, what silently clamps or drops). The bar is highest for compound components
(`Modal` + 4 partners), anything with a non-standard state model (`NumberInput` keeps local state
past mount), and anything with an easy-to-misuse contract (`ToastProvider` takes no children); a
thin, self-explanatory wrapper over a native element (`Input`) can keep its to one or two lines.

---

## Component decision trees

### Overlays — which container?

| Need | Use | Why |
| --- | --- | --- |
| Block the operator for a confirm/destructive action | **Dialog** | Focus trap + ESC + backdrop; small, centered |
| A form that takes over the screen | **Modal** / **ModalForm** | Header/body/footer scaffold, wider |
| Side panel for inspection without leaving context | **Drawer** | Slides from edge, non-blocking feel |
| Lightweight floating panel anchored to a control | **Popover** | No backdrop weight, positioned to anchor |
| A one-line hover hint | **Tooltip** | Delay + placement, text only |
| Right-click / row actions menu | **ContextMenu** | Keyboard nav, dividers, danger items |

**Rule of thumb:** Dialog blocks, Drawer informs, Popover assists. Never stack two blocking layers.

### Navigation & command

| Need | Use |
| --- | --- |
| Keyboard-first jump to any route / run / symbol / action | **CommandPalette** (Ctrl/Cmd-K) |
| Persistent module switching | **NavRail** |
| Breadcrumb trail within a workstation | **Breadcrumb** |

`CommandPalette` is self-contained: pass `commands` and it registers its own `hotkey` (default
`mod+k`) and manages open state. Group commands so no more than ~6 rows show without a section
header; the selected row carries a 4px left-inset accent, never a full-row background swap.

### Selection inputs — which control?

| Situation | Use |
| --- | --- |
| 2–4 short, always-visible options | **SegmentedControl** |
| 3–8 options, one choice | **RadioGroup** or **Select** |
| Long list, one choice, type-to-find | **Combobox** |
| Many options, several choices | **MultiSelect** |
| Single on/off | **Toggle** (setting) / **Checkbox** (form value) |

### Data display — which surface?

| Data shape | Use |
| --- | --- |
| Search + filters + actions band above any surface | **Toolbar** (+ `ToolbarGroup` / `ToolbarSpacer` / `ToolbarDivider`) — don't hand-roll the band |
| Named, persisted filter/sort/search snapshots ("My breaks", "Live only") | **SavedViews** — pairs with `useTableState.getViewState`/`applyViewState`; presets + save-as + dirty indicator |
| List \| inspector, chart \| blotter split | **SplitPane** — resizable, keyboard, `persistKey` |
| Tabular, any size | **DenseDataTable** / **FilteredDataTable** — both auto-window past 500 rows |
| Non-tabular long list (feed, log, tree rows) | **VirtualizedList** — it renders `<div>` rows, so it can't carry table semantics |
| Hierarchy (accounts, taxonomies) | **TreeView** |
| Master/detail expansion per row | **ExpandableDataTable** |
| Key→value facts | **KeyValueGrid** / **EntitySummary** |
| Single headline number | **MetricCard** + **Gauge** / **LinearGauge** |
| Break/exception cases to resolve (SLA, priority, assignee) | **CaseQueue** — rows are work items, not records; selection is controlled |
| Capital-commitment funding state (called / unfunded / DPI / TVPI) | **CommitmentBar** |

### Messaging — which block?

| Intent | Use |
| --- | --- |
| Before → after field changes (audit/review) | **DiffView** |
| UTC time / signed change value | **Timestamp** / **Delta** — never hand-format |
| Run result / data-health / session notice | **StatusBanner** |
| Inline prose guidance inside content | **Callout** |
| Transient confirmation of an action | **Toast** |
| Persistent "what fired while I was away" rail | **NotificationCenter** — durable complement to Toast |
| Panel crash isolation | **ErrorBoundary** — wrap each independent panel |
| Read-only permission state | **ReadOnlyBanner** — never a Toast; permission state is not transient |
| Empty result set | **EmptyState** |
| Liveness / staleness of a streaming value or provider | **FreshnessIndicator** / **ConnectionDot** |
| SLA posture on a case (due-in / over-by clock) | **SlaChip** — deterministic, pass `now`; never self-ticks |
| Where a figure came from (source · as-of · evidence posture) | **ProvenanceChip** — cell-sized; `EvidenceLink` when the artifact is the subject |

`FreshnessIndicator` encodes the voice rule “name the system, the time, the evidence”
(`Polygon offline · 14:02:11Z`). Use **ConnectionDot** as the bare dot beside a live number;
use the full indicator for provider links and run telemetry. Flat by default — only
`connecting` pulses.

### Charts — which plot?

| Question | Use |
| --- | --- |
| Price action (OHLC) | **CandleChart** |
| Cumulative performance vs benchmark | **EquityCurve** |
| Peak-to-trough decline over time | **DrawdownChart** |
| Distribution of returns / a sample | **Histogram** |
| Order-book depth / volume profile | **DepthChart** |
| Pairwise correlation across series | **CorrelationHeatmap** |
| Daily P&L by calendar day | **PnLCalendar** |
| X/Y relationship across a sample (spread vs vol, factor exposure) | **ScatterChart** |
| Contribution bridge — how gross became net (P&L attribution, NAV walk) | **WaterfallChart** — start/delta/total bars, dashed level connectors |
| Composition of a whole — exposure/allocation share, optional day-Δ heat | **Treemap** — squarified; alpha washes by delta or group |
| Categorical comparison (P&L by sector, fees by venue) | **BarChart** — vertical; `horizontal` for ranked exposure rows |
| Term structure — yield vs tenor (rates, bond baskets) | **YieldCurve** — sqrt-of-years x-scale, prior-date overlay, tenor-pair spread readout |
| Option chain — strikes × calls/puts with greeks | **OptionChainTable** (`trading/`) — straddle layout, ITM wash, spot marker; not a chart but lives in this decision |
| Inline trend, no axes | **Sparkline** |
| Entity / ownership structure (nodes + %-edges) | **OwnershipGraph** — layered top-down; `TreeView` for flat hierarchies without percentages |
| Any of the above in a titled, toolbar'd frame | wrap in **ChartCard** |

---

## Layout

Use the layout primitives instead of hand-rolling flex/grid so spacing tracks tokens:

- **Grid** — equal columns: `<Grid cols={3} gap="lg">`
- **Flex** — a row (or `vertical` column) with `align` / `justify` / `gap`
- **Stack** — semantic directional flex when direction is the point: `<Stack direction="vertical" spacing="md">`

All three accept the spacing scale (`xs sm md lg xl 2xl`) for `gap` / `spacing`, mapping to
`--space-*`. Prefer these over per-element margins.

---

## Forms

Compose forms from the field furniture rather than wiring labels by hand:

```jsx
<FormField label="Account" error={errors.account} required>
  <Combobox options={accounts} value={account} onChange={setAccount} />
</FormField>
```

- **FormField** — bundles label + control + hint/error (the default wrapper)
- **FormRow / FormGrid** — multi-field layout
- **FormSectionLabel / FormDivider** — group and separate
- **FieldInput / FormValidation / FormErrorSummary / Validators** — validation stack
- Reach for **FieldInput** when you want validation built in; **FormField** + a raw control
  when you manage validation yourself.

---

## Prop conventions

Keep new components consistent with the existing surface:

| Concept | Canonical prop | Avoid |
| --- | --- | --- |
| Open/closed state | `open` | `isOpen`, `visible`, `show` |
| Close handler | `onClose` | `onDismiss`, `close`, `onHide` |
| Value change | `onChange(value)` | `onSelect`, `onUpdate` |
| Selected value | `value` | `selected`, `current` |
| Option list | `options` (string \| `{value,label}`) | `items` for selectors |
| Row/collection data | `items` | `data`, `rows` |
| Disabled | `disabled` | `isDisabled`, `enabled` |
| Semantic color | `tone` (`info\|success\|warning\|danger`) | `type`, `variant` for status |

`tone` is for semantic status; `variant` is for visual style options (e.g. Button
`primary\|ghost\|danger\|link`). Don't conflate them.

---

## Keyboard navigation

All overlays and selection controls follow the "institutional ops" keyboard convention:

Screen-level chords and sequences (Ctrl+K, "G D" navigation, single-key actions) bind through
**HotkeysProvider** — mount it once per screen; "?" opens its shortcut cheat sheet automatically.
Don't attach raw `keydown` listeners for shortcuts.

| Control | Key | Behavior |
| --- | --- | --- |
| **Dialog / Drawer / Modal** | `Escape` | Close (if `closeOnEsc=true`) |
| **Dialog / Drawer / Modal** | `Tab` | Cycle focus within dialog; trap at boundaries |
| **Select / Combobox / MultiSelect** | `ArrowDown` / `ArrowUp` | Cycle options |
| **Select / Combobox / MultiSelect** | `Enter` | Confirm selection |
| **Select / Combobox / MultiSelect** | `Escape` | Close menu, keep current selection |
| **TreeView** | `ArrowDown` / `ArrowUp` | Move focus to next/prev node (any nesting level) |
| **TreeView** | `ArrowRight` | Expand node (if collapsed); move to first child |
| **TreeView** | `ArrowLeft` | Collapse node (if expanded); move to parent |
| **TreeView** | `Enter` / `Space` | Toggle expand/collapse |
| **Accordion** | `ArrowDown` / `ArrowUp` | Move focus to next/prev section header |
| **Accordion** | `Enter` / `Space` | Toggle section open/closed |
| **RadioGroup / CheckboxGroup** | `ArrowDown` / `ArrowUp` / `ArrowLeft` / `ArrowRight` | Move focus within group |
| **RadioGroup / CheckboxGroup** | `Space` | Toggle selection |
| **ContextMenu** | `ArrowDown` / `ArrowUp` | Cycle menu items |
| **ContextMenu** | `Enter` | Activate item |
| **ContextMenu** | `Escape` | Close menu |
| **WorksheetGrid** | Arrows / `Home` / `End` / `Ctrl+Home` / `Ctrl+End` / `PageUp` / `PageDown` | Move active cell; row extremes; sheet corners; ±10 rows |
| **WorksheetGrid** (editable) | `Enter` / `F2` / type | Edit cell · `Enter`/`Tab` commit down/right · `Escape` cancels |
| **CoverageMatrix** (interactive) | Arrows / `Home` / `End` (`Ctrl+` corners) | Move focused cell — one tab stop, readout follows focus |
| **CoverageMatrix** (interactive) | `Enter` / `Space` | Activate cell (`onCellClick`) |
| **DepthLadder** (with `onPriceClick`) | `ArrowUp` / `ArrowDown` / `Home` / `End` | Move focused price level — one tab stop, starts at best bid |
| **DepthLadder** (with `onPriceClick`) | `Enter` / `Space` | Fire price click (ticket prefill) |
| **OptionChainTable** (with `onSelect`) | `ArrowUp` / `ArrowDown` / `Home` / `End` | Move focused strike — starts at the money |
| **OptionChainTable** (with `onSelect`) | `Enter` / `Space` | Select strike row |
| **All focusable** | `Tab` / `Shift+Tab` | Move focus forward/backward in DOM order |

**Focus visible:** All interactive elements show a `--focus-ring` outline (2px solid, 2px offset) on keyboard focus. Set `outline-offset: -2px` on elements where internal padding would occlude the outline (e.g., close button inside Dialog header).

**Implementation:** `Dialog`, `Drawer`, and `Modal` share one hook — `core/useOverlayFocus.js` — for the overlay focus contract (initial focus into the panel, Tab cycling at the boundaries, restore-on-close). Don't hand-roll per-overlay focus code; reuse the hook.

---

## Theming & tokens

- Tokens live in `tokens/` and resolve through three layers: **base** (`colors.css`, light) →
  **white-label** (`theme.css`, `[data-brand]`) → **dark** (`colors-dark.css`).
- Components read **final** tokens (`--accent`, `--border`, `--text-primary`), never raw
  `--theme-*` source tokens — that's what makes white-label + dark automatic.
- Dark mode is three scopes: auto (`@media prefers-color-scheme: dark`), manual
  (`[data-theme="dark"]`), and a forced-light opt-out (`[data-theme="light"]`).
- Derived tints/dims (`--green-a10`, `--red-dim`) are `color-mix()` off base tokens — override
  one base token and every derivative follows. Never hardcode a hex in a component; use the
  token with a hex **fallback** only (`var(--border, #B2BAC3)`).
- **Density** ships as token scopes (`body[data-theme-density="compact" | "spacious"]`). Give
  operators the **DensityToggle** to switch them — it writes the attribute (to `document.body`
  by default) and can `persist` the choice. Default clears the attribute.

---

## Print & report packs

`tokens/print.css` carries two layers:

- **Defensive (automatic):** any workstation prints as a document — chrome hidden, scroll
  regions unrolled, table headers repeat, rows don't split across pages.
- **Sheet model (opt-in):** compose printable deliverables from `.mds-sheet` sections — each
  renders as a Letter page card on screen and becomes a real page in print (`@page` Letter,
  0.6in margins). Helpers: `.mds-no-print` (screen-only toolbars), `.mds-avoid-break`, and
  `.mds-sheet__footer` (pins the running footer to the sheet bottom).

Keep print surfaces on `data-theme="light"` — paper is light. `templates/report-pack/` is the
reference deliverable: cover, performance summary (with a live `EquityCurve`), income
statement, positions — the figures tie out across sheets.

---

## Performance

- **Density is live:** table row heights read `--theme-row-height` (32 compact · 40 cozy · 48
  spacious) via `data/useThemeRowHeight.js`, so `DensityToggle` compacts real tables — the
  virtualization math tracks the token too. Pass an explicit `rowHeight` only to opt out.

- **Large lists:** `DenseDataTable` and `FilteredDataTable` auto-window past 500 rows (spacer
  `<tr>`s + a visible slice, same math `VirtualizedList` uses) — no extra prop needed, just don't
  fight it with a `virtualize={false}` override unless you have a reason. Reach for
  **VirtualizedList** directly for a non-tabular list (not inside a `<table>`).
- **Expensive renders:** memoize filter/derive steps (`useTableState` already does); pass stable
  handler identities.
- **CSS injection:** each component injects its stylesheet once (guarded by an `injected` flag) —
  safe to mount many instances.
- **No animation on data:** live numbers update without transition; motion is reserved for
  overlays and disclosure (100–150ms ease).

---

## Composition examples

**Inspector drawer with a tree + facts:**
```jsx
<Drawer open={open} onClose={close}>
  <DrawerHeader title="Account 4000 · Revenue" onClose={close} />
  <DrawerBody>
    <TreeView items={subAccounts} defaultExpanded={["4000"]} />
    <KeyValueGrid rows={facts} />
  </DrawerBody>
</Drawer>
```

**Metric tile with a gauge:**
```jsx
<PanelSurface raised>
  <Eyebrow>Fill rate</Eyebrow>
  <Gauge value={94} label="30d" color="var(--green)" />
</PanelSurface>
```

**Guidance + confirm:**
```jsx
<Callout tone="warning" title="Live environment">
  Orders placed here execute against real capital.
</Callout>
<Dialog open={confirm} onClose={cancel}>…</Dialog>
```

---

## Strategy Executor patterns

The Strategy Executor templates (`templates/strategy-builder`, `field-formula`, `amx-governance`,
`backtest-builder`, `strategy-onboarding`) are composed from existing components only — reach for
these recipes when building cell-based / formula-driven research surfaces.

**Worksheet grid (formula bar + computed cells).** Don't use `DenseDataTable` for a spreadsheet —
it's row-record-oriented. Build the grid from hairline `<div>` cells inside a `PanelSurface flat`,
keep an `active` cell ref in state, and render a formula bar above it (cell ref · `fx` · formula).
Cells are mono (`var(--font-data)`); right-align numerics; mark formula cells with a small accent
corner; tint error cells `var(--red-a10)` with `var(--red-dim)` text. The active cell gets
`outline: 2px solid var(--accent); outline-offset: -2px`. Sticky column/row headers use
`var(--bg-medium)` with a `var(--border-strong)` rule. Mirror the selected cell into a
`KeyValueGrid` inspector (reference · type · formula · value · dependencies).

**Formula composer (field picker + autocomplete).** `Combobox` for the searchable field picker;
a mono editor line for the formula; a floating suggestion list (`PanelSurface` + `--shadow-menu`)
where the selected row carries a **4px accent left inset** (never a full-row background swap, per
the CommandPalette rule). Show keyboard affordances with `Kbd` (`↑ ↓` browse · `↵` insert) and
render the selected suggestion's signature in a `KeyValueGrid` + `Callout` below.

**Review gating.** Pair a `GateRail` (ingest → mapping → proof → review → approval) with a
`SeverityBadge` review status and a proof-state `Callout`; gate the primary **Approve** `Button`
(`disabled`) behind a current backtest proof, with a `Tooltip` explaining why. This is the
operator readiness layer applied to strategy sign-off.

**Charts need a definite height.** `EquityCurve` / `CandleChart` flex-fill their container — give
the wrapping `ChartCard` `style={{ flexShrink: 0 }}` when it's a direct child of a flex-column
`main`, or wrap the chart in a fixed-pixel-height box. `DrawdownChart` / `Histogram` emit an
explicit-height svg and are safe either way.
