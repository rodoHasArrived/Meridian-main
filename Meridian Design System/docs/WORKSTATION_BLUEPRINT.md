# Meridian Workstation Template Blueprint v2.0

**Production structure for Meridian workstation templates.** This blueprint preserves the
repository conventions used by the live templates while aligning new screens to Meridian’s
**Programmed Institutionalism** design system: one dominant work plane, a controlled
paper–plate–well material hierarchy, explicit data states, traceable evidence, and restrained
brand expression.

> **Meridian should organize complexity before it decorates it.**

Folder anatomy and the entry contract: `templates/README.md`. Component choice and decision
trees: `guidelines/PATTERNS.md`. Assembly recipes: `docs/COOKBOOK.md`. This document owns the
*composition* law — archetype, work plane, surfaces, data states, and the quality gate.

---

## 1. Workstation design contract

Every Meridian workstation must satisfy these rules before component-level styling begins.

1. **Choose one workspace archetype.** Use Monitor, Analyze, Inspect, Compare, Reconcile, or
   Compose. Do not begin with a generic dashboard grid.
2. **Establish one dominant work plane.** Secondary regions support the primary task; they do
   not receive equal visual weight by default.
3. **Use the material hierarchy intentionally.** Paper is the application canvas, Plate is the
   primary work region, Well contains dense analysis, Rail contains navigation or context, and
   Overlay is reserved for transient UI.
4. **Use one primary action per operational region.** Primary actions resolve to graphite or
   deep slate—not Meridian terracotta.
5. **Make important data trustworthy at a glance.** State, source, freshness, and as-of time
   must be available without leaving the workflow.
6. **Separate brand color from semantic color.** Terracotta is a controlled Meridian signal;
   it does not mean loss, error, warning, or destruction.
7. **Use sentence case for working UI.** Uppercase is reserved for environment labels,
   compact indices, and editorial metadata.
8. **Reduce containers before reducing type.** Create density through alignment, shared
   geometry, and progressive disclosure—not through miniature typography or a card around
   every value.
9. **Use Meridian primitives only when they carry meaning.** Datum, Field, Trace, Plate, and
   Capsule are structural tools, not decoration.
10. **Preserve operability.** Every primary workflow must be keyboard accessible, readable at
    200% zoom, and understandable without color or motion.

---

## 2. Select the workspace archetype first

Declare the archetype in the screen comment and root class before composing the UI.

| Archetype | Primary purpose | Default composition |
|---|---|---|
| **Monitor** | Scan status and detect change | Summary strip, dominant operational plane, narrow alert/freshness rail |
| **Analyze** | Manipulate assumptions and inspect patterns | 8-column analytical well, 4-column controls/scenario plane, lower comparison band |
| **Inspect** | Understand one entity deeply | Stable identity header, ordered detail sequence, persistent evidence region |
| **Compare** | Evaluate standardized peers | Repeated Plates with shared scale, label order, padding, and annotation position |
| **Reconcile** | Resolve exceptions and confirm state | Table-first work plane, queue or filters, selected-record evidence region, explicit resolution action |
| **Compose** | Arrange research or workspace modules | Stable application core with snapping, saveable Capsules |

A rectangular panel is not automatically a Capsule. A Capsule is movable or replaceable,
has a defined data contract, predictable controls, saved state, and standardized dimensions.

---

## 3. Folder structure

Folder anatomy, the entry contract, and the seed-data load-order rule live in one place —
**`templates/README.md`** — because that is the file someone copying a template opens first.
This blueprint does not restate them.

---

## 4. Entry-point contract

The `.dc.html` file remains intentionally thin.

- Keep the `@template` name and description accurate.
- Load `ds-base.js` from `<helmet>`.
- Load any large seed-data script in the static `<head>` before support.js.
- Mount exactly one screen component through `<x-import>`.
- Do not place navigation, headers, dialogs, or workstation markup in `.dc.html`.
- When copying a template to a different directory depth, verify `ds-base.js` before editing
  application code.

---

## 5. Required shell anatomy

`WorkstationShell` **is** the shell. It owns the frame, renders the `<main>` landmark, and
crash-isolates the work plane and evidence rail so a failing panel never takes the chrome down.
Never hand-roll the flex scaffolding, a second `<main>`, or local shell dimensions.

```text
WorkstationShell
├── topbar      WorkstationTopbar
├── nav         NavRail
├── children    the dominant work plane (scrolls here, never on the body)
├── aside       evidence rail (optional)
└── statusBar   StatusBar
```

### Reference screen skeleton

