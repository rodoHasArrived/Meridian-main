# Meridian UI — Component Documentation

All components live under `src/Meridian.Ui/dashboard/src/components/`.  
Primitive building blocks are in `components/ui/`; domain-assembled components are in `components/meridian/`.

---

## Design tokens

The workstation ships a single dark-mode-only theme defined in `src/styles/index.css`. All colour
values are Tailwind HSL tokens (`--background`, `--foreground`, etc.) plus a set of semantic state
tokens. Use these in any new component; never reach for raw hex.

The CSS component classes in `index.css` (`.operator-rail`, `.metric-tile`, etc.) previously used
hardcoded raw alias vars (`--bg`, `--fg-muted`, etc.). These aliases now derive from the canonical
Tailwind tokens, so a single change to e.g. `--background` propagates everywhere.

### Colour ramps

| Token | Raw value | Use when |
|---|---|---|
| `primary` / `primary-foreground` | cyan `hsl(198 72% 50%)` | actions, focus rings, active states |
| `secondary` / `secondary-foreground` | dark navy | secondary actions, panel fills |
| `muted` / `muted-foreground` | mid-grey | quiet text, subtitles, eyebrow labels |
| `success` | teal `hsl(158 66% 44%)` | positive deltas, healthy status |
| `warning` | amber `hsl(36 80% 58%)` | attention states |
| `danger` | rose `hsl(354 68% 62%)` | destructive actions, critical alerts |
| `paper` | soft blue `#60a5fa` | paper-trade / simulation mode |
| `live` | cyan (same as primary) | live-execution mode |
| `border` | `hsl(208 38% 22%)` | default border throughout |

### Semantic state CSS variables

Each state exposes three raw custom properties (not Tailwind tokens) for use in bespoke CSS rules:

```css
--state-{healthy|warn|danger|paper|research|muted|pending}-fg
--state-{...}-bd   /* border alpha */
--state-{...}-bg   /* background alpha */
```

### Typography

| Family | Variable | Usage |
|---|---|---|
| IBM Plex Sans | `font-sans` | body text, UI labels |
| IBM Plex Mono | `font-mono` | metric values, eyebrow labels, status chips |
| Space Grotesk | (hardcoded in rail CSS) | brand mark, rail title |

### Spacing & radius

| Token | Value | Common use |
|---|---|---|
| `--radius-xl` | `0.625rem` | large panels |
| `--radius-lg` | `0.5rem` | cards, rail sections |
| `--radius-md` | `0.375rem` | buttons, inputs |
| `--radius-sm` | `0.25rem` | chips |
| `--radius-xs` | `0.1875rem` | status badges |
| `--space-panel` | `1.5rem` | standard panel padding |

### Shadow levels

| Class | Token | Use |
|---|---|---|
| `shadow-workstation` | `--shadow-workstation` | top-level panels |
| `shadow-panel` | `--shadow-panel` | cards inside a panel |
| `shadow-float` | `--shadow-float` | floating elements (dialogs, popovers) |

---

## Utility component classes

These Tailwind `@layer components` classes are the structural vocabulary — apply them directly
instead of re-assembling the same token combinations.

| Class | Description |
|---|---|
| `panel-surface` | rounded-xl card with workstation shadow |
| `panel-surface-strong` | same but uses the stronger panel background |
| `data-grid-surface` | lighter panel for data tables |
| `glass-card` | rounded-lg card with panel shadow |
| `eyebrow-label` | 10px mono uppercase tracking label |
| `metric-tile` | fixed-layout tile for `MetricCard` |

---

## Security Master Master-Detail Pattern

Use this pattern for Security Master and security-detail workstation pages. It is the canonical
reference-workbench shape for dense instrument data, cash-flow/factor schedules, provider identity
evidence, overrides, and audit history.

### Required structure

| Layer | Required pattern |
|---|---|
| Command deck | Full-width `panel-surface-strong` summary with page title, concise operational description, and four compact status metrics. Metrics must come from the view model. |
| Master list | A search/filter panel using `DenseDataTable`, row selection, `aria-selected`, `aria-controls`, and `aria-expanded` when a row opens detail. Identifier columns must stay visible enough to compare CUSIP/ISIN/FIGI/ticker evidence. |
| Selected detail | A full-width selected-security detail page frame before the detail modules. It must show selected security ID, asset class, status badge, and a `ToolbarStrip` for Overview, Schedules, Controls, and Audit cues. |
| Overview modules | Use sectioned reference data groups and `EntitySummary` for identity, classification, issuance, valuation, ESG, operational, and override metadata. Hide irrelevant asset-class fields visually, but keep their underlying override state intact. |
| Schedule modules | Cash-flow, factor, corporate-action, conversion, redemption, lot, and trading-parameter schedules must render as dense tables or compact metadata grids, not loose card lists. |
| Audit modules | Provider identity conflicts, alias history, notes/comments, overrides, and audit history must remain attached to the selected security rather than sent to a separate workflow. |

