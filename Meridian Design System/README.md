# Meridian Design System

An operator-workstation design system for **Meridian** — a self-hosted trading, market-data,
research, and reporting platform. Meridian collects equities data from providers (Polygon,
IBKR, Databento…), maintains a security master, runs backfills and quality scans, executes
backtests/strategy runs, and operates live & paper trading with accounting and report-pack
delivery.

This system is grounded in the **desktop application** (`src/Meridian.Wpf`), whose visual
language is **"Institutional Ops"**: a **light** workstation — a paper canvas (`#ECEFF3`) of
white cards, capped by a near-black brand bar and status bar (`#171A1F`). One muted teal-blue
accent (`#2F6F8F`), desaturated semantic colors used as alpha-10 washes (never solid fills),
Segoe UI + Cascadia Mono, and depth built from **hairline borders and a whisper of shadow** —
no gradients, no glow. Think a Bloomberg-grade institutional terminal rendered on paper, not a
dark consumer-fintech app.

## Sources

- **GitHub:** [rodoHasArrived/Meridian-main](https://github.com/rodoHasArrived/Meridian-main) —
  the full platform monorepo. The **canonical visual source is the desktop app**:
  `src/Meridian.Wpf/Styles/` — `ThemeTokens.xaml` (colors), `ThemeTypography.xaml` (type),
  `ThemeSurfaces.xaml` (cards/badges/metrics), `ThemeControls.xaml` (buttons/inputs/nav). The
  React web dashboard under `src/Meridian.Ui/dashboard/` is a secondary surface and uses a
  different, darker palette — this system follows the **WPF desktop** patterns. Explore the
  screen view-models for each module's real data shapes.
- **Local codebase mount:** `src/` (same repo; WPF desktop under `src/Meridian.Wpf/`, .NET
  host under `src/Meridian/`).
- Related repos by the same owner (not imported): `stock-strategy-build`,
  `backtest-data-visual`.

## Products / surfaces

| Surface | What it is | Template |
| --- | --- | --- |
| **Workstation dashboard** | Operator overview — portfolio metrics, equity curve, positions | `templates/dashboard-workstation/` |
| **Security Master** | Symbol/identifier registry with an entity inspector | `templates/security-master-registry/` |
| **PlotTool** | Charting workstation (OHLC, studies, crosshair readout) | `templates/charting-workstation/` |
| **Accounting** | Books workstation — GL, trial balance, reconciliation, statements, tax lots, journal entry | `templates/accounting-workstation/` |

## Content fundamentals

Full guide: `guidelines/CONTENT_FUNDAMENTALS.md`.

- **Voice:** terse, declarative, evidence-first. "Backfill complete · 412,008 bars · 0 gaps" —
  state the fact, then the evidence. No exclamation points, no marketing adjectives, no emoji.
- **Casing:** sentence case for UI copy and buttons ("Run backfill", "Close position").
  Small-caps (not ALL-CAPS) for eyebrow labels, table headers, and badges — the desktop sets
  `Typography.Capitals=AllSmallCaps` on these, so authentically they render as small-caps.
- **Person:** imperative verb-first for actions; no "I"/"we"; "you" rarely, only in empty
  states and confirmations.
- **Numbers:** mono (Cascadia/JetBrains), tabular, fixed decimals (prices 4dp), explicit signs
  on deltas, UTC timestamps as `2026-06-09 20:00:00Z`. Counts use thin separators: `412,008`.
- **Errors/status:** name the system, the time, the evidence — "Provider offline · Polygon
  last seen 14:02:11Z". Never apologize, never anthropomorphize.

## Visual foundations

Full guide: `guidelines/VISUAL_FOUNDATIONS.md`. Tokens live in `tokens/` (lifted verbatim from
`ThemeTokens.xaml` / `ThemeTypography.xaml`).

- **Color — light, institutional.** Canvas `#ECEFF3` → command bar `#F5F7FA` → card `#FFFFFF`
  → raised `#FAFBFC`; chrome bars near-black `#171A1F`. One accent: teal-blue `#2F6F8F`
  (primary buttons, focus rings, active nav, crosshair — **one per screen**), pressed
  `#255B75`. Semantic accents are desaturated and come as **trios** (dim text · solid border ·
  alpha-10 wash, never solid fills): green `#16885F`, red `#BA3F55`, amber `#B7791F`, purple
  `#6F5BA7`. Text ladder: primary `#22272E` · secondary `#4D5967` · muted `#6E7781`.
- **Environment modes** are always visible: **Live** red `#BA3F55` (real money), **Paper** blue
  `#2F6F8F` (simulated), **Fixture** amber `#B7791F` (replay).
- **Type:** Segoe UI Variable Display (titles, 600), Segoe UI Variable Text (body, 13px),
  Cascadia Mono / JetBrains Mono (all data, tabular). Ramp: page title 22 · section 15 ·
  card title 14 · body 13 · metric 24 · data value 18 · label 10 (small-caps, muted).
- **Spacing & density:** 24 section · 16 card · 12 compact · 8 tight. Chrome: 48px brand bar,
  28px status bar, 224px (14rem) nav rail, ~34px table rows.
- **Radii:** 4px chips/badges · 6px buttons/inputs · 8px cards/panels. Metric cards carry a
  **3px left-accent border** (tone inspectors 4px). Never large friendly radii.
- **Borders & shadows:** structure is **1px hairlines** (`#D7DCE2`; hover `#B8C2CC`). Elevation
  is a whisper only — card `0 1px 1px /.08`, elevated `0 1px 2px /.10`. **No gradients, no
  glow, no hard-offset shadows.**
- **States:** primary-button hover = accent at 80% α, press = `--accent-dim`; ghost hover =
  `#F1F4F7`; nav active = `--sidebar-active` wash + **3px teal-blue left indicator**; table row
  hover/selected = `#E6EEF5` + `inset 3px 0 0 #2F6F8F` left rail; input focus = teal-blue ring.
  Tables zebra-stripe at `#FAFBFC`.
- **Motion:** minimal — 100–150ms ease on background/border/color. No springs, no entrance
  choreography. Live numbers update without animation.
- **Charts:** plot `--chart-plot`, grid `--chart-grid`, axis labels `--chart-axis`, frame
  `--chart-border`, crosshair `--chart-crosshair`. Series: equity / up `--chart-equity` (green),
  drawdown / down `--chart-drawdown` (red), primary line `--chart-primary` (teal-blue),
  benchmark `--chart-secondary` (dashed), warning `--chart-warning` (amber). Area and volume
  fills use the alpha-10 / alpha-20 overlays — never opaque fills. Full reference:
  `guidelines/colors-charts.card.html`.
- **Accounting:** all money flows through `AmountCell` — mono tabular figures, fixed decimals,
  accounting parentheses for negatives `(1,234.00)`, zero-as-dash `—`, optional P&L tone
  (negative `--red-dim` / positive `--green-dim`). Tables prove their own arithmetic: `LedgerTable`
  footers turn red when Σdebit ≠ Σcredit, `ReconciliationPanel` flags "Out by …", `JournalEntryForm`
  gates Post on a balanced entry. `StatementTable` double-rules grand totals; `AccountTree` rolls
  child balances into bold parent rows. `TaxLotTable` classifies lots short/long-term (amber /
  teal-blue badge) and rolls up basis, value, and unrealized P&L. Cards:
  `components/accounting/accounting-*.card.html`.

## Iconography

Full guide: `guidelines/ICONOGRAPHY.md`. Meridian ships its own **line-icon module set** — 47
icons in `assets/icons/`, drawn on a 24px grid with 1.5px strokes and `stroke="currentColor"`
so they take the accent or text color of their context. Use for nav and module identity at
16–20px. No icon font, no emoji ever. Unicode glyphs appear only as functional marks inside
data (`⌕` search, `Ctrl K` kbd, `Δ`, `·` separators, `↑↓` sort).

Brand marks in `assets/brand/`: `meridian-mark.svg` (use on the dark chrome bar),
`meridian-mark-light.svg` (on the dark bar / colored contexts), `meridian-mark-monochrome.svg`,
`meridian-wordmark.svg` (+ stacked), `meridian-tile-256.png`, `meridian-hero.svg`.

## Index

| Path | Contents |
| --- | --- |
| `styles.css` | Global entry — `@import`s the token files below |
| `tokens/colors.css` | "Institutional Ops" light palette (from `ThemeTokens.xaml`) |
| `tokens/typography.css` | Segoe + Cascadia families and the type ramp |
| `tokens/elevation.css` | Radii, accent bars, hairline shadows, spacing rhythm, chrome heights |
| `tokens/base.css` · `tokens/fonts.css` | Body reset / data + label helpers · webfont load |
| `guidelines/` | Brand / content / visual / iconography docs + foundation specimen cards |
| `assets/brand/`, `assets/icons/` | Brand marks · 47 line module icons |
| `components/core/` | 10 primitives: `Button`, `Badge`, `Eyebrow`, `Input`, `PanelSurface`, `StatusBanner`, `MultiSelect`, `SelectCheckbox`, `ContextMenu`, `Validators` |
| `components/data/` | 6 data UI: `MetricCard`, `DenseDataTable`, `KeyValueGrid`, `EntitySummary`, `EditableCell`, `ExpandableDataTable` |
| `components/accounting/` | 7 accounting: `AmountCell`, `LedgerTable`, `AccountTree`, `StatementTable`, `ReconciliationPanel`, `JournalEntryForm`, `TaxLotTable` + `money.js` helpers |
| `components/shell/` | 3 chrome: `WorkstationTopbar`, `NavRail`, `StatusBar` |
| `components/charts/` | 3 charts: `CandleChart`, `ChartCard`, `EquityCurve` — OHLC, equity curve, chart card wrapper |
| `templates/dashboard-workstation/` | Dashboard workstation template — portfolio metrics, equity curve, positions |
| `templates/security-master-registry/` | Security Master template — symbol registry + entity inspector |
| `templates/charting-workstation/` | PlotTool template — charting workstation (OHLC, studies, crosshair) |
| `templates/accounting-workstation/` | Accounting template — GL, trial balance, reconciliation, statements, tax lots, journal entry |
| `SKILL.md` | Agent-skill entry point |

## Compilation & consumption

**Components compile to `_ds_bundle.js`** — consume via `const { Button } = window.MeridianDesignSystem_4f61be` after loading the bundle. Each component injects its own scoped CSS keyed to the design tokens, so a consuming page only needs to link `styles.css` and the bundle.

**Templates in `templates/<slug>/`** are the starting point for consuming projects:
1. Copy the folder to your project
2. Open `<Slug>.dc.html` (a Design Component file — directly editable in the browser)
3. Edit the markup, props, or logic
4. Adjust the `base` path in the sibling `ds-base.js` if the template sits at a different depth than `templates/`

Each template brings a complete workstation with pre-wired components, sample data, and interaction patterns — copy → customize → ship.

## Core patterns & rules

**Data-rich UI over whitespace.** Meridian is an operator workstation — tight rows, hairlines, small-caps labels, tabular numbers. One teal-blue primary action per screen; everything else ghost/link. Never invent colors — use tokens; tint only via the semantic alpha-10/alpha-20 overlays.

**Accounting & money flows.** All currency goes through `AmountCell` — mono tabular, fixed decimals, accounting parentheses for negatives, zero-as-dash. Tables prove their own arithmetic: `LedgerTable` flags imbalance, `ReconciliationPanel` flags "Out by …", `JournalEntryForm` gates Post on balance, `StatementTable` double-rules totals, `AccountTree` rolls child balances, `TaxLotTable` classifies long/short and computes basis/unrealized.

**Structured editing.** Most tables support inline editing (click a cell → edit → blur to save). Bulk operations are batch-select-then-act. Complex dialogs are rare — prefer expandable detail rows with `ExpandableDataTable`. Context menus appear on right-click for row-specific actions.

**Environment visibility.** Every workstation displays a Live/Paper/Fixture badge so operators never mistake simulation for production. Supported by environment-mode CSS and conditional rendering in templates.

**Depth is borders + shadow.** One hairline (`#D7DCE2`), hover `#B8C2CC`. Card shadow `0 1px 1px /.08`, raised `0 1px 2px /.10`. No gradients, no glow. States: button hover = accent at 80% α, press = accent-dim; nav active = wash + 3px left bar; table row selected = wash + left rail.

## Fonts

The desktop uses native Windows faces — **Segoe UI Variable** (display/body) and **Cascadia
Mono** (data) — referenced by name with system fallback stacks; no webfont binaries ship for
them. **JetBrains Mono** is loaded from Google Fonts as the cross-platform stand-in for Cascadia
so numerics render identically everywhere. If you want pixel-exact desktop parity in the browser,
upload the Segoe UI Variable and Cascadia Mono font files and add `@font-face` rules.