```jsx
const { useMemo, useState } = React;
const {
  WorkstationShell, WorkstationTopbar, NavRail, StatusBar, Badge, Button, ToastProvider,
} = window.MeridianDesignSystem_205d17;

function AccountingWorkstationScreen() {
  const [currentSection, setCurrentSection] = useState("ledger");
  const [data, setData] = useState(SEED_DATA);
  const [selectedId, setSelectedId] = useState(SEED_DATA[0]?.id ?? null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingItem, setEditingItem] = useState(null);

  const selectedItem = useMemo(
    () => data.find((item) => item.id === selectedId) ?? null,
    [data, selectedId]
  );

  return (
    <React.Fragment>
      <WorkstationShell
        height="100vh"
        archetype="reconcile"
        labelledBy="workspace-title"
        topbar={
          <WorkstationTopbar
            moduleLabel="Accounting"
            environment="PAPER"
            clock="14:32:08 UTC"
            brandSrc="../../assets/brand/meridian-mark-light.svg"
          />
        }
        nav={<NavRail activeId={currentSection} onSelect={setCurrentSection} sections={NAV_SECTIONS} />}
        aside={<EvidenceRail item={selectedItem} />}
        asideLabel="Selected journal entry evidence"
        statusBar={
          <StatusBar
            items={[
              { status: "ok", label: "Ledger", value: "balanced" },
              { label: "Exceptions", value: "3 unresolved" },
              { label: "Last post", value: "JE-1060 · 14:09:55 UTC" },
              { label: "Period", value: "2026-Q2 open", push: true },
            ]}
          />
        }
      >
        <div className="acct-workspace">
          <header className="acct-workspace__header">
            <div>
              <p className="acct-workspace__eyebrow">Accounting · Reconcile</p>
              <h1 id="workspace-title">General ledger</h1>
              <p className="acct-workspace__context">
                North America books · As of 2026-06-30 14:32 UTC · 6 sources
              </p>
            </div>
            <div className="acct-workspace__actions">
              <Badge variant="paper" dot>2026-Q2 open</Badge>
              <Button
                variant="primary"
                size="sm"
                onClick={() => { setEditingItem(null); setDialogOpen(true); }}
              >
                New journal entry
              </Button>
            </div>
          </header>

          {/* Dominant work plane: DataGrid / chart */}
        </div>
      </WorkstationShell>

      <EntryDialog
        open={dialogOpen}
        editingItem={editingItem}
        onClose={() => setDialogOpen(false)}
        onSave={(nextItem) => {
          setData((current) => {
            const exists = current.some((item) => item.id === nextItem.id);
            return exists
              ? current.map((item) => (item.id === nextItem.id ? { ...item, ...nextItem } : item))
              : [nextItem, ...current];
          });
          setSelectedId(nextItem.id);
          setDialogOpen(false);
          window.MeridianToast.success(editingItem ? "Entry updated" : "Entry created", nextItem.id);
        }}
      />
      <ToastProvider />
    </React.Fragment>
  );
}

window.AccountingWorkstationScreen = AccountingWorkstationScreen;
```

`height="100vh"` (the default) fills the page; pass `height="100%"` when the shell is embedded in
a sized container. Declare the archetype on the shell (`archetype`) and name the main region from its own
`h1` (`labelledBy`).

Use the full or light Meridian mark once in the application shell. Do not repeat the wordmark
inside panel headers, empty states, dialogs, or charts. Use the symbol only where a compact
brand anchor is genuinely needed.

---

## 6. WorkstationTopbar

```jsx
<WorkstationTopbar
  moduleLabel="Accounting"       // Product module, in sentence case
  environment="PAPER"            // PAPER | LIVE | FIXTURE; compact metadata may be uppercase
  clock="14:32:08 UTC"           // Real-time clock
  brandSrc="../../assets/brand/meridian-mark-light.svg"
/>
```

### Rules

- The topbar carries global identity and environment; do not duplicate the environment badge
  in every region.
- Use one brand mark in the shell. A full wordmark is not a decorative panel element.
- Keep timestamps explicit and include `UTC` where the product operates across jurisdictions.
- Environment styling must not reuse semantic warning or error colors.

---

## 7. NavRail

```jsx
const NAV_SECTIONS = [
  {
    label: "Books",
    items: [
      {
        id: "ledger",
        label: "General ledger",
        icon: "../../assets/icons/dashboard.svg",
      },
      {
        id: "journals",
        label: "Journals",
        icon: "../../assets/icons/data-operations.svg",
      },
    ],
  },
  {
    label: "Close",
    items: [
      {
        id: "reconcile",
        label: "Reconciliation",
        icon: "../../assets/icons/archive-health.svg",
      },
    ],
  },
];

<NavRail
  activeId={currentSection}
  onSelect={(id) => setCurrentSection(id)}
  sections={NAV_SECTIONS}
/>
```

Multi-screen suites share one `sections` definition and route through a single route map:

```jsx
const ROUTES = {
  ledger: "../ledger/Ledger.dc.html",
  journals: "../journals/Journals.dc.html",
  reconcile: "../reconciliation/Reconciliation.dc.html",
};

<NavRail
  activeId={currentSection}
  sections={NAV_SECTIONS}
  onSelect={(id) => {
    const route = ROUTES[id];
    if (route) window.location.href = route;
  }}
/>
```

