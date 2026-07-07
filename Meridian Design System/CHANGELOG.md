# Changelog

Semver from here forward: **MAJOR** = breaking component API or token removal ·
**MINOR** = new components/tokens/templates, behavior changes with compatible APIs ·
**PATCH** = fixes with no API surface change. Pre-1.9 history lives in
`docs/changelog/` (process reports, unversioned).

## 1.18.0 — 2026-07-05

### Added
- **Charts:** `WaterfallChart` — contribution bridge (start/delta/total bars, dashed level
  connectors, computed Net) for P&L attribution and NAV walks. `Treemap` — squarified
  exposure/allocation map (alpha washes by day-delta or group, measured layout, progressive
  labels). `BarChart` — the missing categorical-bars primitive (vertical SVG or ranked
  horizontal rows, `signed` tinting). Card: *Attribution & exposure*.
- **Data:** `SavedViews` — named, persisted view snapshots (query + sort stack + filters)
  with locked presets, save-as, an "· edited" divergence indicator, update and delete;
  persists per `storageKey`. `useTableState` gains `getViewState()` / `applyViewState(state)`
  / `setFilters` (additive). Card: *Saved views*.
- **Templates:** `session-start` — the missing lifecycle surface: credentials → MFA verify →
  environment gate (Live requires typed confirm) → role, with readiness footer and status
  bar. `backtest-compare` — two runs side by side: KPI delta table, overlaid equity curves,
  paired drawdown/returns panes, configuration `DiffView` + provenance rail; joins the
  Strategy Executor suite. `report-pack` — printable LP monthly pack (cover, performance
  summary with live `EquityCurve`, income statement, positions) whose figures tie out.
- **Print layer:** `tokens/print.css` gains the opt-in `.mds-sheet` page model — Letter page
  cards on screen, real `@page` pagination in print — plus `.mds-no-print`,
  `.mds-avoid-break`, `.mds-sheet__footer`. Existing defensive workstation print rules
  unchanged. Documented in `PATTERNS.md › Print & report packs`.

## 1.17.0 — 2026-07-05

### Changed
- **`accounting-workstation` rebuilt — modular and fleshed out.** The single 470-line
  `screen.jsx` is now a DC-template shell + logic-class store over per-view modules
  (`views/*.jsx`) and a `data.js` seed module with internally consistent figures (the
  trial balance proves, the recon variance equals the one open break). NavRail now
  *drives* the views (the old duplicate tab strip is gone): General Ledger · Chart of
  Accounts · Trial Balance · Reconciliation · **FX Revaluation** (new) · **Period
  Close** (new, `GateRail` + `ValidationIssueList` + `ReadinessPanel`) · Statements ·
  Tax Lots. Hand-rolled tabs/search/segmented/drawer replaced with `Toolbar`, `Select`,
  `SegmentedControl`, `Drawer`, `EmptyState`, `TrialBalance`. Flows: the open recon
  break and the FX reval both open the journal drawer *prefilled* and settle the books
  on post; period lock gates on the close rail and flips the workstation read-only
  (`ReadOnlyBanner`, disabled posting, `Reopen` to reverse). Adds `CommandPalette`
  (Ctrl-K), `HotkeysProvider` (`G L`…`G X`, `N`), `ToastProvider` confirmations, CSV
  export, and a voided-pair ledger example. Tweaks: environment · role (viewer =
  read-only) · density.

## 1.16.0 — 2026-07-05

### Added
- **Charts:** `YieldCurve` — term-structure chart (yield vs tenor) for Basket Builder and
  fixed-income surfaces; sqrt-of-years x-scale, prior-date/benchmark overlay, tenor-pair
  spread readout in bp with inversion flag. Card: *Yield curve*.
- **Trading:** `OptionChainTable` — straddle-layout option chain (calls | strike | puts,
  mirrored columns), accent-wash ITM shading, spot marker row, configurable
  greeks/IV/OI columns, keyboard strike selection. Card: *Option chain*.
- **Cards:** *Token playground* (Theming) — flip brand / theme / density live on one set
  of components; the white-label pitch, demonstrated.
- `docs/PERFORMANCE.md` — measured consumer payload (135 KB gzip bundle · 13 KB CSS
  closure · React peer), CSS-injection behavior (verified duplicate-free), and the
  monolith trade-off.

### Changed
- **Keyboard pass on the mouse-heavy data surfaces** (contracts in `ACCESSIBILITY.md` §2
  and `PATTERNS.md › Keyboard navigation`): `DepthLadder` price column is one tab stop
  (↑/↓ levels, Home/End, Enter prefills); `CoverageMatrix` cells navigate as a roving
  grid with the readout following focus; `WorksheetGrid` adds Home/End, Ctrl+corner, and
  PageUp/PageDown jumps. No visual change; non-interactive usages unaffected.
