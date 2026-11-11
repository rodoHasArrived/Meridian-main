# Meridian Design System — Getting Started

Welcome to **Meridian**, a production-ready design system for operator workstations. This guide walks you through setup, consumption, and common patterns.

## 30-second overview

Meridian ships **223 exported symbols**, **7 pre-built workstation templates**, and **632 design tokens** grounded in the desktop app's **"Programmed Institutionalism"** language: light institutional workstation, hairline borders, one terracotta accent, desaturated semantics, no gradients or shadows.

- **Light canvas** `#F2F0EB` with panels in gray-white
- **Hard edges** (0–2px radii, visible borders, no card shadows)
- **One signal** terracotta `#C56343` for brand/scenario moments; graphite `#29323A` for primary actions
- **Mono data** (IBM Plex Mono) for all prices, IDs, counts, timestamps
- **Motion** 120–300ms standard cubic on color/border; no springs, no entrance animation
- **Keyboard-first** (Ctrl-K command palette, arrow navigation, focus rings)

---

## Installation & setup

### Step 1: Copy a template to your project

The fastest way to get started is to copy a template. **If you're new, start with `dashboard-workstation`** — it's a complete operator overview that exercises most components in one screen.

```bash
# Copy the template folder to your consuming project
cp -r templates/dashboard-workstation/ ../my-project/meridian-dashboard/
```

### Step 2: Open the template and edit

1. Navigate to the template folder: `cd ../my-project/meridian-dashboard/`
2. Open `DashboardWorkstation.dc.html` in your browser
3. Edit the markup directly — the file is a **Design Component** (streamable, editable HTML)
4. Adjust `ds-base.js` if the design system is at a different depth:

```javascript
// ds-base.js — configure the path to the design system
(() => {
  const base = '../..'; // ← change this path if your copy sits at a different depth
  // (rest of the script loads styles.css and _ds_bundle.js from that base)
})();
```

### Step 3: Reference the design system

Once the bundle loads, all components are available on `window.MeridianDesignSystem_205d17`:

```html
<script src="../../_ds_bundle.js"></script>
<script>
  const { Button, DataGrid, Dialog } = window.MeridianDesignSystem_205d17;
</script>
```

---

## What's in a template?

Each template is a self-contained folder with:

| File | Purpose |
|------|---------|
| `<TemplateName>.dc.html` | Editable entry point (Design Component) |
| `ds-base.js` | Loads the design system bundle + styles |
| `.thumbnail` | Preview thumbnail for the system browser |
| (optional) `data.json`, `logic.js` | Supporting data and business logic |

Templates are **directly editable in the browser** — click any text or element to edit it inline. For larger changes, edit the source and reload.

---

## Core templates

| Template | Use when | Components highlighted |
|----------|----------|------------------------|
| **dashboard-workstation** | Building an operator overview (portfolio, equity curve, positions) — the 4.0 reference: declared regions, one grid, enveloped P&L | `ScreenComposition`, `DataGrid`, `ValueEnvelope`, `MetricCard`, `WorkstationTopbar`, `NavRail`, `StatusBar` |
| **security-master-registry** | Building a symbol/identifier registry with search and detail panes | `DataGrid`, `Drawer`, `EntitySummary`, `TreeView` |
| **charting-workstation** | Building a charting terminal (candlesticks, studies, crosshair readout) | `CandleChart`, `CommandPalette`, `ContextMenu`, `TimeframeSwitcher` |
| **accounting-workstation** | Building a books workstation (GL, trial balance, reconciliation, FX reval, period close) — journal posting is its compose mode (`view=gl&compose=1`) | `LedgerTable`, `StatementTable`, `AccountTree`, `ReconciliationPanel`, `JournalEntryForm` |
| **basket-builder** | Building a basket/portfolio construction workflow (constituents, concentration risk) | `DataGrid`, `CorrelationHeatmap`, `SegmentedControl`, `BulkActionBar` |
| **report-library** | Building a report catalog with search/filter and a set-parameters-and-run flow | `DataGrid`, `Dialog`, `SeverityBadge` |
| **strategy-formation** (+ `field-formula`, `strategy-runs`, `strategy-governance`) | Building a cell-based research/strategy workflow end to end — formation stages + builder worksheet, run ledger with backtest/compare/lab views, governance; one shared store and nav rail | `WorksheetGrid`, `GateRail`, `Stepper`, `EquityCurve`, `Histogram` |

**Tip:** For a blank canvas with just the shell, copy **`blank-workstation`** — one `WorkstationShell` with topbar, nav rail, status bar and an empty content canvas.

---

## Component reference

**All 189 exports are documented in `docs/COMPONENT_API_REFERENCE.card.html`** (plus sub-cards for each family). Open that file in your browser to see live examples, prop defaults, and keyboard behavior.