### Rules

- Navigation labels use sentence case.
- Group by user intent or workflow, not by internal service ownership.
- The active item uses the Meridian **Datum** language: a persistent edge or rail, a low-opacity
  selection surface, and stronger text. Do not use terracotta as the default active state.
- Keep navigation stable across templates in the same suite.

---

## 8. Main workspace anatomy

The main region contains four layers in a predictable order:

1. **Workspace header** — title, context, as-of time, data coverage, and the region’s primary
   action.
2. **Optional control band** — tabs, saved views, search, filters, comparison controls, or
   scenario inputs.
3. **Dominant work plane** — the table, chart, relationship view, calendar, or composition
   surface where the core task happens.
4. **Secondary evidence region** — provenance, selected-record detail, audit trace, alerts, or
   configuration. This can be an inline rail, collapsible drawer, or lower band depending on
   the archetype.

```jsx
{/* inside <WorkstationShell labelledBy="workspace-title"> — the shell renders <main> */}
<div className="mdn-workspace">
  <header className="mdn-workspace__header">
    <div className="mdn-workspace__heading">
      <p className="mdn-workspace__eyebrow">Data operations · Inspect</p>
      <h1 id="workspace-title">Security master</h1>
      <p className="mdn-workspace__context">
        US Treasury 4.250% 2034 · As of 2026-06-30 14:32 UTC · Confirmed
      </p>
    </div>

    <div className="mdn-workspace__actions">
      <Button variant="ghost" size="sm">Export</Button>
      <Button variant="primary" size="sm">Save changes</Button>
    </div>
  </header>

  <section className="mdn-work-plane">
    {/* Dominant work surface */}
  </section>
</div>
{/* Evidence goes in the shell's `aside` slot, not a second hand-rolled rail */}
```

### Header rules

- Use one `h1` per screen.
- Workspace titles use sentence case and should normally render at 22/28 or 28/34 depending on
  hierarchy.
- Put entity, portfolio, book, period, as-of time, and coverage in the context line rather than
  creating separate KPI cards.
- Use one primary action in the header. Other actions are secondary or tertiary.
- Do not place the same environment, title, or status in both the topbar and workspace header
  unless the repetition materially prevents an error.

---

## 9. Meridian surface hierarchy

Use semantic surfaces rather than a universal card component.

| Surface | Purpose | Typical treatment |
|---|---|---|
| **Paper** | Application canvas | Warm neutral background; no border |
| **Plate** | Dominant or standardized workspace region | Continuous light plane; structure through spacing and rules |
| **Well** | Table, chart, matrix, or other analytical surface | Near-white or subtly recessed; minimal internal framing |
| **Rail** | Navigation, filters, queue, or context | Slightly darker neutral; stable relationship to the work plane |
| **Overlay** | Dialog, menu, or transient drawer | Elevated surface with restrained shadow |
| **Signal** | Rare branded or selected moment | Controlled terracotta, never a generic semantic state |

### Do

- Merge closely related sections into a continuous Plate.
- Use rules, alignment, and spacing before adding a border.
- Reserve shadows for overlays, menus, drawers, and dragged Capsules.
- Let a chart or table occupy the focal region without a card nested inside another card.

### Avoid

- A grid of equally weighted bordered panels.
- Default shadows on every region.
- Warm beige on every nested surface.
- Terracotta borders around every active control.
- Repeating the Meridian Field as background texture.

---

## 10. Meridian primitives in workstation UI

### Datum

Use a Datum for orientation or selection:

- Selected-row rail
- Current-time marker
- Zero line
- Baseline in a comparison
- Divider between context and evidence

The Datum is normally graphite or slate.

### Field

Use a Field only when every mark encodes data, such as completeness, exposure, source
coverage, scenario outcome, or workflow progress. Never use a Field as ambient panel texture.

### Trace

Use a Trace for movement, dependency, lineage, audit history, reconciliation history, or event
sequence. A Trace should answer how the current state came to exist.

### Plate

Use a Plate for honest comparison. Repeated Plates must share scale, framing, label order,
padding, hierarchy, and annotation position.

### Capsule

Use a Capsule only in a Compose workspace or other genuinely configurable layout. It must
have standard dimensions, a clear function, a defined data contract, predictable controls,
saved state, and a reason to move or replace it.

---

## 11. Evidence region versus dialog

Use the evidence region for inspection and workflow context that should remain visible while the
user works:

- Provenance and source timestamps
- Audit history
- Selected-row detail
- Related documents
- Downstream effects
- Exception reason and resolution history
- Data-state explanation