### Interaction rules

- Search, selection, empty states, loading text, disabled reasons, and live-region copy belong in
  view models.
- Preserve keyboard selection on table rows with `Enter` and `Space`; do not rely on pointer-only
  row activation.
- Keep destructive or live-like actions gated. Import, bulk edit, export, and override actions need
  an explicit disabled reason or confirmation state until a real backing workflow exists.
- Prefer one dense detail route over separate disconnected tabs. Section navigation can be shown as
  a `ToolbarStrip`, but the selected security context must remain visible while users inspect
  schedules, controls, lots, conflicts, and audit evidence.
- Do not add mobile-specific Security Master surfaces. Responsive browser validation is allowed for
  the workstation route only.

### Meridian implementation anchor

The browser workstation implements this pattern at `/accounting/security-master` in
`src/Meridian.Ui/dashboard/src/screens/governance-screen.tsx`, with copy and summary state owned by
`src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.ts`.

---

## Button

**File:** `components/ui/button.tsx`

A polymorphic action element. Supports `asChild` to project button styles onto a child element
(e.g. a router `<Link>`) without a nested `<button>`.

### Props

| Prop | Type | Default | Description |
|---|---|---|---|
| `variant` | `"default" \| "secondary" \| "outline" \| "ghost" \| "destructive"` | `"default"` | Visual style |
| `size` | `"default" \| "sm"` | `"default"` | Height and padding scale |
| `asChild` | `boolean` | `false` | Merge classes onto the single child element instead of rendering `<button>` |
| `loading` | `boolean` | `false` | Shows inline spinner, sets `disabled`, adds `aria-busy` |
| `disabled` | `boolean` | — | Native disabled; reduces opacity and blocks interaction |
| `className` | `string` | — | Additional Tailwind classes |
| All native `<button>` attributes | — | — | Forwarded directly |

### Variants

| Variant | Background | Use when |
|---|---|---|
| `default` | `bg-primary` (cyan fill) | Primary action — one per view |
| `secondary` | `bg-secondary` | Secondary or supporting action |
| `outline` | Transparent + border | Tertiary action inline with content |
| `ghost` | Transparent, no border | Icon buttons, toolbar items, nav links |
| `destructive` | `bg-danger/10` with danger border | Irreversible destructive actions |

### Sizes

| Size | Min height | Padding | Font |
|---|---|---|---|
| `default` | `36px` | `px-4 py-2` | `text-sm` |
| `sm` | `32px` | `px-3 py-1.5` | `text-xs` |

### States

| State | Visual |
|---|---|
| Default | Variant fill + border |
| Hover | Slightly desaturated fill (85% opacity on default) |
| Focus-visible | 2px ring in `primary/40` |
| Disabled | 50% opacity, `cursor-not-allowed` |
| Loading | Spinner prepended to children; button auto-disabled; `aria-busy="true"` |

### Accessibility

- **Role:** `button`
- **Keyboard:** `Tab` to focus, `Enter` / `Space` to activate
- **Focus indicator:** `focus-visible:ring-2 focus-visible:ring-primary/40`
- Use `aria-label` when the button contains only an icon

### Usage examples

```tsx
import { Button } from "@/components/ui/button";

// Primary action
<Button onClick={submit}>Place order</Button>

// Destructive confirm
<Button variant="destructive" size="sm" onClick={cancel}>Cancel order</Button>

// Router link styled as button (asChild pattern)
<Button variant="outline" asChild>
  <Link to="/settings">Settings</Link>
</Button>

// Icon-only ghost button (always add aria-label)
<Button variant="ghost" size="sm" aria-label="Refresh data">
  <RefreshCw className="h-4 w-4" />
</Button>
```

### Do's and don'ts

| Do | Don't |
|---|---|
| One `default` button per logical action group | Stack multiple `default` buttons side-by-side |
| Use `destructive` only for actions that cannot be undone | Use `destructive` for navigation or filtering |
| Use `ghost` for toolbar / icon-only buttons | Omit `aria-label` on icon-only ghost buttons |
| Use `asChild` to wrap router links | Nest `<button>` inside `<button>` |