Quick reference by category:

### **Core UI (51 modules)**
Buttons, inputs, selects, checkboxes, toggles, badges, spinners, sliders, dialogs, drawers, tooltips, context menus, command palette, form helpers, layout primitives (Grid, Flex, Stack).

**Most common:** `Button`, `Input`, `Select`, `Dialog`, `Drawer`, `DataGrid`, `Grid`, `Flex`.

### **Data & tables (21 modules)**
Dense data grids, virtualized lists, pagination, bulk actions, entity summaries, metric cards, skeletons.

**Most common:** `DataGrid`, `MetricCard`, `KeyValueGrid`.

### **Accounting (17 modules)**
Ledger tables, GL trees, statements, journal entry forms, tax lots, reconciliation panels, the investment-event lifecycle set, and the shared `Money` arithmetic.

**Most common:** `LedgerTable`, `JournalEntryForm`, `AccountTree`.

### **Charts (23 modules)**
Candlestick, equity curve, drawdown, histogram, depth profile, correlation heatmap, scatter, sparkline, treemap, waterfall, yield curve, ownership graph, plus the v2.1 visualization-grammar set (TraceChart, ComparisonPlate, IntervalBar, LineageGraph).

**Most common:** `CandleChart`, `EquityCurve`, `ChartCard`.

### **Shell (5 modules)**
`WorkstationShell` (the frame: chrome slots around a crash-isolated main + optional evidence rail), topbar, nav rail, status bar, session controls.

### **Operations (17 modules)**
The operator readiness/severity layer (`SeverityBadge`, `ReadinessPanel`, `GateRail`, `ValidationIssueList`, `EvidenceLink`, `TrustStrip`, `WorkspaceSection`, `DiffView`, `LogTail`, `EventTimeline`, `CaseQueue`, `SlaChip`, `ProvenanceChip`) plus the v2.1 evidence layer (`ClaimHeader`, `DataCondition`, `EvidenceInspector`, `ConnectionState`).

**Most common:** `SeverityBadge`, `ReadinessPanel`, `GateRail`.

### **Trading (6 modules)**
`OrderTicket` (live confirm gate), `Blotter`, `FillsFeed`, `DepthLadder`, `OptionChainTable`, `TimeAndSalesTape`.

### **Feedback**
`Toast` + `ToastProvider` (in `core/`) — transient action confirmations, `aria-live` announced.

---

## Styling & tokens

### Global CSS entry point

Link Meridian's stylesheet in your HTML `<head>`:

```html
<link rel="stylesheet" href="../../Meridian/styles.css" />
```

This imports **all 632 design tokens** (colors, spacing, typography, elevation) and **component stylesheets**.

### Token categories

| Category | Examples | Used for |
|----------|----------|----------|
| **Colors** | `--accent`, `--border`, `--text-primary`, `--bg-light`, `--red`, `--green` | Backgrounds, text, borders, accents |
| **Typography** | `--font-body`, `--font-mono`, `--text-sm`, `--text-lg` | Font families and sizing |
| **Spacing** | `--space-xs` (3px), `--space-md` (12px), `--space-lg` (16px), `--space-xl` (24px), `--space-2xl` (32px) | Margins, padding, gaps |
| **Elevation** | `--shadow-card`, `--shadow-menu`, `--focus-ring` | Shadows, outlines, focus states |

### Inline styles example

```html
<div style="background: var(--bg-light); border: 1px solid var(--border); padding: var(--space-lg);">
  <p style="color: var(--text-primary); font-family: var(--font-body);">Content here</p>
</div>
```

### Dark mode & themes

Meridian supports three modes:

1. **Auto (default):** Respects OS `prefers-color-scheme`
2. **Manual:** Set `data-theme="dark"` or `data-theme="light"` on `<html>`
3. **Density:** Set `data-theme-density="compact"` or `"spacious"` on `<body>` (adjusts spacing throughout)

```html
<!-- Force dark mode -->
<html data-theme="dark">
  ...
</html>

<!-- Force compact density -->
<body data-theme-density="compact">
  ...
</body>
```

### White-label theming

To customize colors for your brand, override tokens in a `<style>` block **after** importing `styles.css`:

```html
<link rel="stylesheet" href="Meridian/styles.css" />
<style>
  :root {
    --accent: #2563eb;        /* Your primary blue */
    --accent-hover: #1d4ed8;
    --accent-dim: #1e40af;
  }
</style>
```

---

## Common patterns

### Build a simple form

```html
<FormField label="Account" required>
  <Combobox options={accounts} value={account} onChange={setAccount} />
</FormField>

<FormField label="Amount" error={errors.amount}>
  <NumberInput value={amount} onChange={setAmount} />
</FormField>

<Button variant="primary" onClick={submit}>Post</Button>
```

