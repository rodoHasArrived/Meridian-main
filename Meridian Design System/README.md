# Meridian Design System — Programmed Institutionalism

<!-- release:generated:begin -->
**Production-ready · 4.3.0 · last stable 4.3.0 · 2026-09-10**

> This is the supported build (**4.3.0**); this tree is the working branch. Identity is declared once in `release.json` and rendered from it by `scripts/release_identity.py` — no version is typed by hand anywhere in this package. QA evidence: `docs/QA_REPORT_4.3.0.md`, which still defers 2 item(s) (NVDA + VoiceOver announcement/verbosity journeys (tests/BROWSER_TEST_PLAN.md — every row marked announced/read); Firefox / WebKit half of the browser matrix (Chromium keyboard + ARIA + responsive run: tests/A11Y_RUN_4.3.0.md)) — that is why this is not a release candidate.

> **Census** (generated from `_ds_manifest.json`): **334 exports across 199 modules** in 8 domains · 130 specimen cards · 28 templates · 680 tokens · 11 themes · 28 export(s) at release-candidate maturity (`api/families.json`).
<!-- release:generated:end -->

An operator-workstation design system for **Meridian** — a self-hosted trading, market-data, research, and reporting platform. Meridian collects equities data from providers (Polygon, IBKR, Databento…), maintains a security master, runs backfills and quality scans, executes backtests/strategy runs, and operates live & paper trading with accounting and report-pack delivery.

This system implements **"Programmed Institutionalism"** — an institutional instrument, not a SaaS dashboard: a paper → plate → well material hierarchy (canvas `#F2F0EB`, panels `#F8F7F3`, data surfaces `#FCFBF8`), cool graphite ink, graphite chrome bars (`#29323A`), ONE terracotta **signal** (`#C56343` — brand and scenario, never a loss, error, or focus color), a separate blue **interaction** family for focus and selection (`#2D6FB3` / `#496C8F`), desaturated independent semantics used as alpha-10 washes, IBM Plex Sans + IBM Plex Mono, and structure built from **fine 1px rules, tonal steps and inset wells — no card shadows, no glow, no gradients**. Clarity emerges from systems, not decoration; every pixel communicates information.

> **Release 3.5.0 (12 Aug 2026)** completes the **strategy formation** redesign as a first-class capability. `strategy` is the seventeenth object; six guarded formation stages (Mandate · Thesis · Universe · Portfolio rules · Risk & governance · Review) checkpoint ONE versioned Strategy, and every run is an immutable **StrategyRunArtifact v1** whose metrics derive once from raw series and trades — freshness against the current draft is always derived from the hash, never stored. The seven Strategy Executor screens share one store (`meridian.strategyExecutor.store.v1`), one URL identity (`strategy`/`run`/`runA`/`runB`), and one nav rail; Basket Builder and Covered-Call Lab adopt the same contracts. Adds the additive accessibility extension across 12 primitives, restores the offline governance/inventory tooling under `scripts/`, and ships `tests/unit.html`. Contracts: `guidelines/ENTITY_SCHEMAS.md` · notes: `docs/RELEASE_NOTES_3.5.0.md`.

> **Release 3.4.0 (12 Aug 2026)** adds the **temporal contract**. Meridian works in twelve kinds of time — trade, settlement, effective, received, posting, valuation, reporting period, fiscal period, close deadline, as-of, evidence timestamp, calculation timestamp — and a figure is only interpretable when the surface states which ones it is stated in. `core/TimeContext` declares them once per screen (`TimeContextProvider`), renders them grouped by whose clock they are (`TimeContextPanel`, `TimeRole`), and runs the coherence rules that catch a temporally impossible record — settlement before trade, a figure calculated before its evidence, a posting outside its reporting period, evidence that arrived after the close deadline. Law: `guidelines/TIME_SEMANTICS.md`. Strictly additive.

> **Release 3.3.1 (11 Aug 2026)** is the integrity-and-adoption patch: `templates/reconciliation-exception/` is recomposed from a stacked documentation page into a production workstation — queue and workflow rail, one active decision step, a persistent evidence/consequence rail, and a sticky bar carrying the single permitted primary action — adopting `WorkspaceContextBar`, `LinkedSelectionProvider`, `ContinuityFrame`, and `DataStateSwitch`, with the standard theme/density/environment/data-state controls and URL-backed workflow state. Documentation counts are regenerated from `_ds_manifest.json` (31 templates · 230 exports · 644 tokens). See `docs/RELEASE_NOTES_3.3.1.md`.

> **Release 3.2.0 (11 Aug 2026)** hardens the six operating-language primitives to the point a production screen can adopt them without working around them: named selection scopes with multi-select and history, SVG-native datums with range bands and label-collision lanes, self-ticking continuity with retained-content lock and retry, a self-measuring workspace context bar, and small-multiple/print figure variants under `AnalyticalFigureGrid`. Strictly additive — two new exports, no changed defaults. The normative usage law is `guidelines/OPERATING_LANGUAGE.md`; screen adoption across the 31 templates is release 3.3. See `docs/RELEASE_NOTES_3.2.0.md`.