---

## Badge

**File:** `components/ui/badge.tsx`

A compact status or category label. Renders as monospace uppercase text with an optional leading
dot indicator. Designed for inline use inside table cells, card headers, and status chips.

### Props

| Prop | Type | Default | Description |
|---|---|---|---|
| `variant` | see table below | `"default"` | Semantic colour |
| `dot` | `boolean` | `false` | Prepend a filled circle in the current text colour |
| `className` | `string` | — | Additional Tailwind classes |
| All native `<span>` attributes | — | — | Forwarded directly |

### Variants

| Variant | Colour family | Use when |
|---|---|---|
| `default` | Cyan (primary) | Generic informational label |
| `outline` | Neutral grey | Inactive, archived, or secondary category |
| `success` | Teal | Healthy, confirmed, executed |
| `warning` | Amber | Pending, review needed, partial fill |
| `danger` | Rose | Failed, rejected, halted, critical |
| `paper` | Soft blue | Paper-trade / simulation mode |
| `live` | Cyan | Live execution mode |
| `research` | Primary/muted blue | Research or back-test context |

### States

Badge is a static display element — it has no interactive states. Do not add `onClick` or make
it keyboard-focusable unless wrapping it in a button or link.

### Accessibility

- **Role:** implicit `none` (decorative label)
- For status that screen readers must announce, pair with an `aria-label` on the containing element
  or use `<span role="status">` for live-region updates

### Usage examples

```tsx
import { Badge } from "@/components/ui/badge";

// Static mode indicator
<Badge variant="live" dot>Live</Badge>
<Badge variant="paper" dot>Paper</Badge>

// Order status
<Badge variant="success">Filled</Badge>
<Badge variant="warning">Partial</Badge>
<Badge variant="danger">Rejected</Badge>

// Neutral category
<Badge variant="outline">Equities</Badge>
```

### Do's and don'ts

| Do | Don't |
|---|---|
| Match `variant` to the semantic tone of the state | Mix variants for purely aesthetic reasons |
| Use `dot` when the badge represents a live / active state | Use `dot` on static category labels |
| Keep badge text to 1–3 words | Use full sentences inside a badge |

---

## Card

**File:** `components/ui/card.tsx`

A compound component that provides a rounded, bordered panel container. Composed of five
sub-components that can be assembled freely.

### Sub-components

| Component | Element | Purpose |
|---|---|---|
| `Card` | `<div>` | Outer container: `rounded-lg border bg-card shadow-panel` |
| `CardHeader` | `<div>` | Top section with `p-5` padding and `space-y-1.5` |
| `CardTitle` | `<h3>` | 15px semibold heading |
| `CardDescription` | `<p>` | 14px muted secondary text |
| `CardContent` | `<div>` | Content area: `p-5 pt-0` |
| `CardFooter` | `<div>` | Footer row: `px-5 pb-5 pt-0`, `flex items-center gap-2` |

All sub-components accept `className` and forward `ref`.

### Usage example

```tsx
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from "@/components/ui/card";

<Card>
  <CardHeader>
    <CardTitle>Open positions</CardTitle>
    <CardDescription>6 active positions across 3 strategies</CardDescription>
  </CardHeader>
  <CardContent>
    {/* table or data grid goes here */}
  </CardContent>
</Card>
```

### Do's and don'ts

| Do | Don't |
|---|---|
| Use `CardHeader` + `CardTitle` for a consistent heading rhythm | Put a raw heading directly inside `Card` |
| Use `CardContent` for the primary body; omit it only when the card has no padding needs | Skip to `CardDescription` without a `CardTitle` |
| Compose freely — not all sub-components are required | Add extra padding inside `CardContent` (it already has `p-5 pt-0`) |

---

## MetricCard

**File:** `components/meridian/metric-card.tsx`  
**View-model:** `components/meridian/metric-card.view-model.ts`

A domain-specific tile for displaying a single numeric metric with an optional delta and semantic
tone. Used across the operator workstation's summary rows.

### Props — `MetricSnapshot`

This component accepts a `MetricSnapshot` object directly as its props spread:

| Field | Type | Description |
|---|---|---|
| `id` | `string` (optional) | Stable identifier — falls back to a slug of `label` |
| `label` | `string` | Short metric name, e.g. `"NAV"`, `"Daily P&L"` |
| `value` | `string` | Formatted display value, e.g. `"$1,240,500"` |
| `delta` | `string` | Change indicator, e.g. `"▲ 2.4%"` — pass `""` for no delta |
| `tone` | `"default" \| "success" \| "warning" \| "danger"` | Semantic colour applied to value and delta |

