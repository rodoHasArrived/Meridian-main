# Meridian — Component States & Anti-Patterns

The cards show the happy path. This is the other 80%: what each major component does when data is
**absent, loading, broken, overflowing, or forbidden** — and the compositions that look reasonable
but drift from the system. When you build a screen, walk this list; a screen that only handles the
happy path is not done.

---

## The five states every data surface owes

Every surface that renders server data must have a defined answer for all five. Meridian ships a
component for each so you never render a raw blank:

| State | Use | Never |
|---|---|---|
| **Loading** | `Skeleton` / `SkeletonTable` (shape-preserving) | A spinner centered in an empty panel; a layout that jumps when data lands |
| **Empty (no data)** | `EmptyState` with an action | "No results" as bare gray text |
| **Empty (filtered out)** | `EmptyState` with a *Clear search* action — distinct copy from no-data | The same message as no-data; the user can't tell if they broke it or it's genuinely empty |
| **Error** | `StatusBanner tone="danger"` + retry; render nothing rather than stale rows | Silently showing the last-good data as if it were fresh |
| **Ready** | the component | — |

The Alerting template's toolbar switch demonstrates all five in situ — copy that pattern.

---

## Per-component states

### DenseDataTable / FilteredDataTable
- **Overflow:** cells are `white-space:nowrap` and clip. For long free text, either give the column
  a `width` and accept truncation, or opt that column into wrapping — don't let one column shove the
  layout wide. Right-aligned numerics never wrap.
- **Long tables:** auto-window past 500 rows (no config). Pinned columns stay put under horizontal
  scroll; a pinned column wider than the viewport defeats the purpose — pin identity columns only.
- **Multi-sort:** shift-click builds the stack; the header shows a rank number. Past ~3 keys it's
  unreadable — that's a sign the data wants a group-by, not a fourth sort key.
- **Zero columns visible** (all hidden via ColumnChooser): show the empty-columns hint, not a table
  with only a checkbox.

### AsyncCombobox
- **Loading / empty / error** are footer states inside the popover, never a blank list.
- **40k options:** the list is windowed; don't pre-flatten the whole set into the DOM.
- **No `minChars`** on a huge set will fire a query per keystroke on an empty string — set `minChars`.

### Charts
- **No data / single point:** guard before rendering — a candle chart of one bar is a bug, not a
  chart. Show `EmptyState`.
- **Crosshair off-plot:** `index` is `null`; `ChartTooltip` renders nothing. Don't pin a stale readout.
- **Synced cursors:** only meaningful for charts sharing an x-domain (same bars). Don't sync a
  histogram to a time series.

### Forms
- **Dirty state:** use `useDirtyState`; show an unsaved badge and guard navigation. Don't diff by
  hand in each screen.
- **Async validation:** show the `checking` state; keep submit disabled while any field is checking,
  not just while invalid.
- **Disabled by permission:** disable *and* explain (a `ReadOnlyBanner` or field hint) — a silently
  dead control reads as broken.

### Overlays (Dialog / Modal / Drawer)
- One at a time. Never stack a dialog over a dialog — promote to a wizard (`useWizard`) instead.
- Destructive confirms get a typed-confirmation gate (see Settings template), not just a red button.

---

## Anti-patterns (do → don't, and why)

**Formatting data by hand.**
✅ `<Timestamp value={t} />` · `<Delta value={-4.1} suffix="%" />`
❌ `new Date(t).toLocaleString()` · `` `${v > 0 ? "+" : ""}${v}%` ``
*Why:* local timezones and unsigned deltas are the two most common content-rule violations; the
primitives make them impossible.

**Hardcoding color on a fill.**
✅ `color: var(--text-on-accent)` on accent · `var(--text-on-fill)` on semantic fills
❌ `color: white` / `#fff`
*Why:* white fails AA the moment dark mode lightens the fill. The `white-on-fill` governance rule
now flags this in `components/`.

**Re-implementing a component's chrome.**
✅ `<Badge variant="live">LIVE</Badge>`
❌ a hand-built `.env-chip` with its own live/paper/fixture CSS
*Why:* the topbar carried a duplicate of Badge for months; both had to be fixed twice. One source.

**Hand-picking a z-index.**
✅ `z-index: var(--z-modal)`
❌ `z-index: 9999`
*Why:* the ladder in `elevation.css` is ordered so overlays can't collide; a magic number will.

**Toast for persistent state.**
✅ `NotificationCenter` (durable), `ReadOnlyBanner` (permission), `StatusBanner` (data health)
❌ a toast for "you're in read-only mode" or "3 alerts open"
*Why:* toasts vanish in 4s; permission and health are conditions, not events.

**Blocking on the happy path only.**
✅ handle all five data states
❌ render `rows.map(...)` with no empty/loading/error branch
*Why:* an operator hits the empty and error states on day one; a blank panel reads as a crash.

**Selecting by row index.**
✅ `useRowSelection(rows, r => r.id)`
❌ tracking `selectedIndex` across sorted/filtered/paged data
*Why:* index identity breaks the instant the data reorders; select-across-pages needs a key.
