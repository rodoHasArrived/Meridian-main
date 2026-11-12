The persistent analytical context immediately below application chrome. It carries entity, book/basis, period, as-of, currency, scenario, data condition, and period lock so every chart, table, drawer, export, and saved view agrees on scope.

```jsx
<WorkspaceContextProvider defaultValue={{entity:{type:"Portfolio",label:"General Account"},book:"STAT",period:"2026-07",currency:"USD"}}>
  <WorkspaceContextBar onPeriodClick={openPeriodPicker} />
  <Workspace />
</WorkspaceContextProvider>
```

Use the provider as the canonical React-owned context. Committed changes should be URL-backed by the consuming application when views are shareable.

**3.2 —** The bar measures itself: `collapse="auto"` (the default) folds low-priority facets into a `More +N` disclosure as width runs out — scenario → currency → book → as-of → period. Entity, data condition, and a locked period never collapse, because an operator who cannot see them has no context at all. Use `collapse="always"` with `minVisible` for a fixed header height regardless of entity-name length, `collapse="never"` to keep the 3.1.0 scroll behaviour. Do not add breakpoints around it; print restores every facet.
