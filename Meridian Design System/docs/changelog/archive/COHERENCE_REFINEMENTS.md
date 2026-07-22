# Meridian Design System v1.6 — Coherence Refinements

> ⚠️ **SUPERSEDED / NOT ADOPTED AS WRITTEN.** The purpose-based reorganization proposed in §1
> (`primitives/`, `layout/`, `forms/`, `patterns/`…) was never carried through. The live
> `components/` tree is organized by domain — `core/`, `data/`, `accounting/`, `charts/`,
> `shell/`, `operations/` — matching `readme.md`'s current inventory. The token-naming direction
> in §2 also predates the current 3-tier model in `guidelines/TOKEN_REFERENCE.md`. Kept for
> historical context only; don't use this as a map of the current folder structure or token names.

## 1. Component Organization (by purpose)

Reorganized from category-based (core/, data/, accounting/) to purpose-based:

```
components/
├── primitives/      Button, Input, Select, Checkbox, Toggle, Badge, Eyebrow
├── layout/          FormRow, FormGrid, FormDivider, Modal*, Tabs, TabPanel, PanelSurface
├── data/            DenseDataTable, FilteredDataTable, ExpandableDataTable, Pagination, EmptyState, ColumnChooser
├── forms/           FieldInput, DatePicker, DateRangePicker, TextArea, NumberInput, FileUpload, FormValidation, FormErrorSummary
├── patterns/        BulkActionBar, BulkSelectCheckbox, SelectionToolbar, ReconciliationPanel, Breadcrumb, Stepper
├── accounting/      AmountCell, LedgerTable, AccountTree, StatementTable, JournalEntryForm, TaxLotTable
├── shell/           WorkstationTopbar, NavRail, StatusBar
└── charts/          CandleChart, EquityCurve, Sparkline, ChartCard
```

**Benefit:** Clearer mental model, IDE autocomplete grouping, easier discovery.

---

## 2. Token Naming Unification

Adopted **semantic name + state** pattern across all tokens:

```css
/* Text — foreground/secondary/muted/disabled */
--text-foreground       (#22272E light, #E6EAF0 dark)
--text-secondary        (#4D5967 light, #B1BAC4 dark)
--text-muted            (#6E7781 light, #8B949E dark)
--text-disabled         (#9AA4AF light, #6E7681 dark)

/* Background — canvas/surface/hover/active/disabled */
--bg-canvas             (#ECEFF3 light, #0F1117 dark)
--bg-surface            (#FFFFFF light, #1C2128 dark)
--bg-hover              (#F1F4F7 light, #262D36 dark)
--bg-active             (#E6EEF5 light, #3D444D dark)
--bg-disabled           (#F5F7FA light, #2D333B dark)

/* Borders — default/focus/strong/disabled */
--border-default        (#D7DCE2 light, #444C56 dark)
--border-focus          (#2F6F8F light, #58A6FF dark)
--border-strong         (#AAB4BF light, #6E7681 dark)
--border-disabled       (#E6EAF0 light, #333940 dark)

/* Semantic — always vibrant */
--color-success         (#158055 light, #3FB950 dark)
--color-error           (#BA3F55 light, #F85149 dark)
--color-warning         (#C5881A light, #D29922 dark)
--color-info            (#2F6F8F light, #58A6FF dark)

/* Accent (white-label) */
--accent-primary        (varies by brand)
--accent-hover          (lighter/darker variant)
--accent-active         (pressed state)
```

**Benefit:** Self-documenting, predictable, easier onboarding.

---

## 3. Workstation Template Blueprint

Standardized structure for all workstations:

```
Template structure (always):
├── <WorkstationTopbar moduleLabel="" environment="" clock="" brandSrc="" />
├── <NavRail activeId="" sections={[...]} />
├── <main>
│   ├── Header (title + primary CTA + environment badge)
│   ├── Tabs (optional, if multi-section)
│   ├── SearchBar + Filters (optional, if data-heavy)
│   ├── [Content: Cards/Tables/Forms]
│   └── Pagination (if table, >20 rows)
└── <StatusBar items={[...]} />
└── <Modal> (for create/edit)
```

**Patterns:**
- **Topbar:** Always shows module label, environment (PAPER/LIVE/FIXTURE), UTC clock, brand mark
- **NavRail:** Grouped menu items with icons
- **Header:** `<h1>` module name, `<Badge>` environment, primary `<Button>` for main action
- **Tabs:** T-shirt sizing, count badges, clear active state
- **Search:** Debounced input + dropdown filters
- **Data:** DenseDataTable with sorting, filtering, pagination
- **Status bar:** Real-time indicators (connection, last action, period, mode)
- **Modal:** For create/edit with form validation

**Example seed data:** Always 3–5 realistic rows/items

**Benefit:** New workstations scaffold in 30 minutes, consistent UX across all screens.

---

## Implementation Notes

- Old folder structure preserved for backward compatibility (with deprecation notices)
- All imports updated to new paths
- Token names aliased in theme.css for gradual migration
- Workstation template added to guidelines/
