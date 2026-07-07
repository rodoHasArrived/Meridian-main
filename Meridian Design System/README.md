# Meridian Design System — Concrete

**Production-ready · June 2026**

An operator-workstation design system for **Meridian** — a self-hosted trading, market-data, research, and reporting platform. Meridian collects equities data from providers (Polygon, IBKR, Databento…), maintains a security master, runs backfills and quality scans, executes backtests/strategy runs, and operates live & paper trading with accounting and report-pack delivery.

This system is grounded in the **desktop application** (`src/Meridian.Wpf`), whose visual language is **"Institutional Ops"**: a **light** workstation — a concrete-gray canvas (`#DEE3EA`) of flat light panels, capped by a near-black brand bar and status bar (`#171A1F`). One muted steel-blue accent (`#2F6F8F`), desaturated semantic colors used as alpha-10 washes (never solid fills), Segoe UI + Cascadia Mono, and structure built from **visible, load-bearing borders — no shadows, no glow, no gradients**. Think a Bloomberg-grade institutional terminal: utilitarian, flat, hard-edged — not a soft consumer-fintech app.

> **Concrete refresh** stripped the system to its structural essentials: square chips (0px) and crisp 2px controls, flat surfaces (the card shadow is gone — borders carry the elevation), neutral industrial grays in place of warm off-whites, and desaturated steel/spruce/brick/ochre semantics. Dark mode is graphite charcoal, equally flat. Earlier history lives in `docs/changelog/`.

## Sources

