# Meridian Dashboard — React UI Kit

Minimal JSX kit that mirrors the live dashboard components. Load in this order:

```html
<link rel="stylesheet" href="../../colors_and_type.css">
<script src="https://unpkg.com/react@18.3.1/umd/react.development.js" integrity="sha384-hD6/rw4ppMLGNu3tX5cjIb+uRZ7UkRJ6BPkLpg4hAu/6onKUg4lLsHAs9EBPT82L" crossorigin="anonymous"></script>
<script src="https://unpkg.com/react-dom@18.3.1/umd/react-dom.development.js" integrity="sha384-u6aeetuaXnQ38mYT8rp6sbXaQe3NL9t+IBXmnYxwkUI2Hw4bsp2Wvmx4yRQF1uAm" crossorigin="anonymous"></script>
<script src="https://unpkg.com/@babel/standalone@7.29.0/babel.min.js" integrity="sha384-m08KidiNqLdpJqLq95G/LEi8Qvjl/xUYll3QILypMoQ65QorJ9Lvtp2RXYGBFj1y" crossorigin="anonymous"></script>
<script type="text/babel" src="ui_kits/dashboard/components.jsx"></script>
<script type="text/babel" src="my-page.jsx"></script>
```

## Components

| Name | Purpose |
|---|---|
| `PanelSurface` | Top‑level panel (navy card + inset highlight + soft drop). `strong` for header/sidebar. |
| `Eyebrow` | 10px uppercase letter‑spaced label. |
| `Button` | `variant`: primary / secondary / outline / ghost / destructive. `size`: md / sm. |
| `Badge` | `tone`: live / paper / research / success / warning / danger / outline. `dot` for status dot. |
| `MetricCard` | KPI card. `label`, `value`, `delta`, `tone`. |
| `Input` | Text input with eyebrow label. |
| `StatusBanner` | Horizontal banner. `tone`: success / warning / danger. |
| `NavItem` | Sidebar row with optional `icon`, `label`, `status`, `active`. |
| `DataTable` | Columns array + rows array (each cell can be `{ value, color }`). |
| `WorkstationShell` | Masthead + left rail + content slot for image-inspired operator pages. |
| `ToolbarStrip` | Compact one-line filter/action strip. |
| `DenseDataTable` | High-row-count table with sticky headers, mono cells, selected-row support. |
| `EntitySummary` | Canonical identifier/detail grid for Security Master, portfolio, custody, and reporting records. |

All components are exported to `window` via `Object.assign(window, {...})`.

## Workstation pattern

The uploaded reference images favor a full workstation structure rather than standalone cards:

```jsx
<WorkstationShell
  activeNav="Trading"
  nav={[
    { label: "Trading", status: "LIVE" },
    { label: "Portfolio", status: "OK" },
    { label: "Accounting", status: "OBS" },
    { label: "Reporting", status: "12" },
    { label: "Strategy", status: "RUN" },
    { label: "Data", status: "OK" },
    { label: "Settings", status: "" },
  ]}>
  <ToolbarStrip items={[{ label: "Account: All" }, { label: "Date: 26 Apr 26" }, { label: "Status: Pending", active: true }]} />
  <DenseDataTable columns={[{ label: "Symbol" }, { label: "Qty", align: "right" }]} rows={[["AAPL", "100"]]} />
</WorkstationShell>
```

Use this pattern for operator pages that need global context, compact filters, and selected-record
evidence. Use the uploaded images as structure and density inspiration only; keep Meridian labels,
colors, and brand assets.
