Coordinate one committed selection across tables, charts, graphs, and evidence rails. React owns selection state; each renderer receives `selectedId` and calls `select` on commitment.

```jsx
<LinkedSelectionProvider defaultValue={rows[0]} urlParam="security">
  <HoldingsTable />
  <DurationContribution />
  <EvidenceInspector />
</LinkedSelectionProvider>
```

Hover stays local to the renderer. Click, Enter, or Space commits. Escape clears. Use `urlParam` for shareable state and `storageKey` only for small personal defaults.

**3.2 —** Name a provider with `scope` when one screen hosts more than one linked group, and ask for it by name — no prop drilling, no accidental coupling. `multiple` plus `items` enables ⌘/Ctrl-click toggling and Shift-click range extension; `back()` walks committed history.

```jsx
<LinkedSelectionProvider scope="position" multiple items={positions}>
  <LinkedSelectionProvider scope="lot">
    <PositionsTable />   {/* useLinkedSelection("position") */}
    <TaxLotsTable />     {/* useLinkedSelection("lot") */}
  </LinkedSelectionProvider>
</LinkedSelectionProvider>
```