- **Dark-mode contrast sweep — measured** (full table now in `ACCESSIBILITY.md` §1):
  dark `--accent-dim` `#3C6688` → `#609BC9` (pressed-button ink was 3.11:1; accent-dim-as-
  text ~2.5:1 on panels — dark pressed now goes lighter, per dark-UI convention); dark
  `--text-muted` `#8893A0` → `#8F9AA7` (was 4.28:1 on hover rows). `check_contrast.py`
  gained pressed/hover/accent-text pairs plus composited alpha-wash pairs (`WASH_PAIRS`).

## 1.15.0 — 2026-07-05

### Added
- **Templates** (§D of `docs/CONTRACT_AUDIT.md` — the three largest uncovered backend
  domains): `reconciliation-workstation` (imports · statement runs · SLA-tracked
  break-case triage via `CaseQueue`/`SlaChip` · account sign-off queue with next-best-
  action), `family-office` (balance-sheet metrics · `OwnershipGraph` with entity
  inspector · evidence-backed accounts with `ProvenanceChip` · `CommitmentBar` stack ·
  capital activity · readiness blockers), `covered-call-lab` (backtest request params ·
  strategy-vs-underlying `EquityCurve` · 21-metric block · trades · chain preview with
  filter pass/reject · run library).

## 1.14.0 — 2026-07-05

Driven by the `Meridian.Ui.Shared` contract audit (`docs/CONTRACT_AUDIT.md`).

### Added
- **Operations:** `CaseQueue` (case-triage list mirroring `ReconciliationCaseSummaryDto` /
  `OperationsBreakCaseDto` — priority rail, status, SLA, assignee), `SlaChip` (the case
  clock: OnTrack / Warning / Breached with due-in / over-by durations; deterministic via
  `now`), `ProvenanceChip` (the recurring evidence tuple — source system · as-of ·
  worst-of completeness/reconciliation dot).
- **Charts:** `OwnershipGraph` — layered entity/control structure for
  `FamilyOwnershipGraphDto`; measured SVG edges with ownership-% labels; accepts DTO
  field names directly.
- **Accounting:** `CommitmentBar` — called / unfunded / distributed / NAV funding bar
  with derived DPI · TVPI for `CapitalCommitmentDto`.
- **Cards:** *Case triage & SLA* (Operations), *Ownership graph* (Charts), *Capital
  commitments* (Accounting), *Contract audit · 2026-07* (Documentation).
- `docs/CONTRACT_AUDIT.md` — template↔endpoint coverage, data-shape drift, next steps.

### Fixed
- `status.js` normalizer covers the contract enums that previously fell to "info":
  `NotReady`, `Skipped`, `Matched`, `BreaksDetected`, `Resolved`, `AwaitingApproval`,
  `OnTrack`, `AtRisk`, `Breached`, `Paused`, `SignedOff`, `Reopened`.

## 1.13.0 — 2026-07-05

### Changed
- **Design System tab reorganized.** The 32-card "Components" dump is split by domain — Core ·
  Data · Charts · Accounting · Trading · Operations · Shell — mirroring `components/`. Singleton
  groups folded away (Layout → Core, Dark Mode → Theming, Docs → Documentation); all theming /
  white-label / dark-mode cards now live under one **Theming** group.
- Card names normalized: middot separators throughout; recency names ("new components/primitives")
  replaced with what the cards actually show.

### Added
- **Depth & Motion** foundations group (3 cards): *Surface ladder* (flat elevation, light + dark,
  the border ladder), *Stacking order* (the `--z-*` ladder and overlay shadow policy), *Motion*
  (timing tokens, what animates, what never does).
- **System map** card (Documentation): foundations → 149 exports → 19 templates at a glance.

### Fixed
- readme component counts (117 → 149 exported symbols; `accounting/` 7 → 11 in Architecture).

## 1.12.0 — 2026-07-05

### Added
- **Accounting components:** `TrialBalance` (sectioned Assets/Liabilities/Equity/Revenue/Expenses
  proof with subtotals and a Σdebit = Σcredit footer), `AgingTable` (AR/AP aging buckets with
  escalating amber → red washes, bucket-share footer), `FxRevaluationTable` (booked vs. current
  rates, per-row and net unrealized G/L), `AllocationEditor` (interactive weight editing over
  cent-exact `allocateAmount` splits).
