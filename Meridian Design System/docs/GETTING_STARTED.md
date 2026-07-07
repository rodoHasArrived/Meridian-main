# Meridian Design System — Getting Started

Welcome to **Meridian**, a production-ready design system for operator workstations. This guide walks you through setup, consumption, and common patterns.

## 30-second overview

Meridian ships **109 components**, **14 pre-built templates**, and **331 design tokens** grounded in the desktop app's **"Institutional Ops"** language: light institutional workstation, hairline borders, one steel-blue accent, desaturated semantics, no gradients or shadows.

- **Light canvas** `#DEE3EA` with panels in gray-white
- **Hard edges** (0–2px radii, visible borders, no card shadows)
- **One accent** steel-blue `#2F6F8F` for primary actions
- **Mono data** (Cascadia/JetBrains Mono) for all prices, IDs, counts, timestamps
- **Motion** 100–150ms ease on color/border; no springs, no entrance animation
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

Once the bundle loads, all components are available on `window.MeridianDesignSystem_4f61be`:

```html
<script src="../../Meridian/_ds_bundle.js"></script>
<script>
  const { Button, DenseDataTable, Modal } = window.MeridianDesignSystem_4f61be;
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
| **dashboard-workstation** | Building an operator overview (portfolio, equity curve, positions) | `WorkstationTopbar`, `NavRail`, `StatusBar`, `MetricCard`, `EquityCurve`, `DenseDataTable` |
| **security-master-registry** | Building a symbol/identifier registry with search and detail panes | `FilteredDataTable`, `Drawer`, `EntitySummary`, `TreeView` |
| **charting-workstation** | Building a charting terminal (candlesticks, studies, crosshair readout) | `CandleChart`, `CommandPalette`, `ContextMenu`, `LinearGauge` |
| **accounting-workstation** | Building a books workstation (GL, trial balance, reconciliation) | `LedgerTable`, `StatementTable`, `AccountTree`, `ReconciliationPanel` |
| **journaling-workstation** | Building a double-entry posting workstation | `JournalEntryForm`, `DenseDataTable`, `Modal`, `FormValidation` |
| **basket-builder** | Building a basket/portfolio construction workflow (constituents, concentration risk) | `DenseDataTable`, `CorrelationHeatmap`, `SegmentedControl`, `BulkActionBar` |
| **report-library** | Building a report catalog with search/filter and a set-parameters-and-run flow | `FilteredDataTable`, `Modal`, `SeverityBadge` |
| **strategy-builder** (+ `field-formula`, `amx-governance`, `backtest-builder`, `strategy-onboarding`, `strategy-runs`) | Building a cell-based research/strategy workflow end to end — six cross-linked screens sharing one nav rail | `WorksheetGrid`, `GateRail`, `Stepper`, `EquityCurve`, `Histogram` |

**New tip:** If you want a blank canvas with just the shell (topbar, nav rail, status bar), use **`blank-workstation`** — it has zero hardcoded content, just the wrapper.

---

## Component reference

**All 109 components are documented in `docs/COMPONENT_API_REFERENCE.card.html`** (plus sub-cards for each family). Open that file in your browser to see live examples, prop defaults, and keyboard behavior.

Quick reference by category:

### **Core UI (64 components)**
Buttons, inputs, selects, checkboxes, toggles, badges, spinners, sliders, modals, drawers, tooltips, context menus, command palette, form helpers, layout primitives (Grid, Flex, Stack).

**Most common:** `Button`, `Input`, `Select`, `Modal`, `Drawer`, `DenseDataTable`, `Grid`, `Flex`.

### **Data & tables (17 components)**
Dense data grids, filtered tables, expandable rows, virtualized lists, pagination, bulk actions, entity summaries, metric cards, skeletons.

**Most common:** `DenseDataTable`, `FilteredDataTable`, `ExpandableDataTable`, `MetricCard`, `KeyValueGrid`.

### **Accounting (7 components)**
Ledger tables, GL trees, statements, journal entry forms, tax lots, reconciliation panels.

**Most common:** `LedgerTable`, `JournalEntryForm`, `AccountTree`.

### **Charts (9 components)**
Candlestick, equity curve, drawdown, histogram, depth profile, correlation heatmap, scatter, sparkline, chart wrapper.

**Most common:** `CandleChart`, `EquityCurve`, `ChartCard`.

### **Shell (3 components)**
Topbar, nav rail, status bar — the workstation frame.

### **Operations (7 components)**
The operator readiness/severity domain layer: `SeverityBadge`, `ReadinessPanel`, `GateRail`, `ValidationIssueList`, `EvidenceLink`, `TrustStrip`, `WorkspaceSection`. Encodes Ready/ReviewRequired/Blocked statuses, the gate pipeline (Ingest→Reconcile→Approve), validation issues, and evidence references.

**Most common:** `SeverityBadge`, `ReadinessPanel`, `GateRail`.

### **Feedback (2 components)**
`Toast` + `ToastProvider` — transient action confirmations, `aria-live` announced.

---

## Styling & tokens

### Global CSS entry point

Link Meridian's stylesheet in your HTML `<head>`:

```html
<link rel="stylesheet" href="../../Meridian/styles.css" />
```

This imports **all 331 design tokens** (colors, spacing, typography, elevation) and **component stylesheets**.

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
<DenseDataTable
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

### Show a modal with a form

```html
<ModalForm open={open} onClose={close} title="New Account" onSubmit={submit}>
  <ModalBody>
    <FormField label="Code" required>
      <Input value={code} onChange={setCode} />
    </FormField>
  </ModalBody>
  <ModalFooter>
    <Button variant="ghost" onClick={close}>Cancel</Button>
    <Button variant="primary" onClick={submit}>Create</Button>
  </ModalFooter>
</ModalForm>
```

**See:** `core/modal.card.html` for all overlay patterns.

### Display a metric card with a gauge

```html
<MetricCard
  label="Net liquidation"
  value="$2,431,442.59"
  delta="+$14,221"
  tone="success"
/>

<PanelSurface>
  <Eyebrow>Fill rate</Eyebrow>
  <Gauge value={78} label="30d" color="var(--green)" />
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
| Modal / Drawer | `Escape` | Close |
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
const { Button, Modal, Input } = window.MeridianDesignSystem_4f61be;

export function MyComponent() {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState('');

  return (
    <>
      <Button onClick={() => setOpen(true)}>Open</Button>
      <Modal open={open} onClose={() => setOpen(false)}>
        <Input value={value} onChange={(e) => setValue(e.target.value)} />
      </Modal>
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
3. **Read PATTERNS.md** — decision trees for choosing the right component
4. **Customize tokens** — override `--accent` and other tokens for your brand
5. **Build** — use the shell (topbar, nav rail, status bar) and compose with core components

---

## Questions?

- **Component API:** Open any `<Name>.card.html` in your browser for live props and examples
- **Patterns & architecture:** See `PATTERNS.md`
- **Visual foundations:** See `guidelines/VISUAL_FOUNDATIONS.md`
- **Icons & brand:** See `guidelines/ICONOGRAPHY.md`

**Happy building!**