### Tones

| Tone | Text colour | Typical use |
|---|---|---|
| `default` | `text-foreground` | Neutral value, no directional signal |
| `success` | `text-success` (teal) | Positive P&L, healthy risk metric |
| `warning` | `text-warning` (amber) | Approaching limit, mixed signal |
| `danger` | `text-danger` (rose) | Loss, limit breach, critical condition |

### Accessibility

The view-model builds accessible IDs and a structured `aria-label` automatically:

- The outer `<div>` carries `role="group"` with `aria-label` composed from label, value, delta,
  and tone (`"Daily P&L metric. $−4,200. Change down 0.3%. Status critical."`).
- `aria-describedby` points to the delta element (when present) or the value element.
- Delta text containing `▲▼△▽` and `−` is normalised to plain speech by the view-model.

### Usage example

```tsx
import { MetricCard } from "@/components/meridian/metric-card";
import type { MetricSnapshot } from "@/types";

const metrics: MetricSnapshot[] = [
  { label: "NAV",       value: "$1,240,500", delta: "▲ 2.1%",  tone: "success" },
  { label: "Daily P&L", value: "−$4,200",   delta: "▼ 0.3%",  tone: "danger"  },
  { label: "Margin",    value: "31.4%",      delta: "",        tone: "warning" },
];

<div className="grid grid-cols-3 gap-3">
  {metrics.map(m => <MetricCard key={m.label} {...m} />)}
</div>
```

### Do's and don'ts

| Do | Don't |
|---|---|
| Set `tone` to match the directional signal of the value | Leave `tone` as `"default"` when the value is a loss |
| Format `value` before passing — this component does no number formatting | Pass raw numbers; format with `Intl.NumberFormat` upstream |
| Use `▲` / `▼` in `delta` for change direction | Omit the arrow — the aria normaliser and visual convention both rely on it |
| Pass a stable `id` when multiple `MetricCard`s share the same `label` | Depend on label-derived IDs in lists where labels might duplicate |

---

## Dialog

**File:** `components/ui/dialog.tsx`  
**Interaction view-model:** `components/ui/dialog.view-model.ts`

A controlled modal overlay composed of five sub-components. The interaction view-model handles
backdrop click-to-dismiss and focus management; consumers only manage the `open` boolean.

### Sub-components

| Component | Element | Purpose |
|---|---|---|
| `Dialog` | `<div>` (overlay) | Backdrop: `fixed inset-0 z-50 bg-background/70` — renders `null` when closed |
| `DialogContent` | `<div role="dialog">` | Centred panel: max-width 512px, `shadow-float`, `backdrop-blur-sm`, `position: relative` |
| `DialogClose` | `<button>` | Absolute-positioned × button (top-right). Auto-wired to close via context — no `onClick` needed. |
| `DialogHeader` | `<div>` | `mb-4 space-y-2` — groups title + description |
| `DialogTitle` | `<h2>` | 18px semibold heading |
| `DialogDescription` | `<p>` | 14px muted description |
| `DialogFooter` | `<div>` | `mt-4 flex items-center justify-end gap-2` — action row |

### Props — `Dialog`

| Prop | Type | Description |
|---|---|---|
| `open` | `boolean` | Controls visibility — caller owns this state |
| `onOpenChange` | `(open: boolean) => void` (optional) | Called with `false` when the backdrop is clicked |
| `children` | `ReactNode` | Should include a `DialogContent` |

### Accessibility

- `DialogContent` carries `role="dialog"` and `aria-modal="true"`
- Add `aria-labelledby` pointing to the `DialogTitle` id for screen-reader context
- The view-model handles a full focus trap (Tab cycles within the dialog, Shift+Tab reverses),
  Escape to close, initial focus on open, and focus restoration to the trigger on close

### Usage example

```tsx
import { useState } from "react";
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";

function ConfirmCancelDialog({ onConfirm }: { onConfirm: () => void }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button variant="destructive" size="sm" onClick={() => setOpen(true)}>
        Cancel order
      </Button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent aria-labelledby="cancel-dialog-title">
          <DialogClose />
          <DialogHeader>
            <DialogTitle id="cancel-dialog-title">Cancel this order?</DialogTitle>
            <DialogDescription>
              This will submit a cancel request to the venue. Partial fills are retained.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>Keep order</Button>
            <Button variant="destructive" onClick={() => { onConfirm(); setOpen(false); }}>
              Cancel order
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
```

