# Workstation Template Blueprint

**Standard structure for all Meridian workstation templates — ensures consistency and rapid
development.** Reflects the structure actually used by every template in `templates/` today
(verified against the live folders and shell components, June 2026).

## Folder structure

```
templates/[workstation-name]/
├── [Workstation].dc.html   Entry point — a thin Design Component wrapper:
│                           <x-dc> with an @template comment, a <helmet> that loads
│                           ds-base.js, and a single <x-import> that mounts screen.jsx.
│                           It carries no UI of its own — edit screen.jsx for that.
├── screen.jsx              The actual workstation UI — one React component registered
│                           on window (e.g. window.DashboardWorkstationScreen) and read
│                           by the .dc.html's <x-import>. This is what you edit.
├── ds-base.js              Design-system loader (auto-scaffolded, don't hand-edit its
│                           `base` path unless you move the template to a new depth).
├── .thumbnail              Auto-generated preview thumbnail.
└── (optional) *-data.js    Large seed-data literals some screens keep in a sibling file
                            (e.g. basket-data.js, securities-data.js), loaded as a plain
                            <script> in the .dc.html's static <head> — BEFORE support.js —
                            so the data is guaranteed to exist before screen.jsx reads it.
                            Don't load these from inside <helmet> or via a second
                            <x-import>: neither guarantees execution order against the
                            screen's own module-level reads.
```

There is no `README.md` per template and no generic `data.json`/`logic.js` pair — that was an
earlier, never-adopted plan. Seed data lives directly in `screen.jsx` unless it's large enough
to warrant a sibling file as above.

## Required components (always)

### 1. WorkstationTopbar
```jsx
<WorkstationTopbar
  moduleLabel="Accounting"       // Name of the module
  environment="PAPER"            // PAPER | LIVE | FIXTURE
  clock="14:32:08 UTC"          // Real-time clock
  brandSrc="../../assets/brand/meridian-mark-light.svg"  // Logo
/>
```

### 2. NavRail (left sidebar)
```jsx
<NavRail
  activeId={currentSection}
  onSelect={(id) => { /* switch section, or window.location.href for a separate template */ }}
  sections={[
    { label: "Books", items: [
      { id: "ledger", label: "General Ledger", icon: "../../assets/icons/dashboard.svg" },
      { id: "journals", label: "Journals", icon: "../../assets/icons/data-operations.svg" },
    ]},
    { label: "Close", items: [
      { id: "reconcile", label: "Reconciliation", icon: "../../assets/icons/archive-health.svg" },
    ]},
  ]}
/>
```
Multi-screen suites (like Strategy Executor) share one `sections` list across templates and
route between them with `onSelect={(id) => { window.location.href = ROUTES[id]; }}`.

### 3. Main content area
```jsx
<main style={{ flex: 1, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>
  {/* Header */}
  <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
    <h1 style={{ font: "600 22px var(--font-display)", margin: 0, color: "var(--text-primary)" }}>Module name</h1>
    <Badge variant="paper" dot>PAPER</Badge>
    <div style={{ flex: 1 }} />
    <Button variant="primary" size="sm" onClick={...}>Primary action</Button>
  </div>

  {/* Optional: tabs, search + filters, cards/tables/forms, pagination past ~20 rows */}
</main>
```

### 4. StatusBar (footer)
```jsx
<StatusBar items={[
  { status: "ok", label: "Ledger", value: "balanced" },
  { label: "Last post", value: "JE-1060 · 14:09:55" },
  { label: "Period", value: "2026-Q2 open", push: true },  // push: true floats right
]} />
```

### 5. Modal (for create/edit)
```jsx
<Modal open={modalOpen} onClose={() => setModalOpen(false)}>
  <ModalHeader>New entry</ModalHeader>
  <ModalBody>
    <FormGrid cols={2}>
      <FormRow label="Field"><Input value={...} onChange={...} /></FormRow>
    </FormGrid>
  </ModalBody>
  <ModalFooter>
    <Button variant="primary" onClick={handleSave}>Save</Button>
    <Button variant="ghost" onClick={() => setModalOpen(false)}>Cancel</Button>
  </ModalFooter>
</Modal>
```
`ToastProvider` (if used) is a **self-contained, no-props, no-children** toast stack — mount it
as a sibling (`<ToastProvider />`) at the end of the tree, never as a wrapper around the screen;
it does not render `children`. Trigger toasts imperatively from anywhere via
`window.MeridianToast.success(title, detail)`.

## CSS injection pattern

Every screen injects its own scoped CSS once, the same way every component does:

```js
(function injectCss() {
  if (document.getElementById("module-css")) return;
  const el = document.createElement("style");
  el.id = "module-css";
  el.textContent = `.module-class { ... }`;
  document.head.appendChild(el);
})();
```

## Seed data pattern

Always provide 3–6 realistic examples, inline near the top of `screen.jsx`:

```js
const SEED_DATA = [
  { id: 1, date: "06-02", ref: "REF-001", amount: 1000, status: "posted" },
  { id: 2, date: "06-03", ref: "REF-002", amount: 2000, status: "draft" },
  { id: 3, date: "06-04", ref: "REF-003", amount: 3000, status: "posted" },
];
```

## State management pattern

Keep state at the screen's root component:

```js
const [data, setData] = useState(SEED_DATA);
const [currentTab, setCurrentTab] = useState("tab1");
const [modalOpen, setModalOpen] = useState(false);
const [editingItem, setEditingItem] = useState(null);
```

## Size & scale reference

Measured off the real shell components (`components/shell/*.jsx`) — these are the actual
rendered sizes, not a target:

- **Topbar height:** 48px (`WorkstationTopbar`, `min-height`)
- **NavRail width:** 224px / `14rem` (`NavRail`)
- **StatusBar height:** 28px (`StatusBar`, `min-height`)
- **Table row height:** 40px default (`DenseDataTable`), 32px in `FilteredDataTable`
- **Modal width:** 660px (max 92vw)
- **Padding:** 16px (main), 12–14px (cards), 8px (form rows)
- **Gap:** 12px (default), 8px (compact), 16px (loose)

## Component sizing — no universal T-shirt scale

Meridian does **not** have one xs–xl scale shared by every component; each control defines its
own sizes. Check the component's own `.d.ts` rather than assuming. The two actually-shared
conventions:

- **`Button`**: `size` is `"sm" | "default" | "lg" | "icon"` — 24px / 32px / 40px / 32²px.
- Most other sized controls (`SegmentedControl`, `ProgressBar`, `Slider`, `Skeleton`) use
  `"sm" | "md" | "lg"` instead — check before assuming Button's scale applies.

## Onboarding checklist

- [ ] Copy `templates/[name]/` folder into your project
- [ ] Update the `[Name].dc.html` entry point's `@template` name/description if you rename it
- [ ] Confirm `ds-base.js`'s `base` path still resolves to the design system at the new depth
- [ ] Populate `screen.jsx` with real data (start from the seed-data pattern above)
- [ ] Add 3–6 seed data rows
- [ ] Test dark mode (`data-theme="dark"` on `<html>`)
- [ ] Test brand variants (`data-brand="indigo" | "emerald" | "rose"`)
- [ ] Test density (`data-theme-density="compact" | "spacious"`)

**Typical time: 30 minutes from scaffold to working prototype.**