- **GitHub:** [rodoHasArrived/Meridian-main](https://github.com/rodoHasArrived/Meridian-main) — the full platform monorepo. The **canonical visual source is the desktop app**: `src/Meridian.Wpf/Styles/` — `ThemeTokens.xaml` (colors), `ThemeTypography.xaml` (type), `ThemeSurfaces.xaml` (cards/badges/metrics), `ThemeControls.xaml` (buttons/inputs/nav). The React web workstation under `src/Meridian.Ui/dashboard/` shares this same light **Institutional Ops** palette — its `src/styles/index.css` is the canonical token source this system is now aligned to. (A separate *generated* HTML dashboard uses a darker console theme; not the reference.)
- **Local codebase mount:** `src/` (WPF desktop under `src/Meridian.Wpf/`, .NET host under `src/Meridian/`).

## Products / surfaces

| Surface | What it is | Template |
| --- | --- | --- |
| **Workstation dashboard** | Operator overview — portfolio metrics, equity curve, positions | `templates/dashboard-workstation/` |
| **Security Master** | Symbol/identifier registry with an entity inspector | `templates/security-master-registry/` |
| **PlotTool** | Charting workstation (OHLC, studies, crosshair readout) | `templates/charting-workstation/` |
| **Accounting** | Books workstation — GL, chart of accounts, trial balance, reconciliation, FX revaluation, gated period close, statements, tax lots | `templates/accounting-workstation/` |
| **Journaling** | Double-entry posting workstation with reconciliation tie-in | `templates/journaling-workstation/` |
| **Basket Builder** | Bond basket construction — constituents, analytics, concentration risk | `templates/basket-builder/` |
| **Report Library** | Report catalog — search/category/period filters, set-parameters-and-run dialog | `templates/report-library/` |
| **Strategy Executor** | Cell-based strategy worksheet — builder grid, field/formula composer, AMX catalog, backtest, run history, run compare, governance, setup wizard | `templates/strategy-builder/` (+6) |
| **Trading Desk** | Live trading — watchlist, depth ladder, order ticket (live confirm gate), blotter & fills | `templates/trading-desk/` |
| **Ingestion** | Data-pipeline ops — provider freshness, backfill queue, coverage heat, run log | `templates/ingestion-operations/` |
| **Report Scheduler** | Scheduled report delivery — packs, recipients, delivery-history timeline | `templates/report-scheduler/` |
| **Reconciliation** | Statement reconciliation — imports, statement runs, SLA-tracked break-case triage, account sign-off | `templates/reconciliation-workstation/` |
| **Family Office** | Consolidated balance sheet, ownership & control graph, capital commitments, evidence posture | `templates/family-office/` |
| **Covered-Call Lab** | Options overwrite research — request params, result metrics, chain preview, run library | `templates/covered-call-lab/` |
| **Session Start** | Sign-in — credentials, MFA, environment gate before any surface loads | `templates/session-start/` |
| **Report Pack** | Printable LP monthly pack — the delivery artifact of the reporting pipeline | `templates/report-pack/` |

## Visual foundations

Full guide: `guidelines/VISUAL_FOUNDATIONS.md`. Tokens live in `tokens/`.

- **Color — light, institutional, concrete.** Canvas `#DEE3EA` → command band `#EBEFF4` → panel `#FFFFFF` → raised `#F3F6F9`; chrome bars near-black `#171A1F`. The canvas is a clear concrete gray so flat white panels read as distinct lifted surfaces. One accent: steel-blue `#2F6F8F` (primary buttons, focus rings, active nav, crosshair — **one per screen**), pressed `#255B75`. Semantic accents are desaturated **trios** (dim text · solid border · alpha-10 wash, never solid fills): green `#16885F`, red `#BA3F55`, amber `#8A520E`, purple `#6F5BA7`. Text ladder: primary `#22272E` · secondary `#4D5967` · muted `#59636F` · disabled `#889099`.
- **Environment modes** are always visible: **Live** red `#BA3F55` (real money), **Paper** blue `#2F6F8F` (simulated), **Fixture** amber `#8A520E` (replay).
- **Readiness & severity** are a first-class semantic layer (`--severity-*`, `--state-*`): every gate, lane, and check resolves to **Ready · Review · Action · Blocked · Info**, encoded by the `operations/` components. The app's own `--ws-*` token names are aliased too, so dashboard CSS resolves verbatim.
- **Dark mode** is graphite charcoal, equally flat — canvas `#0E1113`, panel `#1A2026`, steel accent `#5790BE` for night legibility. Activates via `prefers-color-scheme` or `data-theme="dark"`; force light under OS dark with `data-theme="light"`.
- **Type:** Segoe UI Variable Display (titles, 600), Segoe UI Variable Text (body, 13px), Cascadia Mono / JetBrains Mono (all data, tabular). Ramp (px): page title 24 · section 16 · card title 13 · body 13 · metric 28 (mono 700) · data value 16 · label 9 (small-caps, muted).
- **Spacing & density:** 32 major · 24 section · 16 generous · 12 standard · 6 tight · 3 micro. Chrome: 48px brand bar, 28px status bar, 224px (14rem) nav rail, \~34px table rows. Density scopes via `DensityToggle`: **`terminal`** (densest — 26px rows, for multi-monitor ops walls) · `compact` · default · `spacious`.
- **Radii:** unified **2px** across chips/badges, buttons/inputs, and cards/panels — one tight corner. Metric cards carry a **3px left-accent border**. Hard corners — never friendly radii.
- **Borders & shadows:** structure is **visible, load-bearing borders** (`#CBD3DC`; hover `#ADB8C4`; heavy header/total rules `#99A5B2`). Surfaces are **flat — no card shadow**; the only shadow is a tight hard-edged `0 2px 6px /.18` on floating menus/popovers. **No gradients, no glow, no soft cloud shadows.**
- **Motion:** minimal — 100–150ms ease on background/border/color. No springs, no entrance choreography. Live numbers update without animation.

## Content fundamentals

Full guide: `guidelines/CONTENT_FUNDAMENTALS.md`.

- **Voice:** terse, declarative, evidence-first. "Backfill complete · 412,008 bars · 0 gaps". No exclamation points, no marketing adjectives, no emoji.
- **Casing:** sentence case for UI copy and buttons. Small-caps (not ALL-CAPS) for eyebrow labels, table headers, and badges.
- **Numbers:** mono (Cascadia/JetBrains), tabular, fixed decimals (prices 4dp), explicit signs on deltas, UTC timestamps as `2026-06-09 20:00:00Z`. Counts use thin separators: `412,008`.
- **Errors/status:** name the system, the time, the evidence — "Provider offline · Polygon last seen 14:02:11Z". Never apologize, never anthropomorphize.

## Component inventory (160 exported)

Every name is exposed on `window.MeridianDesignSystem_4f61be`. Compounds export multiple symbols (e.g., `Checkbox.jsx` → `Checkbox` + `Toggle`; `Modal.jsx` → `Modal` + `ModalHeader` + `ModalBody` + `ModalFooter` + `ModalForm`; `Skeleton.jsx` → `Skeleton` + `SkeletonTable`). See `PATTERNS.md` for when to reach for each.

### `core/` — primitives, controls, layout, overlays (68)

**Primitives & controls**: `Button` · `Input` · `NumberInput` · `TextArea` · `Select` · `MultiSelect` · `SelectCheckbox` · `Checkbox` · `Toggle` · `RadioGroup` · `SegmentedControl` · `Slider` · `Combobox` · `Badge` · `Eyebrow` · `Kbd` · `Spinner` · `ProgressBar` · `Gauge` · `LinearGauge` · `Tooltip` · `ContextMenu` · `StatusBanner` · `Callout` · `DensityToggle` · `InstrumentChip` (symbol + venue + asset-class identity micro-chip)

**Command & live status**: `CommandPalette` (Ctrl-K operator surface) · `FreshnessIndicator` · `ConnectionDot` (live-data staleness)

**Disclosure & hierarchy**: `Accordion` · `TreeView`

**Layout & surfaces**: `PanelSurface` · `Grid` · `Flex` · `Stack` · `Form` · `FormRow` · `FormGrid` · `FormDivider` · `FormSectionLabel` · `FormField` · `Modal` · `ModalHeader` · `ModalBody` · `ModalFooter` · `ModalForm` · `Dialog` · `DialogHeader` · `DialogBody` · `DialogFooter` · `Drawer` · `DrawerHeader` · `DrawerBody` · `Popover` · `Tabs` · `TabPanel` · `Breadcrumb` · `Stepper`

**Form helpers**: `DatePicker` · `DateRangePicker` · `FileUpload` · `FieldInput` · `FormValidation` · `FormErrorSummary` · `Validators`

**Content-rule primitives & rails**: `Timestamp` (UTC mono time — full/time-of-day/relative) · `Delta` (explicit-sign change value) · `TagInput` (symbol/recipient chip entry) · `SplitPane` (resizable job and l oil two-panel rail — keyboard, persisted)

**Workstation services**: `ErrorBoundary` (panel crash isolation) · `NotificationCenter` (persistent "while you were away" rail) · `HotkeysProvider` (chord/sequence bindings + the "?" shortcut sheet) · `AsyncCombobox` (type-ahead over 40k options — async, windowed)

**Form & table hooks** (logic, not chrome) — React hooks are lowercase so they're reached through capitalized carrier objects on the namespace: `TableHooks` = { `useTableState` (search/multi-sort/filter), `useTableColumns` (resize/reorder/pin/hide), `useRowSelection` (click/ctrl/shift-range + select-across-pages), `useAsyncTableData` (paged fetch) } · `FormHooks` = { `useDirtyState`, `useAsyncValidation`, `useWizard` } · `ChartCursor` = { `ChartCursorSync`, `useSyncedCursor`, `useChartCrosshair` }. Consume: `const { useTableState } = window.MeridianDesignSystem_4f61be.TableHooks`.

### `data/` — tables & data surfaces (20)

`DenseDataTable` · `FilteredDataTable` · `ExpandableDataTable` · `VirtualizedList` · `EditableCell` · `WorksheetGrid` (spreadsheet cell grid — formula bar, formula/error cells) · `FilterBuilder` (structured field/op/value query rows, AND-combined; compiles a predicate) · `SavedViews` (named, persisted filter · sort · search snapshots — presets, save-as, dirty indicator) · `ColumnChooser` · `ColumnManager` (keyboard-accessible move/pin/hide/resize) · `Pagination` · `BulkActionBar` · `BulkSelectCheckbox` · `SelectionToolbar` · `Toolbar` (+ `ToolbarGroup`/`ToolbarSpacer`/`ToolbarDivider` — the standard band above a data surface) · `EmptyState` · `Skeleton` · `SkeletonTable` · `KeyValueGrid` · `EntitySummary` · `MetricCard`

### `accounting/` — books-aware (12)

`AmountCell` · `LedgerTable` · `AccountTree` · `StatementTable` · `JournalEntryForm` · `TaxLotTable` · `ReconciliationPanel` · `TrialBalance` · `AgingTable` · `FxRevaluationTable` · `AllocationEditor` · `CommitmentBar` (capital-commitment funding state — called/unfunded bar, DPI · TVPI)

### `charts/` (19)

`CandleChart` · `EquityCurve` · `ChartCard` · `Sparkline` · `DrawdownChart` (underwater) · `Histogram` (returns distribution) · `DepthChart` (order-book depth) · `CorrelationHeatmap` · `PnLCalendar` (month-grid daily P&L heat view) · `ScatterChart` · `YieldCurve` (term structure — yield vs tenor, prior-date overlay, tenor-pair spread readout) · `ChartTooltip` (crosshair readout; pairs with the `useChartCrosshair` / `useSyncedCursor` interaction hooks) · `CoverageMatrix` (symbol × session data-availability heat grid — full/partial/gap washes, mono hover readout) · `TimeframeSwitcher` (mono segmented resolution picker — 1m … 1W) · `AsOfControl` (session clock: Live ticking UTC ⇄ frozen as-of with amber AS-OF chip) · `OwnershipGraph` (layered entity/control structure — measured %-labeled edges, DTO-shaped nodes/edges accepted directly) · `WaterfallChart` (contribution bridge — gross → net attribution, NAV walks) · `Treemap` (squarified exposure/allocation map — delta/group alpha washes) · `BarChart` (categorical bars — vertical or ranked horizontal rows, signed tinting)

### `shell/` — (4)

`WorkstationTopbar` · `NavRail` · `StatusBar` · `SessionControls` (→ `UserMenu` + `RoleBadge` + `ReadOnlyBanner` — identity chip/popover, permission chips, read-only strip)

### `operations/` — operator readiness & severity (13)

`SeverityBadge` · `ReadinessPanel` · `GateRail` · `ValidationIssueList` · `EvidenceLink` · `TrustStrip` · `WorkspaceSection` · `DiffView` (before → after field changes for audit rails) · `LogTail` (mono evidence stream — level filter chips, follow-tail that pauses on scroll) · `EventTimeline` (vertical audit trail — severity-dotted rail, day grouping, evidence links) · `CaseQueue` (reconciliation/break case triage — priority rail, status, SLA, assignee) · `SlaChip` (case clock — on track / at risk / breached with due-in · over-by durations) · `ProvenanceChip` (source system · as-of · worst-of evidence/reconciliation dot)

### `trading/` — order entry & executions (5)

`OrderTicket` (side/qty/type/TIF with the live-mode confirm gate) · `Blotter` (orders preset over DenseDataTable) · `FillsFeed` (execution tape) · `DepthLadder` (classic DOM price ladder — washed bid/ask depth bars, spread row with bps, last-price marker, price-click prefill) · `OptionChainTable` (straddle-layout chain — calls | strike | puts, ITM wash, spot marker, greeks/IV/OI columns, keyboard strike selection)

### feedback / notifications (2)

`Toast` · `ToastProvider`

> **Bundle-internal helpers** (lowercase by design, not on the namespace): `useToast`, `useContextMenu`, `useTableState` hooks and `money.js` utilities (`formatMoney`, `sumAmounts`, `toNumber`, `currencySymbol`).

## Iconography

Full guide: `guidelines/ICONOGRAPHY.md`. Meridian ships its own **line-icon module set** in `assets/icons/`, drawn on a 24px grid with 1.5px strokes and `stroke="currentColor"`. Use for nav and module identity at 16–20px. No icon font, no emoji ever. Unicode glyphs appear only as functional marks inside data (`⌕` search, `Ctrl K` kbd, `Δ`, `·` separators, `↑↓` sort).

Brand marks in `assets/brand/`: `meridian-mark.svg`, `meridian-mark-light.svg`, `meridian-mark-monochrome.svg`, `meridian-wordmark.svg`, `meridian-tile-256.png`, `meridian-hero.svg`.

## Documentation & guides

Reference material lives in `guidelines/` (visual + content + the two new references below) and `docs/` (getting-started, API reference, changelog). Cards for each appear in the **Documentation** group of the Design System tab. The tab is organized by domain: **Brand · Colors · Type · Spacing · Depth & Motion · Theming** (foundations), **Core · Data · Charts · Accounting · Trading · Operations · Shell** (component galleries, mirroring `components/`), and **Documentation** (references, API stories, unit tests, the system map).

- **`guidelines/VISUAL_FOUNDATIONS.md`** — the "why" behind Concrete (color, type, surface, motion).
- **`guidelines/CONTENT_FUNDAMENTALS.md`** — voice, number formatting, evidence-first copy.
- **`guidelines/ICONOGRAPHY.md`** — the 24px line-icon set and usage.
- **`guidelines/TOKEN_REFERENCE.md`** — *new.* The 3-tier token model (override Tier 1, author Tier 2, never hardcode Tier 3), white-label entry points, and the `text-secondary`-vs-`text-muted` / `accent-hover`-vs-`accent-dim` disambiguations.
- **`guidelines/ACCESSIBILITY.md`** — *new.* WCAG 2.2 AA conformance with **measured** contrast for every token, the keyboard/focus/ARIA matrix, and a consumer conformance checklist.
- **`guidelines/ENTITY_SCHEMAS.md`** — *new.* The shared wire shapes (Order, Fill, Alert, Run, Instrument) that Blotter, FillsFeed, NotificationCenter, and the templates all speak — map once, drop in everywhere.
- **`guidelines/STATES_AND_ANTIPATTERNS.md`** — *new.* The five data states every surface owes (loading/empty/filtered-empty/error/ready), per-component edge cases, and do→don't anti-patterns.
- **`PATTERNS.md`** — when to reach for each component; composition and decision trees.
- **`docs/PERFORMANCE.md`** — *new.* What a consumer pays: measured payload (135 KB gzip bundle · 13 KB CSS closure · React peer), CSS-injection behavior, and the monolith trade-off.
- **`docs/UPGRADING.md`** — per-release consumer migration notes; **`CHANGELOG.md`** — semver history.
- **`docs/changelog/SYSTEM_STATE_2026-06.md`** — current system-state report (supersedes the older audit).

## Compilation & consumption

**Components compile to `_ds_bundle.js`** — consume via `const { Button } = window.MeridianDesignSystem_4f61be` after loading the bundle. Each component injects its own scoped CSS keyed to the design tokens, so a consuming page only needs to link `styles.css` and the bundle.

**Templates in `templates/<slug>/`** are the starting point for consuming projects:

1. Copy the folder to your project
2. Open `<Slug>.dc.html` (a Design Component file — directly editable in the browser)
3. Edit the markup, props, or logic
4. Adjust the `base` path in the sibling `ds-base.js` if the template sits at a different depth

**Start here →** if you're new to Meridian, copy **`templates/dashboard-workstation/`**. It is the recommended entry point: a complete operator overview (topbar, nav rail, status bar, metric row, equity curve, positions table) that exercises the most components in one screen, so it doubles as a working reference. From there, `security-master-registry` and `charting-workstation` add the data-registry and analysis surfaces; `accounting-workstation` and `journaling-workstation` add the books surfaces; `basket-builder` and `report-library` add a portfolio-construction workflow and a report catalog. Need a bare shell instead — masthead, nav rail, status bar, empty canvas, no hardcoded content? Copy `templates/blank-workstation/` and build up from there.

**Strategy Executor suite** — seven cross-linked screens that compose the research workflow end to end, each a workstation sharing one nav rail (click a nav item to move between them): `strategy-builder` (the redesigned cell worksheet — formula bar, computed grid, parameter & inspector rails), `field-formula` (field picker + live formula autocomplete with a signature panel), `amx-governance` (AMX field catalog with lineage/payloads beside the review/audit rail), `backtest-builder` (config, KPI metrics, equity curve, drawdown, returns histogram, trade blotter), `strategy-onboarding` (the four-step setup wizard), and `strategy-runs` (the run-history ledger — backtest/paper/live runs with status, metrics, and run-trace detail; every other screen in the suite links to it). `backtest-compare` sets any two runs side by side — KPI deltas, overlaid equity curves, and the configuration diff. They reuse existing components only — no new primitives — and demonstrate the worksheet and formula-composer patterns below.

**Template index** (25):

| Template | One-liner |
| --- | --- |
| `dashboard-workstation` | Portfolio overview — metrics, equity curve, positions drill-in (**start here**) |
| `blank-workstation` | Bare shell — masthead, nav rail, status bar, empty canvas |
| `alerting-workstation` | Alert triage — severity table, split inspector, diff audit rail, loading/empty/error states |
| `settings-admin` | Forms-heavy admin — validation stack, recipients, danger zone with typed confirm |
| `charting-workstation` | Candles, depth, indicators — the analysis surface |
| `security-master-registry` | Instrument registry — the data-registry surface |
| `accounting-workstation` | Books — modular GL/recon views, FX reval, gated period close (lock → read-only) |
| `journaling-workstation` | Trade journaling |
| `basket-builder` | Portfolio-construction workflow |
| `report-library` | Report catalog & statements |
| `trading-desk` | Live trading — watchlist, depth ladder, order ticket (live confirm gate), blotter & fills |
| `ingestion-operations` | Data-pipeline ops — provider freshness, backfill queue, coverage heat, run log |
| `report-scheduler` | Scheduled report delivery — packs, recipients, delivery-history timeline |
| `strategy-builder` | Cell worksheet — formula bar, computed grid, parameter/inspector rails |
| `field-formula` | Field picker + live formula autocomplete |
| `amx-governance` | Field catalog with lineage beside the review/audit rail |
| `backtest-builder` | Backtest config, KPIs, equity/drawdown/histogram, trade blotter |
| `strategy-onboarding` | Four-step setup wizard |
| `strategy-runs` | Run-history ledger with run-trace detail |
| `reconciliation-workstation` | Statement reconciliation — imports, runs, SLA-tracked case triage, sign-off queue |
| `family-office` | Family-office workspace — balance sheet, ownership graph, commitments, evidence |
| `covered-call-lab` | Options overwrite research — params, strategy-vs-underlying, chain preview, run library |
| `session-start` | Sign-in — credentials, MFA verify, environment gate (typed LIVE confirm), role |
| `backtest-compare` | Two runs side by side — KPI deltas, overlaid equity curves, configuration diff |
| `report-pack` | Printable monthly report pack — Letter sheets over the design-system print layer |

## Core patterns & rules

**Data-rich UI over whitespace.** Tight rows, hairlines, small-caps labels, tabular numbers. One teal-blue primary action per screen; everything else ghost/link. Never invent colors — use tokens; tint only via the semantic alpha-10/alpha-20 overlays.

**Accounting & money flows.** All currency goes through `AmountCell` — mono tabular, fixed decimals, accounting parentheses for negatives, zero-as-dash. Tables prove their own arithmetic: `LedgerTable` flags imbalance, `ReconciliationPanel` flags "Out by …", `JournalEntryForm` gates Post on balance, `StatementTable` double-rules totals, `AccountTree` rolls child balances, `TaxLotTable` classifies long/short and computes basis/unrealized, `TrialBalance` proves Σdebit = Σcredit across sections, `AgingTable` escalates late buckets, `FxRevaluationTable` nets unrealized G/L, `AllocationEditor` splits totals cent-exactly.

**Structured editing.** Most tables support inline editing (click a cell → edit → blur to save). Bulk operations are batch-select-then-act. Prefer expandable detail rows with `ExpandableDataTable` over modals. Context menus appear on right-click for row-specific actions.

**Environment visibility.** Every workstation displays a Live/Paper/Fixture badge so operators never mistake simulation for production.

**Print & report packs.** `tokens/print.css` makes any workstation print as a document (chrome hidden, scroll regions unrolled, headers repeat) and ships the opt-in `.mds-sheet` page model for composed deliverables — Letter page cards on screen, real `@page` pagination in print, with `.mds-no-print` / `.mds-avoid-break` / `.mds-sheet__footer` helpers. `templates/report-pack/` is the reference; see `PATTERNS.md › Print & report packs`.

## Architecture

- **`core/` (68)** — primitives, layout, forms, modals, tabs, toasts (with Undo), tooltips, context menus, command palette, density toggle, freshness status, timestamps/deltas, tag input, split pane, error boundary, notification center, hotkeys, async combobox, instrument chip
- **`data/` (20)** — tables + furniture (pagination, column chooser + keyboard column manager, bulk/selection, toolbar band, saved views, skeletons, metric/key-value/entity surfaces)
- **`accounting/` (12)** — GL, reconciliation, statements, tax lots, journal entry, trial balance, aging, FX revaluation, allocation, capital commitments
- **`charts/` (19)** — candlestick, equity curve, chart card, sparkline, drawdown, histogram, depth profile, correlation heatmap, X/Y scatter, P&L calendar, yield curve, crosshair tooltip, coverage matrix, timeframe switcher, as-of session clock, categorical bars, waterfall bridge, exposure treemap
- **`shell/` (4)** — topbar, nav rail, status bar, session controls (user menu / role badge / read-only banner)
- **`operations/` (13)** — operator readiness layer: severity badges, readiness panels, the gate-pipeline rail, validation issue lists, evidence links, trust strip, workspace sections, diff view, log tail, event timeline, case queue, SLA chip, provenance chip
- **`trading/` (5)** — order ticket with the live-environment confirm gate, blotter, fills feed, depth ladder, option chain (straddle layout, ITM wash, keyboard strike selection)

All components are **functional-first**: minimal motion, restrained elevation (one subtle `--shadow-card`), small fixed radii — pure operator UX.

## Fonts

The desktop uses native Windows faces — **Segoe UI Variable** (display/body) and **Cascadia Mono** (data) — referenced by name with system fallback stacks; no webfont binaries ship for them. **JetBrains Mono** is loaded from Google Fonts as the cross-platform stand-in for Cascadia so numerics render identically everywhere. For pixel-exact desktop parity, upload the Segoe UI Variable and Cascadia Mono font files and add `@font-face` rules.