### Do's and don'ts

| Do | Don't |
|---|---|
| Always provide `aria-labelledby` on `DialogContent` | Open a dialog without a visible title |
| Keep the primary action right-aligned in the footer | Put the destructive action on the left |
| Use `onOpenChange` to close on backdrop click | Remove `onOpenChange` — users expect click-to-dismiss |
| One dialog open at a time | Stack dialogs |

---

## WorkspaceNav (operator rail)

**File:** `components/meridian/workspace-nav.tsx`

The primary left-rail navigation rendered in every operator workspace view. Reads the current
URL via `react-router-dom` and marks the matching workspace item as active.

### Architecture

The nav is driven by a view-model (`workspace-nav.view-model.ts`) and uses CSS classes defined in
`styles/index.css` rather than Tailwind utilities. This keeps the detailed visual logic out of JSX.

Key CSS classes:

| Class | Description |
|---|---|
| `operator-rail` | Outer sidebar container |
| `operator-nav-item` | Individual nav link (grid: icon / label-stack / status-chip) |
| `operator-nav-item.active` | Active state: cyan left-border inset + tinted background |
| `operator-nav-status` | Compact mono status chip (9px) |
| `operator-nav-status-{tone}` | Tone modifier: `live`, `review`, `paper`, `preview`, `setup` |

### Workspace keys and icons

| Key | Icon (lucide) | Description |
|---|---|---|
| `trading` | `RadioTower` | Order entry and execution |
| `portfolio` | `WalletCards` | Holdings, P&L, exposure |
| `accounting` | `Landmark` | Ledger and reconciliation |
| `reporting` | `FileCheck2` | Governed reports and approval |
| `strategy` | `FlaskConical` | Backtesting and paper validation |
| `data` | `DatabaseZap` | Market data ingestion and feeds |
| `settings` | `Settings` | Configuration |

### Status tones

Each workspace item exposes a `statusTone` field rendered as an `operator-nav-status-{tone}` badge:

| Tone | Colour | When to use |
|---|---|---|
| `live` | Teal | Active, connected, executing |
| `review` | Cyan | Under review, pending approval |
| `paper` | Lavender | Paper-trade / simulation |
| `preview` | Amber | Preview mode, not yet active |
| `setup` | Grey | Not configured |

### Accessibility

- The `<aside>` carries `aria-label` combining brand title and subtitle
- The `<nav>` carries `aria-label="Workspaces"`
- Each `<Link>` carries `aria-current="page"` when active and `aria-label` with name + status

---

## Input

**File:** `components/ui/input.tsx`

A text input wrapper that matches the workstation's token system. Supports an optional leading or
trailing icon slot (pass any 16×16 SVG or lucide icon) and an `error` state that shifts the ring
and border to `danger`.

### Props

| Prop | Type | Default | Description |
|---|---|---|---|
| `error` | `boolean` | `false` | Applies danger border/ring and sets `aria-invalid` |
| `leadingIcon` | `ReactNode` | — | Icon rendered inside the left edge (pointer-events disabled) |
| `trailingIcon` | `ReactNode` | — | Icon rendered inside the right edge (pointer-events disabled) |
| `type` | `string` | `"text"` | Native input type |
| All native `<input>` attributes | — | — | Forwarded directly |

### States

| State | Visual |
|---|---|
| Default | `border-border/80`, `bg-secondary/40` |
| Hover | `border-border` |
| Focus-visible | 2px ring `primary/40`, `border-primary/60` |
| Error | `border-danger/60`, 2px ring `danger/40`, `aria-invalid="true"` |
| Disabled | 50% opacity, `cursor-not-allowed` |

### Usage examples

```tsx
import { Input } from "@/components/ui/input";
import { Search } from "lucide-react";

// Plain
<Input placeholder="Enter symbol…" />

// With leading icon
<Input
  leadingIcon={<Search className="h-4 w-4" />}
  placeholder="Search instruments…"
/>

// Error state (pair with visible error message)
<Input
  error
  value={symbol}
  onChange={e => setSymbol(e.target.value)}
  aria-describedby="symbol-error"
/>
<p id="symbol-error" className="mt-1 text-xs text-danger">Symbol not found</p>
```

### Do's and don'ts