**See:** `core/form-controls.card.html` for full examples.

### Display a data table with sorting

```html
<DataGrid
  columns={[
    { key: "symbol", label: "Symbol" },
    { key: "price", label: "Price", align: "right" },
    { key: "change", label: "Change %", align: "right", sortable: true },
  ]}
  rows={rows}
  sortKey={sortKey}
  sortDir={sortDir}
  onSort={(key) => { /* ... */ }}
/>
```

**See:** `data/data.card.html` for sorting, filtering, pagination, and selection.

### Show a dialog with a form

```html
<DialogForm open={open} onClose={close} title="New account"
  size="lg" submitLabel="Create account" onSubmit={submit}>
  <FormField label="Code" required>
    <Input value={code} onChange={setCode} />
  </FormField>
</DialogForm>
```

For a confirm, use `Dialog size="sm"` with your own `DialogBody` / `DialogFooter`.

**See:** `core/dialog.card.html` for all overlay patterns.

### Display a metric card with a bounded ratio

```html
<MetricCard
  label="Net liquidation"
  value="$2,431,442.59"
  delta="+$14,221"
  tone="success"
/>

<PanelSurface>
  <Eyebrow>Fill rate · 30d</Eyebrow>
  <ProgressBar value={78} target={95} showValue />
</PanelSurface>
```

**See:** `data/data-extras.card.html` for metric and summary layouts.

### Wire live data updates

Meridian numbers update **silently** (no animation) to support streaming data. Use `FreshnessIndicator` to signal staleness:

```html
<FreshnessIndicator
  status={isStale ? "stale" : "live"}
  source="Polygon"
  lastSeen={timestamp}
/>
```

**See:** `core/live-status.card.html` for the full pattern.

---

## Keyboard navigation

All interactive components follow these conventions:

| Component | Key | Behavior |
|-----------|-----|----------|
| Dialog / Drawer | `Escape` | Close |
| Select / Combobox | `Arrow Down/Up` | Cycle options |
| TreeView | `Arrow Right` | Expand; Left collapses |
| Table | `Click` to select row; `Ctrl+Click` for multi-select |
| CommandPalette | `Ctrl+K` (or `Cmd+K` on Mac) | Open command search |
| All focusable | `Tab` / `Shift+Tab` | Navigate focus |

Every control shows a `--focus-ring` outline (2px solid, 2px offset) on keyboard focus.

---

## Consuming in React

If you're building with React, import the bundle and use components as you would any React library:

```jsx
import React, { useState } from 'react';

// Load the design system bundle
const { Button, Dialog, Input } = window.MeridianDesignSystem_205d17;

export function MyComponent() {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState('');

  return (
    <>
      <Button onClick={() => setOpen(true)}>Open</Button>
      <Dialog open={open} onClose={() => setOpen(false)} title="Edit">
        <Input value={value} onChange={(e) => setValue(e.target.value)} />
      </Dialog>
    </>
  );
}
```

---

## Troubleshooting

### Components don't load
- Check that `_ds_bundle.js` is loading (open browser DevTools → Network tab)
- Verify `ds-base.js` path points to the correct design system folder
- Check console for errors: DevTools → Console

### Styles don't apply
- Ensure `styles.css` is linked in `<head>`
- Check that you're using token variables (`var(--accent)`) not hardcoded colors
- Verify dark mode is not forcing a light theme (`data-theme="light"`)

### Dark mode not working
- Check `data-theme` attribute on `<html>` (manual override)
- Verify `@media (prefers-color-scheme: dark)` is enabled in OS settings
- Clear browser cache if tokens don't update

### Text is too small
- For a 1920×1080 workstation, body text should never be smaller than 13px
- Use `--text-sm` (12px) only for secondary labels
- Use `--text-lg` (16px) or larger for titles

### Focus ring is hidden
- Check if element has `overflow: hidden` (clips outline)
- Set `outline-offset: -2px` if internal padding occludes the ring
- Ensure button/input is not using `appearance: none` without a custom focus style

---

## Next steps

1. **Copy a template** — start with `dashboard-workstation`
2. **Explore components** — open `components/core/core.card.html` in your browser
3. **Read guidelines/PATTERNS.md** — decision trees for choosing the right component
4. **Customize tokens** — override `--accent` and other tokens for your brand
5. **Build** — start from `WorkstationShell` (it owns the chrome and the `<main>` landmark) and compose with core components

---

## Questions?

- **Component API:** Open any `<Name>.card.html` in your browser for live props and examples
- **Patterns & architecture:** See `guidelines/PATTERNS.md`
- **Visual foundations:** See `guidelines/VISUAL_FOUNDATIONS.md`
- **Icons & brand:** See `guidelines/ICONOGRAPHY.md`

**Happy building!**