> **Release 3.1.0 (11 Aug 2026)** adds the coordinated operating layer: persistent workspace context, linked selection, last-known-good continuity, complete analytical figures, accounting proof, close/posting consequence, staged change control, attestation, and visible evidence coverage. The release adds 25 modules and 34 public exports without removing an existing API. Its source-driven gate passes 36 tests plus 13 browser states and 14 interaction/responsive checks. See `docs/RELEASE_NOTES_3.1.0.md`, `docs/COORDINATED_COMPONENTS.md`, and `docs/QA_REPORT_3.1.0.md`.

> **Foundations v2.1 (Aug 2026)** retoned every token to the Foundations & Primitives sheet: paper/plate/well surfaces, cool graphite ink, blue focus and selection separated from the terracotta signal, the v2.1 type ramp (editorial 48 · workspace 28 · page 22 · section 16 · body 14), a 4px spacing base, dense/standard/review density (28 · 36 · 44), and the visualization layer's series roles and data conditions. Token **names** and component APIs are unchanged — only values. Earlier history lives in `docs/changelog/`.

## Repository map

```
styles.css        global CSS entry — @imports tokens/ (order is load-bearing)
tokens/           L0 — color, type, spacing, elevation, theme, dark, print
components/       core · data · charts · accounting · trading · operations · decision · shell
templates/        27 copy-and-edit screens — 7 archetypes (one per group: Start / Monitor / Investigate / Compose / Report / Admin) + Meridian's 20 production screens
guidelines/       design law — visual, content, patterns, brand, tokens, a11y
docs/             developer docs — architecture, getting started, cookbook, upgrading
assets/           brand marks + the 24px line-icon module set
scripts/          restored offline tooling — governance, inventory, catalog, scaffold, release gate
tests/            the conformance gate (conformance.html) · action contract + mock transport ·
                  semantic-safety negative paths (semantic-safety.html) ·
                  offline unit suite (unit.html) + deferred browser/NVDA test plan
_ds_*.{js,json}   GENERATED by the compiler — never hand-edit
```

**The gate (4.2):** `tests/conformance.html` runs the design law — sixteen static rules over every component, token file and template entry, assertions over all 125 specimen cards, and parity checks that fail when a typed count stops matching `_ds_manifest.json`. It is also the first thing in this repository to run the regex-grade half of the compiler's own `_adherence.oxlintrc.json`. Rules and their fixtures: `tests/conformance-rules.js` · law: `guidelines/CONFORMANCE.md` · scoreboard: the **Conformance gate** card in the Documentation group.

**Tooling restored (3.5):** `scripts/` carries the documented governance/inventory/catalog/scaffold/release tooling as self-contained offline stdlib scripts, and `tests/` carries the offline unit suite plus the deferred browser/NVDA plan (`tests/BROWSER_TEST_PLAN.md`). The upstream captured QA run (`qa/`) stays in the monorepo; `docs/QA_REPORT_3.5.0.md` records what THIS release verified and what it defers.

**Where do I…** add a component / token / template → `CONTRIBUTING.md` · understand the layers & compile model → `docs/ARCHITECTURE.md` · pick the right component → `guidelines/PATTERNS.md` · look up a token → `guidelines/TOKEN_REFERENCE.md` · extend without forking → `guidelines/EXTENDING.md` · check my copy reads right → `guidelines/CONTENT_FUNDAMENTALS.md` · get a consumer running → `docs/GETTING_STARTED.md`. Every folder has a README locating its contents.

## Index — root manifest

| File / folder | What it is |
| --- | --- |
| `readme.md` | This file — the design guide, brand context, and manifest. Start here. |
| `SKILL.md` | Agent-skill front matter, so this folder works as a downloadable Claude Code skill. |
| `styles.css` | The one stylesheet a consumer links. `@import` lines only. |
| `thumbnail.html` | The project tile (brand mark on terracotta + semantic swatch strip). |
| `index.html` | Package landing page. |
| `CONTRIBUTING.md` | Checklists for adding a component, token, or template; release protocol. |
| `CHANGELOG.md` | Semver history through 3.4.0. |
| `github.md` | Source-repo association and the port receipt. |
| `tokens/` | 10 CSS files — fonts, colors, typography, elevation, base, theme, dark, visualization, contrast modes, print. 665 tokens. |
| `components/` | 8 domains — `core` `data` `charts` `accounting` `trading` `operations` `decision` `shell`. Each component is `<Name>.jsx` + `<Name>.d.ts` + `<Name>.prompt.md`, with one `@dsCard` gallery per directory. |
| `templates/` | 27 copy-and-edit screens (`<Slug>.dc.html` + `ds-base.js`) — 7 archetype starting points plus Meridian's 20 production screens. Copy one to begin. Indexes: `templates/README.md`, `templates/PRODUCTION_SCREENS.md`. |
| `guidelines/` | Design law — visual, content, patterns, visualization grammar, brand, iconography, tokens, a11y, entity schemas, extending, states — plus 24 specimen cards. |
| `docs/` | Developer docs — architecture, getting started, cookbook, coordinated components, performance, upgrading, blueprint, release notes, QA report, contract audit. |
| `assets/` | `brand/` (marks, wordmarks, lockups, hero, tiles, identity sheet) and `icons/` (48 24px line-icon modules). |
| `tests/` | The conformance gate (`conformance.html` + `conformance-rules.js`), the action contract (`action-contract.html` + `mock-transport.js`), the semantic-safety negative paths (`semantic-safety.html`), the offline unit suite, and the deferred browser/NVDA plan. |
| `support.js` | Design-component runtime. Generated — never hand-edit. |
| `_ds_bundle.js` · `_ds_manifest.json` · `_adherence.oxlintrc.json` | Compiler output. Generated — never hand-edit. |