| Do | Don't |
|---|---|
| Always pair `error` with a visible error message | Use `error` without explaining what is wrong |
| Use `leadingIcon` for search and filter inputs | Use `trailingIcon` for interactive controls — it's pointer-events disabled |

---

## Select

**File:** `components/ui/select.tsx`

A styled wrapper around the native `<select>` element. Uses a custom chevron overlay so the
control matches `Input` visually. Falls back gracefully everywhere the native select is accessible.

### Props

| Prop | Type | Default | Description |
|---|---|---|---|
| `error` | `boolean` | `false` | Applies danger border/ring and sets `aria-invalid` |
| `placeholder` | `string` | — | Renders a disabled first `<option>` as placeholder text |
| All native `<select>` attributes | — | — | Forwarded directly |

### Usage example

```tsx
import { Select } from "@/components/ui/select";

<Select placeholder="Select venue…" value={venue} onChange={e => setVenue(e.target.value)}>
  <option value="XNAS">NASDAQ</option>
  <option value="XNYS">NYSE</option>
  <option value="SMART">IBKR Smart</option>
</Select>
```

### Do's and don'ts

| Do | Don't |
|---|---|
| Use `placeholder` for unset state | Pre-select the first real option as a workaround |
| Group related options with `<optgroup>` for long lists | Use this for multi-select — use checkboxes instead |

---

## Tooltip

**File:** `components/ui/tooltip.tsx`

A CSS-only hover/focus-within tooltip implemented with `group` and opacity transitions. No portal,
no JS — works for the workstation's controlled viewport. Renders as `role="tooltip"` for assistive
technology.

### Props — `Tooltip`

| Prop | Type | Default | Description |
|---|---|---|---|
| `content` | `ReactNode` | — | Tooltip body; keep to one short phrase |
| `side` | `"top" \| "bottom" \| "left" \| "right"` | `"top"` | Which side of the trigger the tooltip appears on |
| `children` | `ReactNode` | — | The trigger element |

### States

Tooltip is visible on `:hover` and `:focus-within` of the wrapper. Both trigger the CSS `opacity-0 → opacity-100` transition.

### Accessibility

- The tooltip `<span>` carries `role="tooltip"` 
- Wire `aria-describedby` on the trigger to the tooltip's `id` if the tooltip carries information
  that is not otherwise available to screen readers

### Usage examples

```tsx
import { Tooltip } from "@/components/ui/tooltip";
import { Button } from "@/components/ui/button";
import { RefreshCw } from "lucide-react";

// Icon button with label
<Tooltip content="Refresh market data">
  <Button variant="ghost" size="sm" aria-label="Refresh market data">
    <RefreshCw className="h-4 w-4" />
  </Button>
</Tooltip>

// Different placement
<Tooltip content="Opens in execution venue" side="right">
  <span className="cursor-help text-muted-foreground underline decoration-dotted">
    Smart routing
  </span>
</Tooltip>
```

### Do's and don'ts

| Do | Don't |
|---|---|
| Use for icon-only buttons that need a label | Use for interactive content (links, buttons inside the tooltip) |
| Keep content to a short phrase (≤6 words) | Use as a substitute for inline helper text on forms |
| Use `side` to avoid clipping at viewport edges | Leave the default if it clips — change `side` |

---

## Component inventory

| Component | Path | New in this revision | Variants | States | Test |
|---|---|---|---|---|---|
| Button | `ui/button.tsx` | loading state | default, secondary, outline, ghost, destructive | default, hover, focus, disabled, loading | — |
| Badge | `ui/badge.tsx` | — | default, outline, success, warning, danger, paper, live, research | static | — |
| Card | `ui/card.tsx` | CardFooter | — | static | — |
| Input | `ui/input.tsx` | ✨ new | — | default, hover, focus, error, disabled | — |
| Select | `ui/select.tsx` | ✨ new | — | default, hover, focus, error, disabled | — |
| Tooltip | `ui/tooltip.tsx` | ✨ new | top, bottom, left, right | idle, visible | — |
| MetricCard | `meridian/metric-card.tsx` | — | — | default, success, warning, danger | ✅ |
| Dialog | `ui/dialog.tsx` | DialogClose, DialogFooter | — | open, closed | ✅ |
| WorkspaceNav | `meridian/workspace-nav.tsx` | — | — | default, active | ✅ |
| WorkspaceHeader | `meridian/workspace-header.tsx` | — | — | — | ✅ |
| CommandPalette | `meridian/command-palette.tsx` | — | — | open, closed | ✅ |