- **Money helpers:** `daysBetween`, `agingBucketIndex`, `buildTrialBalance` (postings → trial-
  balance rows), `amortizeStraightLine` (remainder-exact schedules), `fxRevalue` — all exposed on
  the `Money` carrier and used by the new components, so consumer math ties out to the pixel.
- **Cards:** "Trial balance & aging" and "FX reval & allocation" under Components.

### Fixed
- `Money.d.ts` parse error (no interface declaration) — the carrier is now typed via
  `MoneyHelpers`, restoring it to the compiled manifest.

## 1.11.0 — 2026-07-02

### Added
- **Components:** `InstrumentChip` (symbol + venue + asset-class identity chip), `DepthLadder`
  (classic DOM price ladder — washed depth, spread bps, price-click prefill), `CoverageMatrix`
  (symbol × session data-availability heat grid), `TimeframeSwitcher` + `AsOfControl` (chart-
  toolbar resolution picker + Live/as-of session clock), `FilterBuilder` (structured
  field/operator/value query rows that compile a predicate), `LogTail` (mono evidence stream
  with level filters + follow-tail), `EventTimeline` (vertical audit trail).
- **Templates:** `trading-desk` (watchlist, depth ladder, order ticket behind the live confirm
  gate, blotter/fills tape), `ingestion-operations` (provider freshness, backfill queue with
  inline progress, coverage heat, run log), `report-scheduler` (report packs, recipients,
  delivery-history timeline).
- **Density:** new **`terminal`** scope (`data-theme-density="terminal"`) — 26px rows, tightest
  spacing, for multi-monitor ops walls. `DensityToggle` gains it as the densest option.
- **Docs & cards:** `docs/COOKBOOK.md` (five assembly recipes) + card, Token Browser card
  (searchable, copy-to-clipboard, live swatches), White-Label Kit card (live `data-brand`
  switcher), Keyboard Map card (shortcut matrix + live keydown tester). `.prompt.md` coverage is
  now **100%** — every component ships per-component usage guidance.
- **Tooling:** `scripts/visual_diff.py` (Playwright screenshot-diff over every `@dsCard`, Pillow
  diff, baseline/compare modes) and governance rule `prompt-coverage` (every `X.jsx` + `X.d.ts`
  requires an `X.prompt.md`).

## 1.10.0 — 2026-07-02

### Added
- **Components:** `Timestamp`, `Delta`, `TagInput`, `SplitPane`, `Toolbar` (+Group/Spacer/Divider),
  `DiffView`, `PnLCalendar`, `OrderTicket`, `Blotter`, `FillsFeed`, `ErrorBoundary`,
  `NotificationCenter`, `HotkeysProvider` (with the "?" shortcut sheet), `UserMenu`, `RoleBadge`,
  `ReadOnlyBanner`.
- **Templates:** `alerting-workstation` (SplitPane triage, loading/empty/error ladder, DiffView
  audit rail), `settings-admin` (forms, validation stack, danger zone with typed confirm).
- **Stylesheets:** `tokens/print.css` (reports print as documents), `tokens/contrast-modes.css`
  (Windows High Contrast / forced-colors).
- **Tooling:** `scripts/check_contrast.py` + `tests/test_contrast.py` (WCAG token regression
  gate), `tests/unit-tests.html` (behavioral harness), `tests/gallery.html` (all cards on one
  page — visual-regression baseline), governance rules `white-on-fill` and `gallery-coverage`.

### Changed
- **Tables now read `--theme-row-height`** — `DenseDataTable`/`FilteredDataTable` row height and
  virtualization math track the density token instead of a hardcoded 40/32px. See UPGRADING.md.
- `OrderTicket` composes `Input`/`Select` instead of hand-rolled fields.
- `WorkstationTopbar` environment chip renders `Badge` instead of a duplicate implementation.

## 1.9.0 — 2026-07-01

### Fixed
- **Contrast:** 11 components hardcoded white text on solid fills (failed AA in dark mode);
  all now route through `--text-on-accent` / new alias `--text-on-fill`.
- **Focus:** `Dialog`/`Drawer`/`Modal` now share `core/useOverlayFocus.js` — the documented Tab
  trap is actually implemented (it previously wasn't, anywhere); focus restore on close; Modal
  gained focus management; CommandPalette traps Tab.
- `Popover` positions in `useLayoutEffect` (no (0,0) flash); stale 6px radius fallbacks → 2px;
  overlay entrance motion unified on `--motion-base`/`--ease-standard`.

### Changed
- Shell chrome (`WorkstationTopbar`, `StatusBar`) tokenized — 9 new `--topbar-*`/`--chrome-*`
  tokens replace raw hex.
- Superseded process reports moved to `docs/changelog/archive/`.