## Sources

- **GitHub:** [rodoHasArrived/Meridian-main](https://github.com/rodoHasArrived/Meridian-main) — the full platform monorepo. The **canonical visual source is the desktop app**: `src/Meridian.Wpf/Styles/` — `ThemeTokens.xaml` (colors), `ThemeTypography.xaml` (type), `ThemeSurfaces.xaml` (cards/badges/metrics), `ThemeControls.xaml` (buttons/inputs/nav). The React web workstation under `src/Meridian.Ui/dashboard/` shares this same light **Programmed Institutionalism** palette — its `src/styles/index.css` is the canonical token source this system is now aligned to. (A separate *generated* HTML dashboard uses a darker console theme; not the reference.)
- **Local codebase mount (this port):** `Meridian_Design_System_3.1.0/` — the packaged v3.1.0 design system, read directly and ported file-for-file into this project. Values, component source, cards, guidelines, and assets are the upstream ones, unmodified; the only rewrite was the bundle namespace (`window.MeridianDesignSystem_205d17` here). Do not assume the reader has this mount — it was an attached local folder, not a URL.
- **Monorepo mount (upstream):** `src/` (WPF desktop under `src/Meridian.Wpf/`, .NET host under `src/Meridian/`). Not attached to this project; referenced for provenance only.

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
| **Strategy Executor** | One versioned Strategy, one immutable run artifact — six-stage formation, shared worksheet, backtest, run ledger, compare, governance with proof-bound attestations | `templates/strategy-formation/` (+3, shared store in `templates/_strategy-executor/`) |
| **Trading Desk** | Live trading — watchlist, depth ladder, order ticket (live confirm gate), blotter & fills | `templates/trading-desk/` |
| **Ingestion** | Data-pipeline ops — provider freshness, backfill queue, coverage heat, run log | `templates/ingestion-operations/` |
| **Report Scheduler** | Scheduled report delivery — packs, recipients, delivery-history timeline | `templates/report-scheduler/` |
| **Reconciliation** | Statement reconciliation — break queue and custodian-vs-ledger split-pane matcher in one template | `templates/reconciliation-workstation/` |
| **Reconciliation Exception** | The Release 2–3 decision chain end to end — variance, tie-out, evidence, trace, resolution, change set, posting impact, version conflict, attestation | `templates/reconciliation-exception/` |
| **Close Management** | Close governance on one selection — calendar, dependency graph and owner views, blocking control, coverage, resolution, close attestation | `templates/close-management/` |
| **Asset-Liability Review** | The Release 4 visualization chain — cash-flow ladder, maturity ladder, duration contribution, basis comparison, calculation trace | `templates/alm-review/` |
| **Ownership Graph** | General-purpose ownership & control investigation — economic vs voting lens, look-through to ultimate owners, attribution proof | `templates/ownership-graph/` |
| **Covered-Call Lab** | Options overwrite research — request params, result metrics, chain preview, run library | `templates/covered-call-lab/` |
| **Session Start** | Sign-in — credentials, MFA, environment gate before any surface loads | `templates/session-start/` |
| **Asset Sheet** | Branded presentation sheet — page rail with keyboard paging, masthead, overview / live components / specifications, corporate footer | `templates/asset-sheet/` |
| **Report Pack** | Printable LP monthly pack — the delivery artifact of the reporting pipeline | `templates/report-pack/` |

## Visual foundations

Full guide: `guidelines/VISUAL_FOUNDATIONS.md`. Tokens live in `tokens/` (map: `tokens/README.md`).

- **Color — material hierarchy, one signal, one interaction family.** Paper (canvas) `#F2F0EB` → recessed rail/command band `#EBE8E1` → plate (panel) `#F8F7F3` → well (data surfaces, raised cells) `#FCFBF8`; chrome bars graphite `#29323A`. Data surfaces read brightest; insets recede toward the canvas. One signal: terracotta `#C56343` (active identity marker, crosshair, scenario series, editorial field — **one purposeful occurrence per composition**), controlled variant `#CB6E4E`. Graphite `#29323A` is the primary-action fill. **Focus and selection are blue** — focus `#2D6FB3`, selection `#496C8F`, selected surface `#E7EDF2` — so system feedback never impersonates the brand. Semantic accents are desaturated **trios** (dim text · solid border · alpha-10 wash, never solid fills): green `#2E7655`, red `#B54B42`, amber `#986717`, information `#2F648A`, pending `#7A6B52`. Text ladder: primary `#20252B` · secondary `#535C65` · muted `#606870` · disabled `#A4ABB2`.
- **Analytical grammar — evidence, not decoration.** Chart color is assigned by **series role**, never by chart instance: actual (graphite) · prior (slate) · benchmark (neutral dashed) · scenario (terracotta dashed — never a loss color) · target (dotted datum) · estimated (dashed + `Est.`). Every value carries one of **ten data conditions** (confirmed, provisional, estimated, stale, overridden, missing, not applicable, pending, exception, reconciled) as text **plus** marker; a blank cell never stands for a condition. Model uncertainty (`IntervalBar`, trace bands) and observed-data condition (`DataCondition`) never share a treatment. Full law: `guidelines/VISUALIZATION_GRAMMAR.md`; tokens: `tokens/visualization.css`.
- **Environment modes** are always visible: **Live** red `#B54B42` (real money), **Paper** field slate `#5D7286` (simulated), **Fixture** amber `#986717` (replay).
- **Readiness & severity** are a first-class semantic layer (`--severity-*`, `--state-*`): every gate, lane, and check resolves to **Ready · Review · Action · Blocked · Info**, encoded by the `operations/` components. The app's own `--ws-*` token names are aliased too, so dashboard CSS resolves verbatim.
- **Dark mode** is cool graphite-slate, equally flat — canvas `#11171D`, plate `#171E25`, rail `#1D252D`, terracotta signal `#D88466` and blue focus `#5C9FE5` for night legibility. Activates via `prefers-color-scheme` or `data-theme="dark"`; force light under OS dark with `data-theme="light"`.
- **Type:** IBM Plex Sans (titles, 600; body 14px), IBM Plex Mono (all data, tabular — prices never jump). Ramp (px): editorial 48 (report covers only) · workspace 28 · page title 22 · data-medium 18 · section 16 · card title 14 · body 14 (dense 13 / review 15) · metric 28 (mono 700) · data value 16 · label 11 · micro 10 (small-caps, muted).
- **Spacing & density:** 4px base — 64 report · 48 macro · 32 workspace · 24 section · 16 generous · 12 standard · 8 tight · 4 micro. Chrome: 48px brand bar, 28px status bar, 224px (14rem) nav rail. Density scopes via `DensityToggle` map to the v2.1 modes: **`terminal`** 26px rows (multi-monitor ops walls) · `compact` = **dense** 28 · default = **standard** 36 · `spacious` = **review** 44, each with its own control height, body, and label size.
- **Radii:** 2 / 4 / 6 / 8 — **2px** chips, badges, plots and plates · **4px** buttons/inputs/panels · **6px** overlays · **8px** dialogs (the ceiling). Metric cards carry a **3px left-accent border**. Tight corners — never friendly radii.
- **Borders & shadows:** structure is **visible, load-bearing borders** (`#D2CDC3`; hover `#BEB8AD`; heavy header/total rules `#AAA49A`). Surfaces are **flat — no card shadow**; the only shadow is a tight hard-edged `0 2px 6px /.18` on floating menus/popovers. **No gradients, no glow, no soft cloud shadows.**
- **Motion:** animations explain, nothing celebrates — 80/120/180/240/280ms on `cubic-bezier(.2,0,0,1)`, no bounce. Four verbs only: reflow, trace, reconcile, focus. Rows reorganize on filter change; live numbers update without animation.

## Content fundamentals

Full guide: `guidelines/CONTENT_FUNDAMENTALS.md`.

- **Voice:** terse, declarative, evidence-first. "Backfill complete · 412,008 bars · 0 gaps". No exclamation points, no marketing adjectives, no emoji.
- **Casing:** sentence case for UI copy and buttons. Small-caps (not ALL-CAPS) for eyebrow labels, table headers, and badges.
- **Numbers:** mono (IBM Plex Mono), tabular, fixed decimals (prices 4dp), explicit signs on deltas, UTC timestamps as `2026-06-09 20:00:00Z`. Counts use thin separators: `412,008`. Every number becomes text through **`core/format.js`** — locale and currency from the surface (`FormatProvider`), decimals from the instrument; a bare `Intl` constructor anywhere else is a governance violation (`guidelines/NUMERIC_AUTHORITY.md`, gate rule C-01).
- **Errors/status:** name the system, the time, the evidence — "Provider offline · Polygon last seen 14:02:11Z". Never apologize, never anthropomorphize.

## Component inventory

<!-- inventory:generated:begin -->
**199 modules across 8 domains → 334 exported symbols** on `window.MeridianDesignSystem_205d17` — this census is generated from `_ds_manifest.json` by `scripts/generate_inventory.py` (run with `--write` after adding a component; CI fails on drift). Per-component one-liners live in each `<Name>.prompt.md`, when-to-reach-for guidance in `guidelines/PATTERNS.md`, the domain map in `components/README.md`.

### `core/` — 69 modules · 126 exports

`Accordion` · `AsyncCombobox` · `Badge` · `Breadcrumb` · `Button` · `Callout` · `Capsule` → `Capsule` + `CapsuleBody` + `CapsuleFooter` + `CapsuleHeader` · `Checkbox` → `Checkbox` + `Toggle` · `Combobox` · `CommandPalette` · `ContextMenu` · `ContinuityFrame` → `ContinuityFrame` + `ContinuityStatus` · `DatePicker` · `DateRangePicker` · `Datum` → `Datum` + `DatumGroup` · `Delta` · `DensityToggle` · `Dialog` → `Dialog` + `DialogBody` + `DialogFooter` + `DialogForm` + `DialogHeader` · `Disclosure` → `DETAIL_LEVELS` + `DetailDial` + `DetailOnly` + `Disclosure` + `DisclosureHooks` + `DisclosureProvider` + `DisclosureRegion` + `DisclosureResidue` · `Drawer` → `Drawer` + `DrawerBody` + `DrawerHeader` · `ErrorBoundary` · `Eyebrow` · `FileUpload` · `Flex` · `Form` → `Form` + `FormDivider` + `FormGrid` + `FormRow` + `FormSectionLabel` · `FormField` · `FormValidation` → `FieldInput` + `FormErrorSummary` + `FormValidation` · `FormatProvider` → `Format` + `FormatHooks` + `FormatProvider` + `NEGATIVE_STYLES` · `FreshnessIndicator` → `ConnectionDot` + `FreshnessIndicator` · `Grid` · `HotkeysProvider` · `Input` · `InstrumentChip` · `Kbd` · `LocalDate` → `LocalDate` + `LocalDateText` · `MaterialityAssessment` → `MaterialityAssessment` + `MaterialityBadge` · `MultiSelect` · `NotificationCenter` · `NumberInput` · `ObjectModel` → `OBJECT_TYPES` + `ObjectHeader` + `ObjectHooks` + `ObjectModel` · `ObjectScope` → `ANALYTICAL_SCOPES` + `ObjectScope` + `ObjectScopeProvider` + `ScopeHooks` + `ScopeMark` + `ScopeMismatch` + `ScopedValue` · `ObjectTrail` → `ObjectTrail` + `ObjectTrailList` + `ObjectTrailProvider` + `TrailHooks` · `PanelSurface` · `Popover` · `ProgressBar` · `RadioGroup` · `Registry` → `DENSITY` + `Registry` + `RegistryTable` + `SEVERITY` · `SegmentedControl` · `Select` · `Slider` · `Spinner` · `SplitPane` · `Stack` · `StatusBanner` · `Stepper` · `StrategyModel` → `STRATEGY_FORMATION_STEPS` + `STRATEGY_LIFECYCLE` + `StrategyModel` · `Tabs` → `TabPanel` + `Tabs` · `TagInput` · `TextArea` · `TimeContext` → `TIME_ROLES` + `TimeContext` + `TimeContextPanel` + `TimeContextProvider` + `TimeHooks` + `TimeRole` · `Timestamp` · `Toast` → `Toast` + `ToastProvider` · `Tooltip` · `TreeView` · `Validators` · `ValueAuthority` → `AuthorityChip` + `AuthorityPanel` + `AuthorityStack` + `ValueAuthority` · `format` → `GLYPHS` · `localDate` → `DOW` · `useFormState` → `FormHooks`

### `data/` — 22 modules · 39 exports

`BulkActionBar` → `BulkActionBar` + `BulkSelectCheckbox` · `ColumnChooser` · `ColumnManager` · `DataGrid` → `CELL_FORMATS` + `DataGrid` + `GRID_SCHEMAS` · `DataStateSwitch` · `EditableCell` · `EmptyState` · `EntitySummary` · `FilterBuilder` · `KeyValueGrid` · `LinkedSelectionProvider` → `LinkedSelectionProvider` + `LinkedSelectionTarget` + `SelectionHooks` · `MetricCard` · `OptimisticCell` → `OptimisticCell` + `OptimisticHooks` + `OptimisticProvider` + `OptimisticRowBar` · `Pagination` · `SavedViews` · `SelectionCompare` → `CompareSheet` + `CompareToggle` + `CompareTray` + `SelectionCompare` + `SelectionCompareHooks` + `SelectionCompareProvider` · `SelectionToolbar` · `Skeleton` → `Skeleton` + `SkeletonTable` · `TableHooks` · `Toolbar` → `Toolbar` + `ToolbarDivider` + `ToolbarGroup` + `ToolbarSpacer` · `VirtualizedList` · `WorksheetGrid`

### `charts/` — 32 modules · 43 exports

`AnalyticalField` · `AnalyticalFigure` → `AnalyticalFigure` + `AnalyticalFigureGrid` · `BarChart` · `CandleChart` · `CashFlowLadder` · `ChartAnnotations` · `ChartCard` · `ChartFrame` → `AXIS_FONT` + `AxisChip` + `ChartFrame` · `ChartTooltip` · `ComparisonPlate` → `ComparisonPlate` + `PlateGrid` · `ControlDependencyGraph` · `CorrelationHeatmap` · `CoverageMatrix` · `DepthChart` · `DotMatrix` · `DrawdownChart` · `DurationContribution` · `EquityCurve` · `Figure` → `FIGURE_ROLES` + `Figure` + `FigureSet` + `GEOMETRIES` · `Histogram` · `IntervalBar` · `LineageGraph` · `MaturityLadder` → `MATURITY_BUCKETS` + `MaturityLadder` · `OwnershipGraph` · `PnLCalendar` · `ScatterChart` · `SmallMultiples` · `Sparkline` · `TimeframeSwitcher` → `AsOfControl` + `TimeframeSwitcher` · `TraceChart` · `Treemap` · `WaterfallChart` · `YieldCurve` · `useChartCrosshair` → `ChartCursor` + `ChartCursorSync` · `viz` → `Fmt` + `ROLES`

### `accounting/` — 27 modules · 32 exports

`AccountTree` · `AccountingPeriodSelector` · `AgingTable` · `AllocationEditor` · `AmountCell` · `Assurance` → `ASSURANCE_STAGES` + `Assurance` + `AssuranceChip` + `CertificationMatrix` · `BasisBridge` · `BasisComparisonMatrix` · `CalculationTrace` · `CloseCalendar` · `CommitmentBar` · `FxRevaluationTable` · `IncomeSchedule` · `JournalEntryForm` · `LedgerTable` · `LifecycleRibbon` → `LifecycleLanes` + `LifecycleRibbon` · `Money` · `PostingImpactPreview` · `ReconciliationPanel` · `RollforwardMatrix` · `RollforwardTrace` · `ScheduleLadder` → `ScheduleLadder` + `ScheduleLadderSet` · `StatementTable` · `TaxLotTable` · `TieOutPanel` · `TrialBalance` · `VarianceCell`

### `trading/` — 6 modules · 6 exports

`Blotter` · `DepthLadder` · `FillsFeed` · `OptionChainTable` · `OrderTicket` · `TimeAndSalesTape`

### `operations/` — 33 modules · 68 exports

`ActionEnvelope` → `ACTION_STATES` + `ActionEnvelope` + `ActionEnvelopeProvider` + `ActionHooks` + `ActionStateChip` + `InstructionLedger` · `ActionGuard` → `ActionAvailability` + `ActionGuard` + `ActionGuardList` · `ActionQueue` → `ActionQueue` + `ActionQueueChip` + `QUEUE_RUNGS` · `AdapterDiagnostics` → `ADAPTER_POSTURES` + `AdapterDiagnostics` + `AdapterHooks` + `AdapterPostureChip` · `AnnotationLayer` → `AnnotationAnchor` + `AnnotationHooks` + `AnnotationLayer` + `AnnotationPanel` + `AnnotationProvider` · `AttestationPanel` · `CaseQueue` · `ChangeSetReview` → `ChangeReviewBar` + `ChangeSetDiff` + `ChangeSetHooks` + `ChangeSetProvider` + `ChangeSetReview` · `ClaimHeader` · `ConnectionState` · `DataCondition` → `DataCondition` + `DataConditionLegend` + `DataConditionValue` · `DecisionFlow` → `DecisionConsequenceBar` + `DecisionFlow` + `DecisionFlowProvider` + `DecisionFlowRail` + `DecisionFlowStage` · `DiffView` · `EventTimeline` · `EvidenceInspector` → `EvidenceInspector` + `EvidenceSection` + `ProvenanceTrace` · `EvidenceLink` · `EvidencePreview` · `GateRail` · `HandoverBrief` → `HandoverBrief` + `HandoverBriefChip` · `LogTail` · `ProvenanceChip` · `ReadinessPanel` · `ResolutionDrawer` · `SeverityBadge` · `SlaChip` · `SourceCoverageSummary` · `Status` · `StrategyRunModel` → `STRATEGY_RUN_AXES` + `StrategyRunModel` · `TrustStrip` · `ValidationIssueList` · `ValueEnvelope` → `AUTHORITIES` + `ENVELOPE_MARKS` + `EnvelopeCoherence` + `EnvelopeMark` + `EnvelopePanel` + `ValueEnvelope` · `VersionConflictNotice` · `WorkspaceSection`

### `shell/` — 10 modules · 21 exports

`NavRail` · `ScreenComposition` → `ARCHETYPES` + `ScreenComposition` · `SessionControls` → `ReadOnlyBanner` + `RoleBadge` + `SessionControls` + `UserMenu` · `StatusBar` · `WorkspaceContextBar` → `WorkspaceContextBar` + `WorkspaceContextProvider` + `WorkspaceHooks` · `WorkspaceLayout` → `LayoutHooks` + `LayoutRegion` + `WorkspaceLayout` + `WorkspaceLayoutBar` + `WorkspaceLayoutGrid` + `WorkspaceLayoutProvider` · `WorkstationShell` · `WorkstationTopbar` · `GlobalSearch` · `ReturnPath`

### `decision/` — 3 modules · 7 exports

`ConsequenceRail` → `CONSEQUENCE_KINDS` + `ConsequenceRail` · `DecisionSnapshot` → `DecisionSnapshot` + `DecisionSnapshotPanel` + `SnapshotHooks` · `EvidenceRail` → `EVIDENCE_SECTIONS` + `EvidenceRail`
<!-- inventory:generated:end -->

> **Lowercase = bundle-internal by design.** Standalone hooks/utilities (`useToast`, `useContextMenu`, `useOverlayFocus`, `money.js` functions) aren't on the namespace directly — reach them through their carriers (`Money`, `TableHooks`, `FormHooks`, `ChartCursor`) or owning components.

## Iconography

Full guide: `guidelines/ICONOGRAPHY.md`. Meridian ships its own **line-icon module set** in `assets/icons/`, drawn on a 24px grid with 1.5px strokes and `stroke="currentColor"`. Use for nav and module identity at 16–20px. No icon font, no emoji ever. Unicode glyphs appear only as functional marks inside data (`⌕` search, `Ctrl K` kbd, `Δ`, `·` separators, `↑↓` sort).

Brand marks in `assets/brand/`: `meridian-mark.svg`, `meridian-mark-light.svg`, `meridian-mark-monochrome.svg`, `meridian-wordmark.svg`, `meridian-tile-256.png`, `meridian-hero.svg`.

Composition law for a screen: `docs/WORKSTATION_BLUEPRINT.md`.

## Documentation & guides

Design law lives in `guidelines/`, developer docs in `docs/` — each folder README indexes its contents; cards for each appear in the **Documentation** group of the Design System tab. The tab is organized by domain: **Brand · Colors · Type · Spacing · Depth & Motion · Theming** (foundations), **Core · Data · Charts · Accounting · Trading · Operations · Decision · Shell** (component galleries, mirroring `components/`), and **Documentation**.

- **`docs/ARCHITECTURE.md`** — compile model, layer/dependency rules, component anatomy, quality gates.
- **`docs/COORDINATED_COMPONENTS.md`** — state ownership and reference compositions for the 3.1 operating layer.
- **`docs/RELEASE_NOTES_3.3.1.md`** · **`docs/RELEASE_NOTES_3.2.0.md`** · **`docs/RELEASE_NOTES_3.1.0.md`** · **`docs/QA_REPORT_3.3.1.md`** · **`docs/QA_REPORT_3.1.0.md`** — shipped surface and validation evidence.
- **`CONTRIBUTING.md`** — checklists: add a component / token / template; release protocol.
- **`guidelines/VISUAL_FOUNDATIONS.md`** — the "why" behind Programmed Institutionalism.
- **`guidelines/CONTENT_FUNDAMENTALS.md`** — voice, number formatting, evidence-first copy.
- **`guidelines/PATTERNS.md`** — when to reach for each component; composition and decision trees; keyboard map.
- **`guidelines/OPERATING_LANGUAGE.md`** — **normative.** The six operating-language primitives: the operator question each answers, its rule of use, its test, the anti-pattern it retires, the restraint clause, the adoption matrix by archetype, and the seven-point screen checklist.
- **`guidelines/CONFORMANCE.md`** — **normative.** The fifteen machine-checked rules, what each catches and which shipped defect it comes from, the card assertions, what the gate does not claim, and how to add a rule.
- **`guidelines/NUMERIC_AUTHORITY.md`** — **normative.** `N-1…N-8`: one `Intl` constructor, locale and currency from the surface, decimals from the instrument, U+2212 negatives, and where compact notation may not appear.
- **`guidelines/ACTION_SEMANTICS.md`** — **normative.** `A-1…A-9`: the four fates of an instruction, why "failed" is not one of them, idempotency as identity, and what a retry after *unlanded* must do.
- **`guidelines/TIME_SEMANTICS.md`** — **normative.** The twelve time roles, the three clocks they belong to, which roles each surface owes, the coherence rules, and the anti-patterns a single `date` prop creates.
- **`guidelines/VISUALIZATION_GRAMMAR.md`** — the analytical law: job model, reading path, series-role ledger, comparison plates, uncertainty vs. data condition, live/degraded behavior, graphs, responsive and accessibility contracts, and the 22-question QA gate.
- **`guidelines/BRAND_GUIDELINES.md`** — marks, wordmark, palette usage, do/don't.
- **`guidelines/ICONOGRAPHY.md`** — the 24px line-icon set and usage.
- **`guidelines/TOKEN_REFERENCE.md`** — the 3-tier token model, white-label entry points, which-token-when.
- **Authorities on the namespace** — `Format` (every number), `LocalDate` (every calendar date; `LocalDateText` renders one), `Assurance` (certification carried per balance: `AssuranceChip`, `CertificationMatrix`, `ASSURANCE_STAGES`).
- **`guidelines/ACCESSIBILITY.md`** — WCAG 2.2 AA with **measured** contrast, keyboard/focus/ARIA matrix.
- **`guidelines/ENTITY_SCHEMAS.md`** — shared wire shapes (Order, Fill, Alert, Run, Instrument) — map once, drop in everywhere.
- **`guidelines/EXTENDING.md`** — extension recipes, stable-vs-internal API surface, robustness gates.
- **`guidelines/STATES_AND_ANTIPATTERNS.md`** — the five data states every surface owes; do→don't.
- **`docs/GETTING_STARTED.md`** · **`docs/COOKBOOK.md`** · **`docs/PERFORMANCE.md`** · **`docs/UPGRADING.md`** · **`CHANGELOG.md`** (semver history) · **`docs/WORKSTATION_BLUEPRINT.md`** (how a workstation screen is composed).

## Compilation & consumption

**Components compile to `_ds_bundle.js`** — consume via `const { Button } = window.MeridianDesignSystem_205d17` after loading the bundle. Each component injects its own scoped CSS keyed to the design tokens, so a consuming page only needs to link `styles.css` and the bundle.

**Archetype templates in `templates/<slug>/`** are the starting point for consuming projects (anatomy: `templates/README.md`; Meridian's own 20 production screens are indexed in `templates/PRODUCTION_SCREENS.md`):

1. Copy the folder to your project
2. Open `<Slug>.dc.html` (a Design Component file — directly editable in the browser)
3. Edit the markup, props, or logic
4. Adjust the `base` path in the sibling `ds-base.js` if the template sits at a different depth

**Start here →** copy **`templates/dashboard-workstation/`**: a complete operator overview that exercises the most components in one screen, so it doubles as a working reference. Each archetype is the reference composition for its group — `analytical-workbench` for analysis surfaces, `basket-builder` for compose flows, `report-pack` for print deliverables, `settings-admin` for forms-heavy admin, `session-start` for the entry gate. Need a bare shell — masthead, nav rail, status bar, empty canvas? Copy `templates/blank-workstation/`: one `WorkstationShell`, nothing else. Building a screen Meridian itself already has? Copy the closest production screen from `templates/` instead.

**The Meridian production-screen library — 20 of the 27 templates.** The platform's own screens — accounting, trading desk, reconciliation, security master, ingestion ops, ownership graph, and the seven-screen cross-linked Strategy Executor suite — consume this system exactly as any consumer does: compiled bundle + `styles.css` only, no privileged access. Upstream they were staged in a separate `meridian-screens/` tier pending extraction into their own project; **in this project they sit in `templates/` beside the archetypes**, because that is the only tree the template picker indexes — and upstream's own guidance was already "building a screen Meridian itself already has? copy the closest production screen." Index, cross-link map, and adoption guide: `templates/PRODUCTION_SCREENS.md`.

**Archetype template index (7)** — the reference composition for each group. The other 20 templates are the production screens above, indexed in `templates/PRODUCTION_SCREENS.md`:

| Template | Group | One-liner |
| --- | --- | --- |
| `blank-workstation` | Start | Bare shell — masthead, nav rail, status bar, empty canvas |
| `session-start` | Start | Sign-in — credentials, MFA verify, environment gate (typed LIVE confirm), role |
| `dashboard-workstation` | Monitor | Portfolio overview — metrics, equity curve, positions drill-in (**start here**) |
| `analytical-workbench` | Investigate | Claim-led rates workbench — trace chart, evidence inspector, comparison plates, exact-value table, lineage graph (**the visualization grammar, end to end**) |
| `basket-builder` | Compose | Bond basket construction — constituents, analytics, concentration risk |
| `report-pack` | Report | Printable monthly report pack — Letter sheets over the design-system print layer |
| `settings-admin` | Admin | Forms-heavy admin — validation stack, recipients, danger zone with typed confirm |

## Core patterns & rules

**Data-rich UI over whitespace.** Tight rows, hairlines, compact metadata labels, tabular numbers. Limit each operational region to one graphite primary action; terracotta remains brand/scenario signal and other actions stay ghost/link. Never invent colors — use tokens; tint only via the semantic alpha-10/alpha-20 overlays.

**Accounting & money flows.** All currency goes through `AmountCell` — mono tabular, fixed decimals, accounting parentheses for negatives, zero-as-dash. Tables prove their own arithmetic: `LedgerTable` flags imbalance, `ReconciliationPanel` flags "Out by …", `JournalEntryForm` gates Post on balance, `StatementTable` double-rules totals, `AccountTree` rolls child balances, `TaxLotTable` classifies long/short and computes basis/unrealized, `TrialBalance` proves Σdebit = Σcredit, `AgingTable` escalates late buckets, `FxRevaluationTable` nets unrealized G/L, `AllocationEditor` splits totals cent-exactly (via the same `Money` arithmetic consumers can call).

**The fate of an instruction.** Anything that mutates something outside the screen — an order, a backfill, a posting, a delivery — goes through `ActionEnvelope`: it declares its endpoint, idempotency key, permission, posture (confirmed or optimistic) and failure surface *before* the attempt, and renders one of four fates — in flight · landed · **unlanded** · rejected. There is no "failed": an adapter that does not answer leaves the fate unknown, so the retry carries the same key rather than sending a second instruction. `ActionGuard` answers *may I*; this answers *did it*. Law: `guidelines/ACTION_SEMANTICS.md`; reference adoption: `templates/trading-desk/`.

**Structured editing.** Most tables support inline editing (click a cell → edit → blur to save). Bulk operations are batch-select-then-act. Prefer expandable detail rows with `ExpandableDataTable` over modals. Context menus appear on right-click for row-specific actions.

**Environment visibility.** Every workstation displays a Live/Paper/Fixture badge so operators never mistake simulation for production.

**Print & report packs.** `tokens/print.css` makes any workstation print as a document (chrome hidden, scroll regions unrolled, headers repeat) and ships the opt-in `.mds-sheet` page model for composed deliverables. `templates/report-pack/` is the reference; see `guidelines/PATTERNS.md › Print & report packs`.

## Architecture

The layer model, dependency rules, component anatomy, and quality gates are specified in **`docs/ARCHITECTURE.md`**. The short version:

```
 tokens/ (L0) → core/ (L1) → data/ charts/ (L2) → accounting/ trading/ operations/ decision/ (L3)
             → shell/ (L4) → templates/ (L5 — 7 archetypes + 19 production screens,
                              which consume the system as any consumer does)
```

Imports point **down** the layers, never up or sideways. All components are functional-first: minimal motion, flat surfaces, small fixed radii — pure operator UX. In this project, conformance is checked by the design-system compiler (component/`.d.ts` pairing, card HTML, token reachability, font resolution) **and by `tests/conformance.html`**, which runs the thirteen design-law rules, the card assertions, and the regex-grade half of the generated adherence ruleset — the part of the ruleset nothing ran before 4.2. Upstream, the same rules are enforced by `scripts/check_design_system_governance.py` and the `tests/` suites.

## Fonts

The desktop uses native Windows faces — **IBM Plex Sans** (display/body) and **IBM Plex Mono** (data), loaded by `tokens/fonts.css`. **Both families are self-hosted**: TTF binaries (400 / 400 italic / 500 / 600 / 700 each, OFL licenses alongside) ship in `assets/fonts/`, with `local()` short-circuit where an installed copy exists — pixel-exact desktop parity with zero egress; no Google Fonts dependency remains.
