The triage surface for reconciliation cases and workflow breaks — one row per case: mono id, summary + category sub-line, `SlaChip`, `SeverityBadge`, assignee (falsy assignee reads "Unassigned" in amber — the queue's call to action). Priority is the 3px left rail: Critical red, High amber, everything else bare.

```jsx
<CaseQueue
  items={cases}                    // ReconciliationCaseSummaryDto-shaped rows
  selectedId={selected} onSelect={setSelected}
  now="2026-07-05T14:32:00Z"       // pins every row's SLA countdown
/>
```

Controlled selection only (listbox: click, ArrowUp/Down) — pair with a detail panel or `Drawer` keyed off `selectedId`. Use it instead of `DenseDataTable` when the unit of work is a *case to resolve*, not a record to scan; past ~200 rows switch to `FilteredDataTable` with custom cells.