Use a Dialog for bounded create/edit flows, confirmations, and actions that temporarily require
exclusive attention — `size="sm"` to confirm, `DialogForm size="lg"` for a form takeover. Do not
open a dialog merely to display row detail. (The `Modal*` aliases were removed in 2.0.0; don't write them.)

If the component library exposes a typed Drawer or Sheet, use it. Otherwise, implement the
secondary region as a scoped `<aside>` inside the screen rather than inventing a new global
component API.

---

## 12. Dialog pattern

```jsx
function EntryDialog({ open, editingItem, onClose, onSave }) {
  const [draft, setDraft] = useState({ reference: "", amount: "" });

  useEffect(() => {
    if (!open) return;
    setDraft({
      reference: editingItem?.reference ?? "",
      amount: editingItem?.amount == null ? "" : String(editingItem.amount),
    });
  }, [open, editingItem]);

  const handleSave = () => {
    const amount = Number(draft.amount);
    if (!draft.reference.trim() || !Number.isFinite(amount)) return;

    onSave({
      ...editingItem,
      id: editingItem?.id ?? `JE-${Date.now()}`,
      reference: draft.reference.trim(),
      amount,
    });
  };

  return (
    <DialogForm
      open={open}
      onClose={onClose}
      size="lg"
      title={editingItem ? "Edit journal entry" : "New journal entry"}
      submitLabel={editingItem ? "Save changes" : "Create entry"}
      onSubmit={handleSave}
    >
      <FormGrid cols={2}>
          <FormRow label="Reference">
            <Input
              value={draft.reference}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  reference: event.target.value,
                }))
              }
            />
          </FormRow>
          <FormRow label="Amount (USD)">
            <Input
              value={draft.amount}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  amount: event.target.value,
                }))
              }
            />
          </FormRow>
      </FormGrid>
    </DialogForm>
  );
}
```

### Rules

- Titles name the object and action precisely.
- The primary button uses a verb and reflects the consequence.
- Destructive actions use semantic red, explicit language, and confirmation when the impact is
  material. Never use Meridian terracotta as the destructive color.
- Width is the `size` prop, never a second overlay component or a local CSS override.
- Keep validation near the affected field and preserve entered data after an error.
- Initial focus, focus trapping, `Esc`, and focus restoration are required.

`ToastProvider` is a self-contained, no-props, no-children toast stack. Mount it as a sibling at
the end of the tree, never as a wrapper:

```jsx
<ToastProvider />
```

Trigger a toast imperatively:

```js
window.MeridianToast.success("Entry created", "JE-1064");
```

Toast copy should state the completed action. Avoid generic messages such as “Success!”

---

## 13. Scoped CSS injection

Each screen injects its own scoped CSS once, under a workstation-specific style ID (the generic
ID `module-css` collides when several screens mount in one runtime). Scope every rule under the
workstation root and alias the tokens once at the top — never scatter literals through the rules.

Style **inside** the work plane only: frame geometry (viewport height, chrome rows, the scroll
container, the evidence rail) belongs to `WorkstationShell`, and row heights, control heights,
and gaps come from the density tokens. A local `height: 100vh`, a second `<main>`, or a hard-coded
row height is a defect, not a tweak.

```js
(function injectCss() {
  const STYLE_ID = "accounting-workstation-css";
  if (document.getElementById(STYLE_ID)) return;

  const el = document.createElement("style");
  el.id = STYLE_ID;
  el.textContent = `
    .acct-workspace {
      --ws-plate: var(--surface-plate, #F8F7F3);
      --ws-well: var(--surface-well, #FCFBF8);
      --ws-recessed: var(--surface-recessed, #EBE8E1);
      --ws-rule-subtle: var(--rule-subtle, #E2DED5);
      --ws-ink-primary: var(--ink-primary, var(--text-primary, #20252B));
      --ws-ink-secondary: var(--ink-secondary, var(--text-secondary, #535C65));
      --ws-ink-muted: var(--ink-muted, var(--text-muted, #7A828A));
      --ws-axis: var(--brand-axis, #29323A);
      --ws-focus: var(--interaction-focus, #2D6FB3);
      --ws-selected: var(--interaction-hover, #DDE5EC);

      height: 100%;
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      gap: var(--space-lg, 16px);
      padding: var(--space-lg, 16px);
      color: var(--ws-ink-primary);
      font-variant-numeric: tabular-nums lining-nums;
    }

    .acct-workspace__header {
      display: flex;
      align-items: flex-start;
      gap: var(--space-xl, 24px);
    }

    .acct-workspace__eyebrow {
      margin: 0 0 4px;
      color: var(--ws-ink-muted);
      font: 600 10px/14px var(--font-body);
      letter-spacing: 0.04em;
    }

    .acct-workspace h1 {
      margin: 0;
      font: 500 28px/34px var(--font-display);
    }

    .acct-workspace__context {
      margin: 6px 0 0;
      color: var(--ws-ink-secondary);
      font: 400 13px/18px var(--font-body);
    }

    .acct-workspace__actions {
      margin-left: auto;
      display: flex;
      align-items: center;
      gap: var(--space-sm, 8px);
      flex: none;
    }

    .acct-workspace .mdn-work-plane {
      flex: 1;
      min-height: 0;
      display: flex;
      flex-direction: column;
      background: var(--ws-plate);
    }

    .acct-workspace .mdn-analytical-well {
      flex: 1;
      min-height: 0;
      overflow: auto;
      background: var(--ws-well);
    }

    .acct-workspace [data-selected="true"] {
      background: var(--ws-selected);
      box-shadow: inset 2px 0 0 var(--ws-axis);
    }

    .acct-workspace :where(
      button, a, input, select, textarea, [tabindex]:not([tabindex="-1"])
    ):focus-visible {
      outline: 2px solid var(--ws-focus);
      outline-offset: 2px;
    }

    @media (max-width: 1023px) {
      .acct-workspace__header { flex-direction: column; }
      .acct-workspace__actions { width: 100%; margin-left: 0; }
    }

    @media (prefers-reduced-motion: reduce) {
      .acct-workspace *,
      .acct-workspace *::before,
      .acct-workspace *::after {
        scroll-behavior: auto;
        transition-duration: 0.01ms;
        animation-duration: 0.01ms;
        animation-iteration-count: 1;
      }
    }
  `;

  document.head.appendChild(el);
})();

### CSS rules

- Namespace every selector under the workstation root.
- Centralize fallback values in one alias block; do not scatter hard-coded colors through the
  component rules.
- Never use `!important` — fix cascade order or specificity (the governance checker fails on it).
- Prefer semantic tokens such as surface, rule, ink, interaction, and state roles.
- Do not locally recolor `Button` primary to terracotta.
- Use 1px rules for ordinary structure and 2px for focus, selected Datum rails, or critical
  boundaries.
- Avoid default card shadows and excessive radius.
- Keep operational data tabular and lining.

---

## 14. Seed-data contract

Always provide **3–6 realistic examples** near the top of `screen.jsx`. Seed data must exercise
more than the happy path: include at least one provisional, stale, estimated, missing,
exception, overridden, or reconciled state when the domain supports it.

```js
const SEED_DATA = [
  {
    id: "JE-1060",
    date: "2026-06-30",
    reference: "Revenue accrual",
    amount: 184250,
    currency: "USD",
    postingStatus: "posted",
    dataState: "confirmed",
    asOf: "2026-06-30T14:09:55Z",
    source: "ERP · Revenue subledger",
  },
  {
    id: "JE-1061",
    date: "2026-06-30",
    reference: "Custody fee estimate",
    amount: -18420,
    currency: "USD",
    postingStatus: "draft",
    dataState: "estimated",
    asOf: "2026-06-30T13:58:12Z",
    source: "Custodian estimate",
  },
  {
    id: "JE-1062",
    date: "2026-06-30",
    reference: "FX translation reserve",
    amount: 72500,
    currency: "USD",
    postingStatus: "draft",
    dataState: "provisional",
    asOf: "2026-06-30T12:41:03Z",
    source: "Treasury rates · ERP",
  },
  {
    id: "JE-1063",
    date: "2026-06-30",
    reference: "Broker receivable",
    amount: null,
    currency: "USD",
    postingStatus: "blocked",
    dataState: "missing_required",
    asOf: "2026-06-30T10:15:00Z",
    source: "Prime broker feed",
  },
  {
    id: "JE-1064",
    date: "2026-06-30",
    reference: "Intercompany elimination",
    amount: 0,
    currency: "USD",
    postingStatus: "posted",
    dataState: "reconciled",
    asOf: "2026-06-30T14:21:44Z",
    source: "Consolidation engine",
  },
];
```

### Data rules

- `0` is a valid value. Do not render it as missing.
- `null` requires an explicit data state such as `missing_required` or `not_applicable`.
- Preserve source and as-of fields for important values.
- Use ISO-like identifiers and dates internally; format them for display.
- Label illustrative data in demos and exports.
- Do not silently convert provisional or estimated data into a confirmed visual treatment.

---

## 15. Domain data-state model

**Ship the ladder, always.** Every template wraps its dominant work plane in `DataStateSwitch`
wired to the `dataState` tweak, so a copy demonstrates loading, empty and error alongside ready.
Use `EmptyState` for the zero-data case *inside* an otherwise populated screen; use
`DataStateSwitch` when the whole surface is in one state.

Generic UI states are not enough for Meridian. Important data should be able to express the
following conditions.

| State | Required treatment |
|---|---|
| **Confirmed** | Solid marker or explicit confirmed status; no warning ornament |
| **Provisional** | Outlined marker plus the word “Provisional” |
| **Estimated** | `Est.` adjacent to the value |
| **Stale** | Clock/freshness cue plus explicit age, such as `Stale 18m` |
| **Overridden** | Override label, actor, timestamp, and visible source change |
| **Missing required** | `Missing` label and exception reason; never a blank cell |
| **Not applicable** | Em dash (`—`) |
| **Pending** | Pending label plus expected refresh or completion when known |
| **Exception** | Semantic red cue plus a written reason |
| **Reconciled** | Check/confirmation cue plus reconciliation timestamp |

Color is always accompanied by text, iconography, sign, line style, or shape.

---

## 16. State-management pattern

Keep mutable screen state at the root component unless a child is independently reusable.

```js
const [data, setData] = useState(SEED_DATA);
const [currentSection, setCurrentSection] = useState("ledger");
const [currentTab, setCurrentTab] = useState("open");
const [selectedId, setSelectedId] = useState(SEED_DATA[0]?.id ?? null);
const [filters, setFilters] = useState({ query: "", state: "all" });
const [dialogOpen, setDialogOpen] = useState(false);
const [editingItem, setEditingItem] = useState(null);

const selectedItem = useMemo(
  () => data.find((item) => item.id === selectedId) ?? null,
  [data, selectedId]
);
```

### Rules

- Derive filtered rows, selected records, totals, and counts with `useMemo` rather than storing
  duplicate state.
- Keep selected-row state independent from keyboard focus.
- Preserve filters and selected context after create/edit actions where practical.
- Save workspace state only when it is meaningful and stable; do not persist transient dialog
  state.
- For multi-template suites, keep route identity in one shared map.

---

## 17. Table pattern

Tables are Meridian’s primary operational primitive.

### Required behavior

- Sticky headers
- Frozen identifiers where appropriate
- Tabular numerals
- Units in column headings
- Visible sort direction
- Column resizing and visibility controls when the component supports them
- Saved table configuration for consequential workflows
- Keyboard row navigation
- Distinct focused and selected states
- Group totals and subtotals
- Explicit zero, missing, stale, estimated, and not-applicable treatments
- Inline provenance access or a selected-record evidence region
- Pagination, virtualization, or another deliberate strategy beyond roughly 20–50 rows,
  depending on row complexity

### Recommended row anatomy

```text
Selection | Identifier | Description | Primary values | Data state | Freshness | Actions
```

### Rules

- The selected row uses a 2px Datum rail, low-opacity tint, and stronger text.
- Do not use zebra striping by default.
- Do not add a border around every cell.
- Keep row height consistent within a workspace.
- Place source detail and audit history in the evidence region rather than widening the table
  indefinitely.
- Align financial values by decimal point where practical.

---

## 18. Financial notation

| Context | Convention |
|---|---|
| Analytical change | `−1.23%` |
| Accounting negative | `(1.23)` |
| Basis points | `32 bp` |
| Yield | `4.35%` |
| Currency context | ISO code in the column heading |
| Large amount | `$28.4M`; exact value in detail/evidence |
| Not applicable | `—` |
| Missing value | `Missing` |
| Estimated value | `4.35% Est.` |
| Stale value | `4.35% · Stale 18m` |

Do not repeat units in every cell when the column heading can carry them.

---

## 19. Visualization rules

Use the Meridian visualization grammar when the dominant work plane contains a chart,
calendar, matrix, or relationship view.

### Series roles

| Series | Treatment |
|---|---|
| Current / actual | Dark ink or deep slate, strongest line, filled marker |
| Prior period | Lighter slate, secondary line |
| Benchmark | Gray dashed line |
| Scenario | Terracotta dashed line with open markers |
| Target | Thin dotted Datum |
| Estimate | Reduced-opacity line plus an explicit estimate label |

### Non-negotiable behavior

- Shared scales for comparable views
- Stronger zero/reference lines than ordinary gridlines
- Direct labels when there are fewer than four series
- Explicit missingness, uncertainty, freshness, and partial coverage
- As-of time and source path
- A non-visual summary or table fallback for complex visualizations
- Orthogonal connectors and stable layered layout for lineage or ownership graphs
- No decorative radial network graph when relationship direction matters
- No dual axis unless the relationship is explicit and unavoidable
- No more than one primary annotation per view

Terracotta may denote a scenario or alternate state; it does not denote negative performance.

---

## 20. StatusBar

```jsx
<StatusBar
  items={[
    { status: "ok", label: "Ledger", value: "balanced" },
    { label: "Exceptions", value: "3 unresolved" },
    { label: "Data", value: "2 sources stale" },
    { label: "Last post", value: "JE-1060 · 14:09:55 UTC" },
    { label: "Period", value: "2026-Q2 open", push: true },
  ]}
/>
```

### Rules

- Use the bar for compact system and workflow state, not as a second dashboard.
- Keep labels short and values precise.
- State age explicitly: `Stale 18m`, not `Recent`.
- Use `push: true` for one right-aligned context cluster.
- Do not use brand signal color for warning or error status.

---

## 21. Geometry — where the numbers live

This document does not carry a size table. Every dimension has exactly one home, and a copy here
would be a fourth answer waiting to go stale:

| Question | Single source of truth |
|---|---|
| Chrome dimensions (topbar, nav rail, status bar) | the shell components themselves — `components/shell/*.jsx` |
| Row / control height, body and label size | the density tokens in `tokens/theme.css` (`--theme-row-height`, `--theme-control-height`) |
| Spacing and gaps | the `--space-*` scale (`tokens/spacing.css`), summarized in `guidelines/TOKEN_REFERENCE.md` |
| Overlay width | `Dialog`'s `size` prop — sm 420 · md 520 · lg 720 |
| A component's own sizes and variants | that component's `.d.ts` |

Do not patch shell dimensions locally to chase a design mockup, and do not hard-code a row
height: update the source component or the token, or use the supported variant.

---

## 22. Density model and compatibility

Meridian’s semantic density modes are:

| Semantic mode | Target row | Target control | Body | Best use |
|---|---:|---:|---:|---|
| **Dense** | 28px | 28px | 12.5–13px | Blotters, accounting, security lists |
| **Standard** | 36px | 36px | 14px | Most analytical workspaces |
| **Review** | 44px | 40px | 15px | Approvals, reports, presentation |

The current host convention uses:

```html
<html data-theme-density="compact">
<html>
<html data-theme-density="spacious">
```

Until the component APIs expose the semantic names directly, treat the host modes as:

```text
compact  → Dense intent
default  → Standard intent
spacious → Review intent
```

Do not introduce unsupported `dense`, `standard`, or `review` prop values. Check each
component’s `.d.ts` and use the variants it actually supports.

---

## 23. Component sizing — no universal T-shirt scale

Meridian does not have one `xs`–`xl` scale shared by every component.

- **Button**: `"sm" | "default" | "lg" | "icon"`.
- **Dialog**: `"sm" | "md" | "lg"` — the only overlay width control.
- Many other controls, including `SegmentedControl`, `ProgressBar`, `Slider`, and `Skeleton`,
  use `"sm" | "md" | "lg"`.
- Table density is controlled by the table implementation, not the Button scale.
- Check the component’s `.d.ts` before using a size or variant.
- Never invent a prop because another component happens to use the same concept name.

---

## 24. Color and action hierarchy

### Brand roles

- **Axis** — structural graphite/ink
- **Field** — relationship and secondary data blue-gray
- **Signal** — restrained terracotta brand moment

### Interaction roles

- Focus — accessible blue focus ring
- Selection — blue-gray tint plus Datum rail
- Primary action — graphite or deep slate
- Hover — neutral or blue-gray surface shift

### Semantic roles

Positive, negative, warning, information, pending, neutral, missing, and exception each use a
separate semantic token and a companion text/icon/shape cue.

### Never

- Use terracotta for negative P&L.
- Use terracotta for errors or destructive actions.
- Make every selected control terracotta.
- Encode state by color alone.
- Introduce per-workstation “brand colors” that override Meridian’s Axis, Field, and Signal
  roles.

If a legacy host still exposes `data-brand="indigo" | "emerald" | "rose"`, smoke-test the
screen for compatibility, but do not use those modes to carry workflow meaning in new Meridian
workstations.

---

## 25. Motion

Use motion to explain causality, not to decorate the interface.

| Motion | Duration | Use |
|---|---:|---|
| Instant | 80ms | Press feedback |
| Fast | 120ms | Hover and small controls |
| Standard | 180ms | Menus, tabs, disclosure |
| Reflow | 240ms | Sorting, filtering, chart reorganization |
| Deliberate | 280ms | Drawers and workspace transitions |

Use four patterns:

- **Reflow** — rows, marks, or Capsules move directly to new positions.
- **Trace** — lineage or dependency reveals from source to output.
- **Reconcile** — compared states converge into a confirmed state.
- **Focus** — nonessential detail recedes while the selected object becomes clearer.

No bounce, elastic motion, glow, or overshoot. Respect `prefers-reduced-motion`.

---

## 26. Responsive behavior

Meridian is a desktop-first workstation. Do not compress every capability into a mobile card
stack.

**Stated non-goal in `templates/`.** No shipped template carries a media query, and that is a
decision, not an omission: a template is a *desktop* starting point, sized by its `$preview`
(1920×1080 for workstations). The breakpoint behaviour below is guidance for the product a consumer
builds from it — do not add breakpoints to a template to satisfy a checklist.

### Large desktop

Full multi-region analytical workspace.

### Standard laptop

Reduced gutters, narrower rail, and collapsible evidence region.

### Below 1024px

Transform to focused task mode:

- One dominant work region
- Evidence before advanced settings
- Collapsible or relocated secondary rail
- No reduction below the dense typography floor
- Horizontal table scrolling only when the information cannot be reorganized honestly

### Mobile

Prioritize alerts, approvals, search, status review, and read-only summaries. Complex plotting,
accounting, and workspace composition should become focused sibling experiences rather than a
shrunk desktop canvas.

---

## 27. Accessibility and interaction requirements

- At least 4.5:1 contrast for standard text.
- Visible 2px focus ring separated from the component edge.
- Minimum 32px interactive target in dense mode.
- Keyboard access for the primary workflow.
- Distinct focus and selection states.
- No color-only status encoding.
- Reduced-motion support.
- Text summary or table fallback for complex charts and relationship views.
- Distinguishable chart markers and line styles without color.
- Meaning preserved at 200% zoom.
- Dialog focus trapping and focus restoration.
- `aria-labelledby` or an equivalent accessible name for the main workspace and major regions.
- Announce dynamic confirmations without relying solely on a visual toast.

---

## 28. Onboarding checklist

### Repository structure

- [ ] Copy `templates/[name]/` into the project.
- [ ] Update the `.dc.html` `@template` name and description.
- [ ] Confirm the `ds-base.js` `base` path at the new depth.
- [ ] Keep the entry point thin and mount one `screen.jsx` component.
- [ ] Compose the frame with `WorkstationShell` — no hand-rolled chrome, no second `<main>`.
- [ ] Load large seed data in the static `<head>` before support.js.

### Product architecture

- [ ] Declare Monitor, Analyze, Inspect, Compare, Reconcile, or Compose.
- [ ] Identify one dominant work plane.
- [ ] Define the supporting evidence region.
- [ ] Use one primary action per operational region.
- [ ] Remove generic cards that do not represent a true Plate, Well, Rail, or Capsule.

### Data trust

- [ ] Add 3–6 realistic seed rows.
- [ ] Include at least one non-happy-path data state.
- [ ] Distinguish zero, missing, not applicable, and no activity.
- [ ] Show source, as-of time, and freshness for important values.
- [ ] Make provisional, estimated, stale, overridden, missing, pending, exception, and
  reconciled states visually distinct where applicable.

### Visual system

- [ ] Use Paper, Plate, Well, Rail, Overlay, and Signal intentionally.
- [ ] Use sentence case for working UI.
- [ ] Keep brand and semantic colors separate.
- [ ] Use graphite/deep slate for primary actions.
- [ ] Use the Meridian mark once in the shell; avoid logo repetition.
- [ ] Use Field only when it encodes data.
- [ ] Standardize every comparison Plate.

### Interaction and QA

- [ ] Test light mode.
- [ ] Test dark mode with `data-theme="dark"` on `<html>`.
- [ ] Test current density compatibility with default, `compact`, and `spacious`.
- [ ] Smoke-test legacy host brand modes only when the host still exposes them.
- [ ] Complete the primary workflow by keyboard.
- [ ] Verify focus, selected, hover, disabled, loading, and data-state treatments.
- [ ] Test at 200% zoom.
- [ ] Test reduced motion.
- [ ] Test below 1024px as focused task mode.
- [ ] Verify dialog focus restoration and toast behavior.
- [ ] Confirm no important meaning depends on color alone.

---

## 29. Meridian workstation quality gate

A template is ready for review only when every answer below is “yes.”

1. Is the workspace archetype explicit?
2. Is there one clearly dominant work region?
3. Does each surface have a defined Paper, Plate, Well, Rail, Overlay, or Signal role?
4. Are closely related regions combined before new borders are added?
5. Is there only one primary action per operational region?
6. Are brand and semantic colors separated?
7. Are important values accompanied by state, source, freshness, or as-of context?
8. Are zero, missing, no activity, and not applicable distinguishable?
9. Are comparable objects shown with shared geometry and scale?
10. Does selection use a persistent Datum rather than a decorative accent border?
11. Does every Field encode real information?
12. Is selected-record evidence available without losing the main workspace?
13. Does the screen remain understandable without motion?
14. Can the primary workflow be completed by keyboard?
15. Does the layout remain intentional in dense/default host mode?
16. Does the narrow layout become a focused task flow rather than a card stack?
17. Is the screen recognizable as Meridian without relying on repeated logos?
18. Does the implementation use only supported component props and variants?
19. Is the frame `WorkstationShell` — no hand-rolled chrome, no second `<main>`, no local shell
    dimensions or hard-coded row heights?

---

## 30. What "done" means

A copied template is a **working structural prototype** when the shell is wired, the routes
resolve, the seed data is representative, and the focal work plane functions. That is the start
of the work, not the end: production data integration, accessibility verification, responsive
refinement, full state coverage, and the quality gate in §29 all come after.
